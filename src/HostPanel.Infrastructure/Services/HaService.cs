using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Infrastructure.Services;

public class HaService
{
    private readonly AppDbContext _db;
    private readonly ServerAgentFactory _agentFactory;
    private readonly IServerAgent _localAgent;
    private readonly WebhookDispatcher _webhooks;
    private readonly FailoverStrategyFactory _strategyFactory;
    private readonly ILogger<HaService> _logger;

    public HaService(AppDbContext db, ServerAgentFactory agentFactory, IServerAgent localAgent,
        WebhookDispatcher webhooks, FailoverStrategyFactory strategyFactory, ILogger<HaService> logger)
    {
        _db = db;
        _agentFactory = agentFactory;
        _localAgent = localAgent;
        _webhooks = webhooks;
        _strategyFactory = strategyFactory;
        _logger = logger;
    }

    /// <summary>
    /// Full failover: promote standby to primary, stop sync from old primary.
    /// </summary>
    public async Task<string> FailoverAsync(int standbyServerId, CancellationToken ct)
    {
        var standby = await _db.Set<Server>().FindAsync([standbyServerId], ct)
            ?? throw new InvalidOperationException("Standby server not found");
        var syncState = await _db.Set<HaSyncState>().FirstOrDefaultAsync(s => s.ServerId == standbyServerId, ct)
            ?? throw new InvalidOperationException("No HA sync state for this server");

        _logger.LogWarning("FAILOVER: Promoting {Host} to primary", standby.Hostname);

        var agent = _agentFactory.GetAgent(standby);

        // Stop pulling from old primary
        await agent.ExecuteAsync(new AgentRequest
        {
            Method = "ha.stopSync",
            Params = []
        }, ct);

        // Activate all services on standby
        await agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.HaPromoteToPrimary,
            Params = []
        }, ct);

        // Execute traffic routing strategy (DNS swap, floating IP, Cloudflare, or per-domain)
        var oldPrimary = await _db.Set<Server>().FirstOrDefaultAsync(s => s.IsLocal, ct);
        if (oldPrimary is not null)
        {
            var strategy = _strategyFactory.Get(syncState.FailoverStrategy);
            await strategy.ExecuteFailoverAsync(standby, oldPrimary, syncState, ct);
            _logger.LogInformation("Failover traffic routing via {Strategy}", syncState.FailoverStrategy);
        }

        // Update state
        syncState.Role = "primary";
        syncState.FailoverTriggeredAt = DateTime.UtcNow;
        syncState.ConsecutiveFailures = 0;

        // Mark old primary's sync state as "down" (reuse oldPrimary from above)
        if (oldPrimary is not null)
        {
            var oldState = await _db.Set<HaSyncState>().FirstOrDefaultAsync(s => s.ServerId == oldPrimary.Id, ct);
            if (oldState is not null)
                oldState.Role = "down";
        }

        await _db.SaveChangesAsync(ct);

        _ = _webhooks.FireAsync("server.failover", new
        {
            promoted = standby.Hostname, standbyId = standbyServerId,
            timestamp = DateTime.UtcNow
        });

        _logger.LogWarning("FAILOVER COMPLETE: {Host} is now primary", standby.Hostname);
        return $"Failover complete. {standby.Hostname} is now primary.";
    }

    /// <summary>
    /// Failback: original primary has recovered. Sync changes from current primary (B) back to A, then promote A.
    /// </summary>
    public async Task<string> FailbackAsync(int recoveredServerId, CancellationToken ct)
    {
        var recovered = await _db.Set<Server>().FindAsync([recoveredServerId], ct)
            ?? throw new InvalidOperationException("Server not found");
        var recoveredState = await _db.Set<HaSyncState>().FirstOrDefaultAsync(s => s.ServerId == recoveredServerId, ct)
            ?? throw new InvalidOperationException("No HA sync state");

        // Find current primary
        var currentPrimary = await _db.Set<HaSyncState>()
            .Include(s => s.Server)
            .FirstOrDefaultAsync(s => s.Role == "primary" && s.ServerId != recoveredServerId, ct)
            ?? throw new InvalidOperationException("No current primary found");

        _logger.LogInformation("FAILBACK: Syncing changes from {Primary} back to {Recovered}",
            currentPrimary.Server.Hostname, recovered.Hostname);

        recoveredState.Role = "recovering";
        await _db.SaveChangesAsync(ct);

        var primaryAgent = _agentFactory.GetAgent(currentPrimary.Server);
        var recoveredAgent = _agentFactory.GetAgent(recovered);

        // Step 1: Sync files from current primary (B) to recovered server (A)
        var syncResult = await recoveredAgent.ExecuteAsync(new AgentRequest
        {
            Method = "ha.syncFrom",
            Params = new Dictionary<string, object?>
            {
                ["sourceIp"] = currentPrimary.Server.IpAddress,
                ["syncFiles"] = recoveredState.SyncFiles,
                ["syncExcludes"] = recoveredState.SyncExcludes
            }
        }, ct);

        if (!syncResult.IsSuccess)
        {
            recoveredState.Role = "down";
            recoveredState.LastSyncLog = $"File sync failed: {syncResult.Error?.Message}";
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException($"File sync failed: {syncResult.Error?.Message}");
        }

        // Step 2: Sync databases from current primary to recovered
        if (recoveredState.SyncDatabases)
        {
            var dbSyncResult = await recoveredAgent.ExecuteAsync(new AgentRequest
            {
                Method = "ha.syncDatabases",
                Params = new Dictionary<string, object?>
                {
                    ["sourceIp"] = currentPrimary.Server.IpAddress
                }
            }, ct);

            if (!dbSyncResult.IsSuccess)
                _logger.LogWarning("Database sync partially failed: {Error}", dbSyncResult.Error?.Message);
        }

        // Step 3: Sync email if configured
        if (recoveredState.SyncEmail)
        {
            await recoveredAgent.ExecuteAsync(new AgentRequest
            {
                Method = "ha.syncEmail",
                Params = new Dictionary<string, object?>
                {
                    ["sourceIp"] = currentPrimary.Server.IpAddress
                }
            }, ct);
        }

        // Step 4: Promote recovered server back to primary
        await recoveredAgent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.HaPromoteToPrimary,
            Params = []
        }, ct);

        // Step 5: Demote current primary back to standby
        await primaryAgent.ExecuteAsync(new AgentRequest
        {
            Method = "ha.demoteToStandby",
            Params = new Dictionary<string, object?>
            {
                ["primaryIp"] = recovered.IpAddress,
                ["syncInterval"] = recoveredState.HealthCheckIntervalSeconds
            }
        }, ct);

        // Step 6: Route traffic back to recovered server
        var strategy = _strategyFactory.Get(recoveredState.FailoverStrategy);
        await strategy.ExecuteFailbackAsync(recovered, currentPrimary.Server, recoveredState, ct);
        _logger.LogInformation("Failback traffic routing via {Strategy}", recoveredState.FailoverStrategy);

        // Update states
        recoveredState.Role = "primary";
        recoveredState.FailbackCompletedAt = DateTime.UtcNow;
        recoveredState.LastSyncAt = DateTime.UtcNow;
        currentPrimary.Role = "standby";
        await _db.SaveChangesAsync(ct);

        _ = _webhooks.FireAsync("server.failback", new
        {
            recovered = recovered.Hostname, demoted = currentPrimary.Server.Hostname,
            timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("FAILBACK COMPLETE: {Host} is primary again, {Standby} is standby",
            recovered.Hostname, currentPrimary.Server.Hostname);

        return $"Failback complete. {recovered.Hostname} is primary again.";
    }

    /// <summary>
    /// Check if a server is reachable and healthy.
    /// </summary>
    public async Task<bool> CheckHealthAsync(Server server, CancellationToken ct)
    {
        var agent = _agentFactory.GetAgent(server);
        return await agent.IsAvailableAsync(ct);
    }
}

/// <summary>
/// Background service that monitors HA cluster health and triggers automatic failover/failback.
/// </summary>
public class HaHealthMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HaHealthMonitorService> _logger;

    public HaHealthMonitorService(IServiceScopeFactory scopeFactory, ILogger<HaHealthMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var haService = scope.ServiceProvider.GetRequiredService<HaService>();

                var syncStates = await db.Set<HaSyncState>()
                    .Include(s => s.Server)
                    .ToListAsync(stoppingToken);

                foreach (var state in syncStates)
                {
                    var healthy = await haService.CheckHealthAsync(state.Server, stoppingToken);

                    state.Server.IsHealthy = healthy;
                    state.Server.LastHealthCheck = DateTime.UtcNow;

                    if (healthy)
                    {
                        state.ConsecutiveFailures = 0;

                        // Auto-failback: if this was the original primary and it's recovered
                        if (state.Role == "down" && state.AutoFailback)
                        {
                            _logger.LogInformation("AUTO-FAILBACK: {Host} has recovered, initiating failback",
                                state.Server.Hostname);
                            try
                            {
                                await haService.FailbackAsync(state.ServerId, stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Auto-failback failed for {Host}", state.Server.Hostname);
                            }
                        }
                    }
                    else
                    {
                        state.ConsecutiveFailures++;
                        _logger.LogWarning("Health check failed for {Host} ({Count}/{Threshold})",
                            state.Server.Hostname, state.ConsecutiveFailures, state.FailoverAfterMissedChecks);

                        // Auto-failover: primary is down
                        if (state.Role == "primary" && state.ConsecutiveFailures >= state.FailoverAfterMissedChecks)
                        {
                            var standby = syncStates.FirstOrDefault(s => s.Role == "standby" && s.Server.IsHealthy);
                            if (standby is not null)
                            {
                                _logger.LogWarning("AUTO-FAILOVER: {Primary} down, promoting {Standby}",
                                    state.Server.Hostname, standby.Server.Hostname);
                                try
                                {
                                    await haService.FailoverAsync(standby.ServerId, stoppingToken);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Auto-failover failed");
                                }
                            }
                        }
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "HA health monitor error");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
