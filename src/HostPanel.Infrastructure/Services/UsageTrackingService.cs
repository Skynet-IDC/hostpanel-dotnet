using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Infrastructure.Services;

public class UsageTrackingService : IBillingMeter
{
    private readonly AppDbContext _db;
    private readonly IServerAgent _agent;
    private readonly ILogger<UsageTrackingService> _logger;

    public UsageTrackingService(AppDbContext db, IServerAgent agent, ILogger<UsageTrackingService> logger)
    {
        _db = db;
        _agent = agent;
        _logger = logger;
    }

    public async Task<long> GetDiskUsageBytesAsync(int subscriptionId, CancellationToken ct = default)
    {
        var sub = await _db.Subscriptions.FindAsync([subscriptionId], ct);
        if (sub?.SystemUsername is null) return 0;

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.SystemGetStats,
            Params = new Dictionary<string, object?> { ["username"] = sub.SystemUsername }
        }, ct);

        if (result.IsSuccess && result.Result?.Data?.TryGetValue("diskUsageBytes", out var usage) == true)
        {
            if (usage is System.Text.Json.JsonElement je && je.TryGetInt64(out var n)) return n;
            if (usage is long l) return l;
            if (usage is int i) return i;
            if (usage is string s && long.TryParse(s, out var ls)) return ls;
            try { return Convert.ToInt64(usage); } catch { return 0; }
        }

        return 0;
    }

    public async Task<long> GetBandwidthUsageBytesAsync(int subscriptionId, DateTime periodStart, DateTime periodEnd, CancellationToken ct = default)
    {
        // Resolve the subscription's hostnames so the agent can filter nginx logs.
        var hostnames = await _db.Domains
            .Where(d => d.SubscriptionId == subscriptionId)
            .Select(d => d.Name)
            .ToListAsync(ct);
        if (hostnames.Count == 0) return 0;

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.SystemGetBandwidth,
            Params = new Dictionary<string, object?>
            {
                ["hostnames"] = hostnames,
                ["since"] = periodStart.ToString("o"),
            }
        }, ct);

        if (result.IsSuccess && result.Result?.Data?.TryGetValue("bytes", out var v) == true)
        {
            // Same JsonElement guard as #16/#42/#49.
            if (v is System.Text.Json.JsonElement je && je.TryGetInt64(out var n)) return n;
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is string s && long.TryParse(s, out var ls)) return ls;
            try { return Convert.ToInt64(v); } catch { return 0; }
        }
        return 0;
    }

    public async Task<decimal> CalculateOveragesAsync(int subscriptionId, CancellationToken ct = default)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.ServicePlan)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);

        if (sub is null) return 0;

        var diskUsage = await GetDiskUsageBytesAsync(subscriptionId, ct);
        var diskLimitBytes = sub.ServicePlan.DiskQuotaMb * 1024 * 1024;

        if (diskUsage <= diskLimitBytes) return 0;

        var overageMb = (diskUsage - diskLimitBytes) / (1024.0m * 1024.0m);
        // $0.10 per MB over quota
        return Math.Round(overageMb * 0.10m, 2);
    }
}

/// <summary>
/// Background service that tracks usage for all active subscriptions periodically.
/// </summary>
public class UsageTrackingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UsageTrackingBackgroundService> _logger;

    public UsageTrackingBackgroundService(IServiceScopeFactory scopeFactory, ILogger<UsageTrackingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tracker = scope.ServiceProvider.GetRequiredService<UsageTrackingService>();

                var activeSubs = await db.Subscriptions
                    .Where(s => s.Status == SubscriptionStatus.Active)
                    .Select(s => s.Id)
                    .ToListAsync(stoppingToken);

                _logger.LogInformation("Tracking usage for {Count} active subscriptions", activeSubs.Count);

                foreach (var subId in activeSubs)
                {
                    try
                    {
                        var diskUsage = await tracker.GetDiskUsageBytesAsync(subId, stoppingToken);
                        // Could store historical usage data here for reporting
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to track usage for subscription {SubId}", subId);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in usage tracking cycle");
            }

            // Run every 6 hours
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }
}
