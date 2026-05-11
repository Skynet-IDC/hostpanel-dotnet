using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Infrastructure.Services;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/ha")]
[Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
public class HighAvailabilityController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ServerAgentFactory _agentFactory;
    private readonly ILogger<HighAvailabilityController> _logger;

    public HighAvailabilityController(AppDbContext db, ServerAgentFactory agentFactory, ILogger<HighAvailabilityController> logger)
    {
        _db = db;
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <summary>Get HA cluster status - which servers are primary, which are standby.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var servers = await _db.Set<Server>()
            .OrderBy(s => s.Hostname)
            .ToListAsync();

        var statuses = new List<object>();
        foreach (var server in servers)
        {
            var agent = _agentFactory.GetAgent(server);
            var healthy = await agent.IsAvailableAsync();

            server.IsHealthy = healthy;
            server.LastHealthCheck = DateTime.UtcNow;

            statuses.Add(new
            {
                server.Id, server.Hostname, server.IpAddress,
                server.IsLocal, server.IsActive, server.IsHealthy,
                server.ServerGroup, server.LastHealthCheck,
                role = server.IsLocal ? "primary" : (server.IsActive ? "standby" : "disabled")
            });
        }

        await _db.SaveChangesAsync();
        return Ok(statuses);
    }

    /// <summary>
    /// Configure a server as a failover standby for the primary.
    /// Sets up DNS failover and site replication.
    /// </summary>
    [HttpPost("configure-failover")]
    public async Task<IActionResult> ConfigureFailover([FromBody] FailoverConfigRequest request)
    {
        var primary = await _db.Set<Server>().FirstOrDefaultAsync(s => s.IsLocal);
        var standby = await _db.Set<Server>().FindAsync(request.StandbyServerId);

        if (primary is null || standby is null)
            return BadRequest(new { message = "Primary or standby server not found" });

        if (standby.IsLocal)
            return BadRequest(new { message = "Standby cannot be the local server" });

        standby.IsActive = true;
        standby.ServerGroup = "failover";
        await _db.SaveChangesAsync();

        // Set up replication job on the standby
        var agent = _agentFactory.GetAgent(standby);
        var result = await agent.ExecuteAsync(new AgentRequest
        {
            Method = "ha.configureStandby",
            Params = new Dictionary<string, object?>
            {
                ["primaryIp"] = primary.IpAddress,
                ["syncInterval"] = request.SyncIntervalMinutes,
                ["syncDomains"] = request.SyncDomains,
                ["syncDatabases"] = request.SyncDatabases,
                ["syncEmail"] = request.SyncEmail
            }
        });

        return Ok(new
        {
            success = result.IsSuccess,
            primary = primary.Hostname,
            standby = standby.Hostname,
            message = "Failover configured. Standby will sync on interval."
        });
    }

    /// <summary>Manual failover via HaService.</summary>
    [HttpPost("failover")]
    public async Task<IActionResult> TriggerFailover([FromBody] FailoverRequest request, [FromServices] HaService haService)
    {
        try
        {
            var message = await haService.FailoverAsync(request.PromoteServerId, HttpContext.RequestAborted);
            return Ok(new { success = true, message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Failback: sync changes from current primary (B) back to recovered server (A), then promote A.
    /// </summary>
    [HttpPost("failback")]
    public async Task<IActionResult> TriggerFailback([FromBody] FailbackRequest request, [FromServices] HaService haService)
    {
        try
        {
            var message = await haService.FailbackAsync(request.RecoveredServerId, HttpContext.RequestAborted);
            return Ok(new { success = true, message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Permanent promotion: Make a server the new primary forever (decommission the old primary).
    /// No failback will occur. Old primary's sync state is removed.
    /// </summary>
    [HttpPost("promote-permanent")]
    public async Task<IActionResult> PromotePermanent([FromBody] PermanentPromoteRequest request, [FromServices] HaService haService)
    {
        try
        {
            // Do failover first if not already primary
            var state = await _db.Set<HaSyncState>().FirstOrDefaultAsync(s => s.ServerId == request.ServerId);
            if (state is not null && state.Role != "primary")
                await haService.FailoverAsync(request.ServerId, HttpContext.RequestAborted);

            // Mark as permanent primary
            var allStates = await _db.Set<HaSyncState>().Include(s => s.Server).ToListAsync();

            foreach (var s in allStates)
            {
                if (s.ServerId == request.ServerId)
                {
                    s.Role = "primary";
                    s.AutoFailback = false;
                    s.Server.IsLocal = true;
                    s.Server.IsActive = true;
                }
                else if (request.DecommissionOldPrimary)
                {
                    // Remove old primary from HA entirely
                    s.Role = "decommissioned";
                    s.Server.IsActive = false;
                    s.AutoFailback = false;
                }
                else
                {
                    // Keep as standby pointing to new primary
                    s.Role = "standby";
                }
            }

            await _db.SaveChangesAsync();

            _logger.LogWarning("PERMANENT PROMOTION: Server {Id} is now the permanent primary. Decommission={Decom}",
                request.ServerId, request.DecommissionOldPrimary);

            return Ok(new
            {
                success = true,
                message = $"Server permanently promoted to primary." +
                    (request.DecommissionOldPrimary ? " Old primary decommissioned." : " Old primary demoted to standby.")
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>Get detailed sync state for all HA servers.</summary>
    [HttpGet("sync-state")]
    public async Task<IActionResult> GetSyncState()
    {
        var states = await _db.Set<HaSyncState>()
            .Include(s => s.Server)
            .Select(s => new
            {
                s.ServerId,
                serverHostname = s.Server.Hostname,
                serverIp = s.Server.IpAddress,
                s.Role,
                s.LastSyncAt,
                s.SyncDirection,
                s.AutoFailback,
                s.ConsecutiveFailures,
                s.FailoverTriggeredAt,
                s.FailbackCompletedAt,
                s.SyncFiles,
                s.SyncDatabases,
                s.SyncEmail,
                s.HealthCheckIntervalSeconds,
                s.FailoverAfterMissedChecks,
                isHealthy = s.Server.IsHealthy,
                lastHealthCheck = s.Server.LastHealthCheck
            })
            .ToListAsync();

        return Ok(states);
    }

    /// <summary>Configure HA sync settings for a server.</summary>
    [HttpPut("sync-state/{serverId}")]
    public async Task<IActionResult> UpdateSyncState(int serverId, [FromBody] UpdateSyncStateRequest request)
    {
        var state = await _db.Set<HaSyncState>().FirstOrDefaultAsync(s => s.ServerId == serverId);
        if (state is null)
        {
            state = new HaSyncState { ServerId = serverId };
            _db.Set<HaSyncState>().Add(state);
        }

        if (request.AutoFailback.HasValue) state.AutoFailback = request.AutoFailback.Value;
        if (request.SyncFiles.HasValue) state.SyncFiles = request.SyncFiles.Value;
        if (request.SyncDatabases.HasValue) state.SyncDatabases = request.SyncDatabases.Value;
        if (request.SyncEmail.HasValue) state.SyncEmail = request.SyncEmail.Value;
        if (request.HealthCheckIntervalSeconds.HasValue) state.HealthCheckIntervalSeconds = request.HealthCheckIntervalSeconds.Value;
        if (request.FailoverAfterMissedChecks.HasValue) state.FailoverAfterMissedChecks = request.FailoverAfterMissedChecks.Value;
        if (request.SyncExcludes is not null) state.SyncExcludes = request.SyncExcludes;
        if (request.FailoverStrategy.HasValue) state.FailoverStrategy = request.FailoverStrategy.Value;
        if (request.FloatingIp is not null) state.FloatingIp = request.FloatingIp;
        if (request.CloudflareApiToken is not null) state.CloudflareApiToken = request.CloudflareApiToken;
        if (request.CloudflareZoneId is not null) state.CloudflareZoneId = request.CloudflareZoneId;
        if (request.CloudflareUseLoadBalancer.HasValue) state.CloudflareUseLoadBalancer = request.CloudflareUseLoadBalancer.Value;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Sync state updated" });
    }

    /// <summary>Health check for external monitors.</summary>
    [HttpGet("/api/v1/health")]
    [AllowAnonymous]
    public async Task<IActionResult> HealthCheck([FromServices] IServerAgent agent)
    {
        var agentOk = await agent.IsAvailableAsync();
        if (!agentOk)
            return StatusCode(503, new { status = "unhealthy", agent = "down" });
        return Ok(new { status = "healthy", agent = "up", timestamp = DateTime.UtcNow });
    }
}

public record FailoverConfigRequest(int StandbyServerId, int SyncIntervalMinutes = 15, bool SyncDomains = true, bool SyncDatabases = true, bool SyncEmail = true);
public record FailoverRequest(int PromoteServerId, List<string>? Domains = null);
public record FailbackRequest(int RecoveredServerId);
public record UpdateSyncStateRequest(bool? AutoFailback = null, bool? SyncFiles = null, bool? SyncDatabases = null, bool? SyncEmail = null, int? HealthCheckIntervalSeconds = null, int? FailoverAfterMissedChecks = null, string? SyncExcludes = null, HostPanel.Core.Enums.FailoverStrategy? FailoverStrategy = null, string? FloatingIp = null, string? CloudflareApiToken = null, string? CloudflareZoneId = null, bool? CloudflareUseLoadBalancer = null);
public record PermanentPromoteRequest(int ServerId, bool DecommissionOldPrimary = false);
