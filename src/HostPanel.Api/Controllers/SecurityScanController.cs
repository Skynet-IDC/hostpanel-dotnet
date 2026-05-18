using System.Collections.Concurrent;
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
