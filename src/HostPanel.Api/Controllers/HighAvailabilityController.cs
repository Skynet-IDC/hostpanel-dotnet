using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly IServerAgent _localAgent;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HighAvailabilityController> _logger;

    public HighAvailabilityController(
        AppDbContext db,
        ServerAgentFactory agentFactory,
        IServerAgent localAgent,
        IHttpClientFactory httpFactory,
        ILogger<HighAvailabilityController> logger)
    {
        _db = db;
        _agentFactory = agentFactory;
        _localAgent = localAgent;
        _httpFactory = httpFactory;
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

        // Persist an HaSyncState row so downstream endpoints (/ha/test, /ha/sync-state,
        // /ha/replication-lag's lag persistence, /ha/loadbalancer/*) have a row to find.
        // Upsert: one row per standby Server.
        var syncState = await _db.Set<HaSyncState>()
            .FirstOrDefaultAsync(s => s.ServerId == standby.Id);
        if (syncState is null)
        {
            syncState = new HaSyncState { ServerId = standby.Id };
            _db.Set<HaSyncState>().Add(syncState);
        }
        syncState.Role = "standby";
        syncState.SyncDirection = "pull";
        syncState.SyncFiles = request.SyncDomains;
        syncState.SyncDatabases = request.SyncDatabases;
        syncState.SyncEmail = request.SyncEmail;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = result.IsSuccess,
            primary = primary.Hostname,
            standby = standby.Hostname,
            haSyncStateId = syncState.Id,
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
                lastHealthCheck = s.Server.LastHealthCheck,
                // Phase 19c
                s.LastTestedAt,
                s.LastTestResult,
                s.LastReplicationLagSeconds,
                s.LastReplicationCheckAt,
                s.CloudflareUseLoadBalancer,
                s.CloudflareLoadBalancerId,
                s.FailoverStrategy
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

    /// <summary>
    /// Phase 19c: current replication lag for SQL backup file, lsyncd files, and watchdog state.
    /// Reads timestamps off disk via the agent. Cheap — call from UI on a 10s timer.
    /// </summary>
    [HttpGet("replication-lag")]
    public async Task<IActionResult> GetReplicationLag()
    {
        var result = await _localAgent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.HaReplicationLag,
            Params = []
        });

        if (!result.IsSuccess || result.Result is null)
            return StatusCode(503, new { ok = false, error = result.Error?.Message ?? "agent unavailable" });

        var data = result.Result.Data ?? new Dictionary<string, object?>();

        // Best-effort: persist the worst-channel lag on each HaSyncState row so it survives restarts
        // and shows up on the dashboard even when the agent is briefly unreachable.
        int? worstLag = null;
        if (data.TryGetValue("sqlBackupAgeSeconds", out var sqlObj) && sqlObj is JsonElement sqlEl && sqlEl.TryGetInt32(out var sqlLag))
            worstLag = Math.Max(worstLag ?? 0, sqlLag);
        if (data.TryGetValue("filesBacklogSeconds", out var fbObj) && fbObj is JsonElement fbEl && fbEl.TryGetInt32(out var fbLag))
            worstLag = Math.Max(worstLag ?? 0, fbLag);

        if (worstLag is int lag)
        {
            var states = await _db.Set<HaSyncState>().ToListAsync();
            foreach (var s in states)
            {
                s.LastReplicationLagSeconds = lag;
                s.LastReplicationCheckAt = DateTime.UtcNow;
            }
            if (states.Count > 0) await _db.SaveChangesAsync();
        }

        return Ok(data);
    }

    /// <summary>
    /// Phase 19c: probe-only test of HA readiness. Does NOT flip DNS or touch traffic.
    /// Checks: standby agent healthy + standby nginx responding + replication lag within threshold.
    /// Records a "HA tested OK at {timestamp}" badge on the standby's HaSyncState row.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> TestFailover([FromBody] HaTestRequest? request = null)
    {
        var thresholdSeconds = request?.MaxAcceptableLagSeconds ?? 300; // 5 min default
        var standbyId = request?.StandbyServerId;

        var standbyState = standbyId.HasValue
            ? await _db.Set<HaSyncState>().Include(s => s.Server).FirstOrDefaultAsync(s => s.ServerId == standbyId.Value)
            : await _db.Set<HaSyncState>().Include(s => s.Server).FirstOrDefaultAsync(s => s.Role == "standby");

        if (standbyState is null)
            return BadRequest(new { ok = false, message = "No standby configured" });

        var checks = new List<object>();
        var allOk = true;

        // 1. Standby agent reachable
        var standbyAgent = _agentFactory.GetAgent(standbyState.Server);
        var agentOk = await standbyAgent.IsAvailableAsync();
        checks.Add(new { check = "standbyAgent", ok = agentOk });
        if (!agentOk) allOk = false;

        // 2. Standby /api/v1/health (anonymous endpoint nginx is fronting)
        bool nginxOk = false;
        string? nginxError = null;
        try
        {
            using var http = _httpFactory.CreateClient("plesk-migration"); // reuse the self-signed-accepting client
            http.Timeout = TimeSpan.FromSeconds(8);
            var url = $"https://{standbyState.Server.IpAddress}:8443/api/v1/health";
            using var resp = await http.GetAsync(url);
            nginxOk = resp.IsSuccessStatusCode || (int)resp.StatusCode == 503; // 503 from health = nginx up, agent fronted
        }
        catch (Exception ex) { nginxError = ex.Message; }
        checks.Add(new { check = "standbyNginx", ok = nginxOk, error = nginxError });
        if (!nginxOk) allOk = false;

        // 3. Replication lag within threshold (use most recent value)
        int? lag = standbyState.LastReplicationLagSeconds;
        if (lag is null || (DateTime.UtcNow - (standbyState.LastReplicationCheckAt ?? DateTime.MinValue)).TotalMinutes > 2)
        {
            // Stale — force a fresh read
            var lagResult = await _localAgent.ExecuteAsync(new AgentRequest { Method = AgentMethods.HaReplicationLag, Params = [] });
            if (lagResult.IsSuccess && lagResult.Result?.Data is { } lagData)
            {
                int worst = 0;
                if (lagData.TryGetValue("sqlBackupAgeSeconds", out var s) && s is JsonElement se && se.TryGetInt32(out var sv)) worst = Math.Max(worst, sv);
                if (lagData.TryGetValue("filesBacklogSeconds", out var f) && f is JsonElement fe && fe.TryGetInt32(out var fv)) worst = Math.Max(worst, fv);
                lag = worst;
                standbyState.LastReplicationLagSeconds = worst;
                standbyState.LastReplicationCheckAt = DateTime.UtcNow;
            }
        }
        var lagOk = lag.HasValue && lag.Value <= thresholdSeconds;
        checks.Add(new { check = "replicationLag", ok = lagOk, lagSeconds = lag, thresholdSeconds });
        if (!lagOk) allOk = false;

        // Record on state
        standbyState.LastTestedAt = DateTime.UtcNow;
        standbyState.LastTestResult = JsonSerializer.Serialize(new { ok = allOk, checks });
        await _db.SaveChangesAsync();

        _logger.LogInformation("HA test on {Host}: {Result}", standbyState.Server.Hostname, allOk ? "PASSED" : "FAILED");

        return Ok(new
        {
            ok = allOk,
            standby = standbyState.Server.Hostname,
            standbyIp = standbyState.Server.IpAddress,
            testedAt = standbyState.LastTestedAt,
            checks
        });
    }

    /// <summary>
    /// Phase 19c (Premium tier): enable Cloudflare Load Balancer for the configured zone.
    /// Creates an LB pool with both primary + standby as origins, then a load-balancer that
    /// points the apex (or specified hostname) at that pool. Uses the per-cluster CF token
    /// from the standby's HaSyncState.CloudflareApiToken/ZoneId fields.
    /// </summary>
    [HttpPost("loadbalancer/enable")]
    public async Task<IActionResult> EnableLoadBalancer([FromBody] EnableLoadBalancerRequest request)
    {
        var state = await _db.Set<HaSyncState>()
            .Include(s => s.Server)
            .FirstOrDefaultAsync(s => s.ServerId == request.StandbyServerId);
        if (state is null) return NotFound(new { message = "HA sync state not found for standby" });
        if (string.IsNullOrEmpty(state.CloudflareApiToken) || string.IsNullOrEmpty(state.CloudflareZoneId))
            return BadRequest(new { message = "Cloudflare API token + Zone ID must be configured on this standby first (see HA → Configure)" });

        var primary = await _db.Set<Server>().FirstOrDefaultAsync(s => s.IsLocal)
            ?? throw new InvalidOperationException("No local primary server registered");

        using var http = _httpFactory.CreateClient("cloudflare");
        http.DefaultRequestHeaders.Remove("Authorization");
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {state.CloudflareApiToken}");

        // Look up account id from zone (LB pools live under accounts, not zones)
        var zoneResp = await http.GetAsync($"https://api.cloudflare.com/client/v4/zones/{state.CloudflareZoneId}");
        zoneResp.EnsureSuccessStatusCode();
        var zoneNode = await zoneResp.Content.ReadFromJsonAsync<JsonNode>();
        var accountId = zoneNode?["result"]?["account"]?["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Could not resolve CF account id from zone");
        var zoneName = zoneNode?["result"]?["name"]?.GetValue<string>() ?? "";

        // 1. Create the pool with both origins (primary + standby)
        var poolBody = new JsonObject
        {
            ["name"] = $"hostpanel-{zoneName.Replace(".", "-")}",
            ["description"] = "HostPanel HA pool (auto-managed)",
            ["enabled"] = true,
            ["minimum_origins"] = 1,
            ["origins"] = new JsonArray(
                new JsonObject { ["name"] = "primary", ["address"] = primary.IpAddress, ["enabled"] = true, ["weight"] = 1.0 },
                new JsonObject { ["name"] = "standby", ["address"] = state.Server.IpAddress, ["enabled"] = true, ["weight"] = 1.0 }
            )
        };

        using var poolReq = new StringContent(poolBody.ToJsonString(), Encoding.UTF8, "application/json");
        var poolResp = await http.PostAsync($"https://api.cloudflare.com/client/v4/accounts/{accountId}/load_balancers/pools", poolReq);
        if (!poolResp.IsSuccessStatusCode)
        {
            var body = await poolResp.Content.ReadAsStringAsync();
            return StatusCode((int)poolResp.StatusCode, new { message = "CF pool create failed", detail = body });
        }
        var poolNode = await poolResp.Content.ReadFromJsonAsync<JsonNode>();
        var poolId = poolNode?["result"]?["id"]?.GetValue<string>()!;

        // 2. Create the load balancer at the zone level (apex by default)
        var lbHostname = string.IsNullOrEmpty(request.Hostname) ? zoneName : request.Hostname;
        var lbBody = new JsonObject
        {
            ["name"] = lbHostname,
            ["fallback_pool"] = poolId,
            ["default_pools"] = new JsonArray((JsonNode)poolId),
            ["description"] = "HostPanel HA load balancer (auto-managed)",
            ["ttl"] = 30,
            ["proxied"] = false,
            ["enabled"] = true,
            ["steering_policy"] = "off" // active-passive: primary preferred via origin order
        };
        using var lbReq = new StringContent(lbBody.ToJsonString(), Encoding.UTF8, "application/json");
        var lbResp = await http.PostAsync($"https://api.cloudflare.com/client/v4/zones/{state.CloudflareZoneId}/load_balancers", lbReq);
        if (!lbResp.IsSuccessStatusCode)
        {
            var body = await lbResp.Content.ReadAsStringAsync();
            return StatusCode((int)lbResp.StatusCode, new { message = "CF load_balancer create failed (pool was created — clean up manually)", detail = body, poolId });
        }
        var lbNode = await lbResp.Content.ReadFromJsonAsync<JsonNode>();
        var lbId = lbNode?["result"]?["id"]?.GetValue<string>()!;

        state.CloudflareUseLoadBalancer = true;
        state.CloudflareLoadBalancerId = $"{poolId}|{lbId}"; // stash both IDs for teardown
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, hostname = lbHostname, poolId, loadBalancerId = lbId, accountId });
    }

    /// <summary>
    /// Phase 19c: tear down the Cloudflare Load Balancer + pool created by Enable.
    /// </summary>
    [HttpPost("loadbalancer/disable")]
    public async Task<IActionResult> DisableLoadBalancer([FromBody] DisableLoadBalancerRequest request)
    {
        var state = await _db.Set<HaSyncState>().FirstOrDefaultAsync(s => s.ServerId == request.StandbyServerId);
        if (state is null) return NotFound(new { message = "HA sync state not found" });
        if (string.IsNullOrEmpty(state.CloudflareApiToken) || string.IsNullOrEmpty(state.CloudflareZoneId))
            return BadRequest(new { message = "Cloudflare config missing" });
        if (string.IsNullOrEmpty(state.CloudflareLoadBalancerId))
            return BadRequest(new { message = "No load balancer is currently enabled" });

        var ids = state.CloudflareLoadBalancerId.Split('|');
        var poolId = ids.Length > 0 ? ids[0] : null;
        var lbId = ids.Length > 1 ? ids[1] : null;

        using var http = _httpFactory.CreateClient("cloudflare");
        http.DefaultRequestHeaders.Remove("Authorization");
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {state.CloudflareApiToken}");

        var zoneResp = await http.GetAsync($"https://api.cloudflare.com/client/v4/zones/{state.CloudflareZoneId}");
        zoneResp.EnsureSuccessStatusCode();
        var accountId = (await zoneResp.Content.ReadFromJsonAsync<JsonNode>())?["result"]?["account"]?["id"]?.GetValue<string>();

        var errors = new List<string>();
        if (!string.IsNullOrEmpty(lbId))
        {
            var r = await http.DeleteAsync($"https://api.cloudflare.com/client/v4/zones/{state.CloudflareZoneId}/load_balancers/{lbId}");
            if (!r.IsSuccessStatusCode) errors.Add($"LB delete: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        }
        if (!string.IsNullOrEmpty(poolId) && !string.IsNullOrEmpty(accountId))
        {
            var r = await http.DeleteAsync($"https://api.cloudflare.com/client/v4/accounts/{accountId}/load_balancers/pools/{poolId}");
            if (!r.IsSuccessStatusCode) errors.Add($"Pool delete: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        }

        state.CloudflareUseLoadBalancer = false;
        state.CloudflareLoadBalancerId = null;
        await _db.SaveChangesAsync();

        return Ok(new { ok = errors.Count == 0, errors });
    }
}

public record FailoverConfigRequest(int StandbyServerId, int SyncIntervalMinutes = 15, bool SyncDomains = true, bool SyncDatabases = true, bool SyncEmail = true);
public record FailoverRequest(int PromoteServerId, List<string>? Domains = null);
public record FailbackRequest(int RecoveredServerId);
public record UpdateSyncStateRequest(bool? AutoFailback = null, bool? SyncFiles = null, bool? SyncDatabases = null, bool? SyncEmail = null, int? HealthCheckIntervalSeconds = null, int? FailoverAfterMissedChecks = null, string? SyncExcludes = null, HostPanel.Core.Enums.FailoverStrategy? FailoverStrategy = null, string? FloatingIp = null, string? CloudflareApiToken = null, string? CloudflareZoneId = null, bool? CloudflareUseLoadBalancer = null);
public record PermanentPromoteRequest(int ServerId, bool DecommissionOldPrimary = false);
public record HaTestRequest(int? StandbyServerId = null, int MaxAcceptableLagSeconds = 300);
public record EnableLoadBalancerRequest(int StandbyServerId, string? Hostname = null);
public record DisableLoadBalancerRequest(int StandbyServerId);
