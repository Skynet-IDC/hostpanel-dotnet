using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Infrastructure.Services.Migration;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/migration")]
[Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
public class MigrationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PleskMigrationService _migration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MigrationController> _logger;

    public MigrationController(
        AppDbContext db,
        PleskMigrationService migration,
        IServiceScopeFactory scopeFactory,
        ILogger<MigrationController> logger)
    {
        _db = db;
        _migration = migration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ── Sources ────────────────────────────────────────────────────────

    [HttpGet("sources")]
    public async Task<IActionResult> ListSources(CancellationToken ct) =>
        Ok(await _db.MigrationSources.AsNoTracking()
            .Select(s => new {
                s.Id, s.Name, s.Host, s.PleskApiPort, s.SshUser, s.SshPort,
                s.DnsProvider, s.IsActive, s.CreatedAt
            })
            .ToListAsync(ct));

    [HttpGet("sources/{id:int}")]
    public async Task<IActionResult> GetSource(int id, CancellationToken ct)
    {
        var s = await _db.MigrationSources.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new {
                x.Id, x.Name, x.Host, x.PleskApiPort, x.SshUser, x.SshPort,
                x.SshKeyPath, x.DnsProvider, x.IsActive, x.CreatedAt, x.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);
        return s is null ? NotFound() : Ok(s);
    }

    public record CreateSourceRequest(
        string Name, string Host, int? PleskApiPort, string? SshUser, int? SshPort,
        string? SshKeyPath, string PleskApiKey, string? DnsProvider);

    [HttpPost("sources")]
    public async Task<IActionResult> CreateSource([FromBody] CreateSourceRequest req, CancellationToken ct)
    {
        var src = new MigrationSource
        {
            Name = req.Name,
            Host = req.Host,
            PleskApiPort = req.PleskApiPort ?? 8443,
            SshUser = req.SshUser ?? "root",
            SshPort = req.SshPort ?? 22,
            SshKeyPath = req.SshKeyPath ?? "/etc/hostpanel/plesk-migration-key",
            PleskApiKey = req.PleskApiKey,
            DnsProvider = req.DnsProvider ?? "cloudflare",
            IsActive = true
        };
        _db.MigrationSources.Add(src);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetSource), new { id = src.Id }, new { src.Id });
    }

    [HttpDelete("sources/{id:int}")]
    public async Task<IActionResult> DeleteSource(int id, CancellationToken ct)
    {
        var src = await _db.MigrationSources.FindAsync(new object?[] { id }, ct);
        if (src is null) return NotFound();
        _db.MigrationSources.Remove(src);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Discovery ──────────────────────────────────────────────────────

    [HttpPost("sources/{id:int}/discover")]
    public async Task<IActionResult> Discover(int id, CancellationToken ct)
    {
        var src = await _db.MigrationSources.FindAsync(new object?[] { id }, ct);
        if (src is null) return NotFound();

        var inventory = await _migration.DiscoverAsync(src, ct);
        return Ok(inventory);
    }

    // ── Preflight ──────────────────────────────────────────────────────

    public record PreflightRequest(string[]? Domains);

    [HttpPost("sources/{id:int}/preflight")]
    public async Task<IActionResult> Preflight(int id, [FromBody] PreflightRequest? req, CancellationToken ct)
    {
        var src = await _db.MigrationSources.FindAsync(new object?[] { id }, ct);
        if (src is null) return NotFound();

        var report = await _migration.RunPreflightAsync(src, req?.Domains, ct);
        return Ok(report);
    }

    // ── Jobs ───────────────────────────────────────────────────────────

    public record CreateJobsRequest(
        int MigrationSourceId,
        string[] Domains,
        string TargetUserId,
        int ServicePlanId);

    [HttpPost("jobs")]
    public async Task<IActionResult> CreateJobs([FromBody] CreateJobsRequest req, CancellationToken ct)
    {
        var src = await _db.MigrationSources.FindAsync(new object?[] { req.MigrationSourceId }, ct);
        if (src is null) return BadRequest(new { error = "MigrationSource not found" });

        var jobs = new List<MigrationJob>();
        foreach (var d in req.Domains.Distinct())
        {
            var job = new MigrationJob
            {
                MigrationSourceId = src.Id,
                SourceDomain = d,
                TargetUserId = req.TargetUserId,
                ServicePlanId = req.ServicePlanId,
                State = "preflight-pass"
            };
            _db.MigrationJobs.Add(job);
            jobs.Add(job);
        }
        await _db.SaveChangesAsync(ct);
        return Ok(jobs.Select(j => new { j.Id, j.SourceDomain, j.State }));
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> ListJobs(CancellationToken ct) =>
        Ok(await _db.MigrationJobs.AsNoTracking()
            .OrderByDescending(j => j.Id)
            .Select(j => new {
                j.Id, j.MigrationSourceId, j.SourceDomain, j.State,
                j.TargetSubscriptionId, j.StagedAt, j.CutoverAt,
                j.RolledBackAt, j.ErrorMessage, j.CreatedAt
            })
            .ToListAsync(ct));

    [HttpGet("jobs/{id:int}")]
    public async Task<IActionResult> GetJob(int id, CancellationToken ct)
    {
        var j = await _db.MigrationJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j is null) return NotFound();

        var logTail = j.MigrationLog;
        if (logTail.Length > 8000) logTail = "...\n" + logTail[^8000..];

        return Ok(new
        {
            j.Id, j.MigrationSourceId, j.SourceDomain, j.State,
            j.TargetSubscriptionId, j.TargetUserId, j.ServicePlanId,
            j.StagedAt, j.CutoverAt, j.RolledBackAt, j.ErrorMessage,
            j.CreatedAt, j.UpdatedAt,
            discovery = SafeParse(j.DiscoveryJson),
            preflight = SafeParse(j.PreflightReport),
            logTail
        });
    }

    [HttpPost("jobs/{id:int}/stage")]
    public IActionResult Stage(int id)
    {
        // Fire and forget; create a fresh DI scope so the long-running task
        // has its own DbContext + PleskMigrationService (the controller's
        // scoped services are disposed when this method returns).
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<PleskMigrationService>();
            var log = scope.ServiceProvider.GetRequiredService<ILogger<MigrationController>>();
            try { await svc.StageJobAsync(id, CancellationToken.None); }
            catch (Exception ex) { log.LogError(ex, "Background stage failed for job {Id}", id); }
        });
        return Accepted(new { id, started = true });
    }

    [HttpPost("jobs/{id:int}/cutover")]
    public async Task<IActionResult> Cutover(int id, CancellationToken ct)
    {
        try
        {
            var r = await _migration.CutoverJobAsync(id, ct);
            return Ok(r);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public record BulkCutoverRequest(int[] JobIds, string ConfirmationToken);

    [HttpPost("jobs/bulk-cutover")]
    public async Task<IActionResult> BulkCutover([FromBody] BulkCutoverRequest req, CancellationToken ct)
    {
        if (req.ConfirmationToken != "I_UNDERSTAND_THIS_AFFECTS_LIVE_TRAFFIC")
            return BadRequest(new { error = "Confirmation token missing or invalid" });

        var results = new List<object>();
        foreach (var id in req.JobIds.Distinct())
        {
            try
            {
                var r = await _migration.CutoverJobAsync(id, ct);
                results.Add(new { id, ok = true, result = r });
            }
            catch (Exception ex)
            {
                results.Add(new { id, ok = false, error = ex.Message });
            }
        }
        return Ok(results);
    }

    public record RollbackRequest(string SourceIp);

    [HttpPost("jobs/{id:int}/rollback")]
    public async Task<IActionResult> Rollback(int id, [FromBody] RollbackRequest req, CancellationToken ct)
    {
        try
        {
            var r = await _migration.RollbackJobAsync(id, req.SourceIp, ct);
            return Ok(r);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("jobs/{id:int}/validation")]
    public async Task<IActionResult> GetValidation(int id, CancellationToken ct)
    {
        var j = await _db.MigrationJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j is null) return NotFound();
        if (string.IsNullOrEmpty(j.SourceDomain)) return BadRequest(new { error = "Job has no source domain" });

        // Validation is part of cutover; replay it here read-only via agent
        return Ok(new { jobId = id, state = j.State, message = "Validation runs during /cutover; check job state" });
    }

    private static JsonNode? SafeParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try { return JsonNode.Parse(json); }
        catch { return null; }
    }
}
