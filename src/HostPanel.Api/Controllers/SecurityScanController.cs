using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Api.Middleware;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public class SecurityScanController : ControllerBase
{
    private readonly IServerAgent _agent;
    private readonly TenantAuthorizationService _auth;
    private readonly AppDbContext _db;

    // Bug #44: ClamAV scans on a tenant docroot can take minutes — way past the
    // nginx/proxy default of 60s. We now kick the scan off in a background task,
    // return 202 with a job id, and let the client poll /scan/jobs/{id} for status.
    // Jobs are kept in-memory (lost on API restart, which is fine — they're disposable);
    // ttl 30 min so the dictionary doesn't grow forever.
    private static readonly ConcurrentDictionary<string, ScanJob> _jobs = new();
    private static readonly TimeSpan _jobTtl = TimeSpan.FromMinutes(30);

    public SecurityScanController(IServerAgent agent, TenantAuthorizationService auth, AppDbContext db)
    {
        _agent = agent;
        _auth = auth;
        _db = db;
    }

    /// <summary>
    /// ClamAV scan on a domain's document root. Returns 202 immediately with a job id;
    /// poll <c>GET /scan/jobs/{id}</c> for results. Pass <c>?wait=true</c> to keep the
    /// legacy synchronous behavior (small docroots only — caps at 60s).
    /// </summary>
    [HttpPost("domains/{domainId}/scan")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> ScanDomain(int domainId, [FromQuery] bool wait = false)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var domain = await _db.Domains.FindAsync(domainId);
        if (domain is null) return NotFound();

        if (wait)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await _agent.ExecuteAsync(new AgentRequest
            {
                Method = "security.clamScan",
                Params = new Dictionary<string, object?> { ["docRoot"] = domain.DocumentRoot }
            }, cts.Token);
            return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
        }

        var jobId = Guid.NewGuid().ToString("N");
        var job = new ScanJob(jobId, "running", DateTime.UtcNow, null, null, null);
        _jobs[jobId] = job;
        EvictExpiredJobs();

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
                var result = await _agent.ExecuteAsync(new AgentRequest
                {
                    Method = "security.clamScan",
                    Params = new Dictionary<string, object?> { ["docRoot"] = domain.DocumentRoot }
                }, cts.Token);
                _jobs[jobId] = job with
                {
                    Status = result.IsSuccess ? "completed" : "failed",
                    CompletedAt = DateTime.UtcNow,
                    Result = result.Result?.Data,
                    Error = result.IsSuccess ? null : result.Error?.Message,
                };
            }
            catch (Exception ex)
            {
                _jobs[jobId] = job with { Status = "failed", CompletedAt = DateTime.UtcNow, Error = ex.Message };
            }
        });

        return Accepted(new { jobId, status = "running", pollUrl = $"/api/v1/scan/jobs/{jobId}" });
    }

    [HttpGet("scan/jobs/{jobId}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller,Customer")]
    public IActionResult GetScanJob(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return NotFound(new { message = "Unknown job — either never existed or expired (30min TTL)." });
        return Ok(new
        {
            jobId = job.Id,
            status = job.Status,
            startedAt = job.StartedAt,
            completedAt = job.CompletedAt,
            result = job.Result,
            error = job.Error,
        });
    }

    private static void EvictExpiredJobs()
    {
        var cutoff = DateTime.UtcNow - _jobTtl;
        foreach (var (k, v) in _jobs)
        {
            if (v.CompletedAt.HasValue && v.CompletedAt.Value < cutoff)
                _jobs.TryRemove(k, out _);
        }
    }

    private record ScanJob(
        string Id,
        string Status,
        DateTime StartedAt,
        DateTime? CompletedAt,
        Dictionary<string, object?>? Result,
        string? Error
    );

    /// <summary>Fail2Ban status and management.</summary>
    [HttpGet("security/fail2ban")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> Fail2BanStatus()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "security.fail2banStatus",
            Params = []
        });
        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }

    // ---- Malware scan history + findings (file-backed) ----
    // Scan + finding records are persisted as JSON in /var/lib/hostpanel/malware-*.json
    // to keep this feature in step with the SecurityScan API surface without forcing a
    // brand-new EF migration. The volume of records is tiny (one scan = a few hundred
    // bytes at most), so disk is fine.
    private const string ScanStorePath = "/var/lib/hostpanel/malware-scans.json";
    private const string FindingStorePath = "/var/lib/hostpanel/malware-findings.json";

    [HttpGet("security/malware/scans")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> ListMalwareScans()
    {
        var scans = await ReadScansAsync();
        return Ok(scans.OrderByDescending(s => s.StartedAt).Take(50));
    }

    [HttpGet("security/malware/findings")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> ListMalwareFindings()
    {
        // Active findings only — quarantined ones drop out so the list reads as
        // "things I still need to act on". Quarantined files are still on disk
        // under /var/quarantine for forensics.
        var findings = await ReadFindingsAsync();
        return Ok(findings.Where(f => f.Status != "quarantined").OrderByDescending(f => f.DetectedAt).Take(100));
    }

    [HttpPost("security/malware-scan")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> ScanPath([FromBody] MalwareScanPathRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return BadRequest(new { message = "path required" });
        if (!request.Path.StartsWith("/var/www/", StringComparison.Ordinal))
            return BadRequest(new { message = "scan path must be under /var/www/" });

        var startedAt = DateTime.UtcNow;
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.SecurityClamScan,
            Params = new Dictionary<string, object?> { ["docRoot"] = request.Path }
        });
        var completedAt = DateTime.UtcNow;

        var data = result.Result?.Data ?? new Dictionary<string, object?>();
        var infectedLines = ParseInfectedList(data);
        var domainName = DomainFromPath(request.Path);

        var scans = await ReadScansAsync();
        var scan = new MalwareScanRecord(
            Id: NextId(scans),
            DomainName: domainName,
            Status: result.IsSuccess ? "completed" : "failed",
            FilesScanned: -1, // clamscan with --no-summary doesn't report this; -1 = unknown
            ThreatsFound: infectedLines.Count,
            StartedAt: startedAt,
            CompletedAt: completedAt
        );
        scans.Add(scan);
        await WriteScansAsync(scans);

        if (infectedLines.Count > 0)
        {
            var findings = await ReadFindingsAsync();
            foreach (var line in infectedLines)
            {
                // clamscan output line: "/var/www/.../infected.php: Threat.Name FOUND"
                var colonIdx = line.LastIndexOf(": ");
                var foundIdx = line.LastIndexOf(" FOUND");
                if (colonIdx <= 0 || foundIdx <= colonIdx) continue;
                var filePath = line[..colonIdx];
                var threatName = line.Substring(colonIdx + 2, foundIdx - colonIdx - 2);
                findings.Add(new MalwareFindingRecord(
                    Id: NextId(findings),
                    ScanId: scan.Id,
                    FilePath: filePath,
                    ThreatName: threatName,
                    Severity: SeverityFor(threatName),
                    Status: "detected",
                    DetectedAt: completedAt
                ));
            }
            await WriteFindingsAsync(findings);
        }

        return Ok(scan);
    }

    [HttpPost("security/malware/findings/{id}/quarantine")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> QuarantineFinding(int id)
    {
        var findings = await ReadFindingsAsync();
        var finding = findings.FirstOrDefault(f => f.Id == id);
        if (finding is null) return NotFound();
        if (finding.Status == "quarantined")
            return Ok(finding); // idempotent

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.SecurityQuarantineFile,
            Params = new Dictionary<string, object?> { ["path"] = finding.FilePath }
        });

        if (!result.IsSuccess)
            return StatusCode(500, new { message = result.Error?.Message ?? "Quarantine failed" });

        var updated = finding with { Status = "quarantined" };
        var idx = findings.FindIndex(f => f.Id == id);
        findings[idx] = updated;
        await WriteFindingsAsync(findings);
        return Ok(updated);
    }

    // ---- helpers (file persistence + parsing) ----

    private static readonly SemaphoreSlim _malwareIoLock = new(1, 1);

    private static async Task<List<MalwareScanRecord>> ReadScansAsync()
    {
        await _malwareIoLock.WaitAsync();
        try
        {
            if (!System.IO.File.Exists(ScanStorePath)) return new();
            var json = await System.IO.File.ReadAllTextAsync(ScanStorePath);
            return JsonSerializer.Deserialize<List<MalwareScanRecord>>(json) ?? new();
        }
        finally { _malwareIoLock.Release(); }
    }

    private static async Task WriteScansAsync(List<MalwareScanRecord> scans)
    {
        await _malwareIoLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScanStorePath)!);
            await System.IO.File.WriteAllTextAsync(ScanStorePath, JsonSerializer.Serialize(scans));
        }
        finally { _malwareIoLock.Release(); }
    }

    private static async Task<List<MalwareFindingRecord>> ReadFindingsAsync()
    {
        await _malwareIoLock.WaitAsync();
        try
        {
            if (!System.IO.File.Exists(FindingStorePath)) return new();
            var json = await System.IO.File.ReadAllTextAsync(FindingStorePath);
            return JsonSerializer.Deserialize<List<MalwareFindingRecord>>(json) ?? new();
        }
        finally { _malwareIoLock.Release(); }
    }

    private static async Task WriteFindingsAsync(List<MalwareFindingRecord> findings)
    {
        await _malwareIoLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FindingStorePath)!);
            await System.IO.File.WriteAllTextAsync(FindingStorePath, JsonSerializer.Serialize(findings));
        }
        finally { _malwareIoLock.Release(); }
    }

    private static int NextId<T>(List<T> rows) where T : IMalwareRow => rows.Count == 0 ? 1 : rows.Max(r => r.Id) + 1;

    // ClamScanHandler returns `infected` as a JSON-encoded string of lines (legacy decision
    // when the handler was added). Tolerate both that shape AND a real array in case it's
    // ever fixed upstream.
    private static List<string> ParseInfectedList(Dictionary<string, object?> data)
    {
        if (!data.TryGetValue("infected", out var raw) || raw is null) return new();
        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Array)
                return je.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (je.ValueKind == JsonValueKind.String)
            {
                var s = je.GetString();
                if (string.IsNullOrWhiteSpace(s)) return new();
                try { return JsonSerializer.Deserialize<List<string>>(s) ?? new(); } catch { return new(); }
            }
        }
        if (raw is string str)
        {
            try { return JsonSerializer.Deserialize<List<string>>(str) ?? new(); } catch { return new(); }
        }
        return new();
    }

    private static string DomainFromPath(string path)
    {
        // /var/www/<systemuser>/[httpdocs|domains/<sub>/]... → return the host-ish part
        var trimmed = path.TrimEnd('/');
        var parts = trimmed.Split('/');
        // /var/www = parts[0..2] empty/var/www; parts[3] is the system user, parts[4]+ is path inside
        if (parts.Length >= 4 && parts[1] == "var" && parts[2] == "www")
            return parts[3];
        return trimmed;
    }

    private static string SeverityFor(string threatName)
    {
        // clamav threat names follow Family.Name pattern. Use a tiny heuristic so the
        // UI can colour-code without backend knowing the entire AV taxonomy.
        var lower = threatName.ToLowerInvariant();
        if (lower.Contains("trojan") || lower.Contains("ransom") || lower.Contains("rootkit") || lower.Contains("backdoor"))
            return "high";
        if (lower.Contains("adware") || lower.Contains("pup"))
            return "low";
        return "medium";
    }

    [HttpPost("security/fail2ban/unban")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> Fail2BanUnban([FromBody] UnbanRequest request)
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "security.fail2banUnban",
            Params = new Dictionary<string, object?>
            {
                ["jail"] = request.Jail,
                ["ip"] = request.Ip
            }
        });
        return Ok(new { success = result.IsSuccess });
    }

    /// <summary>Full server malware scan.</summary>
    [HttpPost("security/scan-server")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> ScanServer()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "security.clamScanAll",
            Params = []
        });
        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }
}

public record UnbanRequest(string Jail, string Ip);

public record MalwareScanPathRequest(string Path);

// Marker so NextId<T> can resolve the .Id property without reflection.
public interface IMalwareRow { int Id { get; } }
public record MalwareScanRecord(
    int Id, string DomainName, string Status, int FilesScanned, int ThreatsFound,
    DateTime StartedAt, DateTime? CompletedAt) : IMalwareRow;
public record MalwareFindingRecord(
    int Id, int ScanId, string FilePath, string ThreatName, string Severity,
    string Status, DateTime DetectedAt) : IMalwareRow;
