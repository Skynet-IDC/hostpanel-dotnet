using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ServerController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServerAgent _agent;
    private readonly ILogger<ServerController> _logger;

    public ServerController(AppDbContext db, IServerAgent agent, ILogger<ServerController> logger)
    {
        _db = db;
        _agent = agent;
        _logger = logger;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ServerStatsDto>> GetStats()
    {
        var totalDomains = await _db.Domains.CountAsync();
        var totalSubscriptions = await _db.Subscriptions.CountAsync();
        var activeSubscriptions = await _db.Subscriptions
            .CountAsync(s => s.Status == SubscriptionStatus.Active);

        long totalMemMb = 0, usedMemMb = 0, totalDiskMb = 0, usedDiskMb = 0;
        double uptimeHours = 0;
        var hostname = Environment.MachineName;
        var osVersion = Environment.OSVersion.ToString();

        try
        {
            var agentResult = await _agent.ExecuteAsync(new AgentRequest
            {
                Method = AgentMethods.SystemGetStats,
                Params = []
            });

            if (agentResult.IsSuccess && agentResult.Result?.Data is { } data1)
            {
                hostname = GetString(data1, "hostname") ?? hostname;
                osVersion = GetString(data1, "osVersion") ?? osVersion;
                totalMemMb = GetInt64(data1, "totalMemoryMb") ?? totalMemMb;
                var ups1 = GetInt64(data1, "uptimeSeconds");
                if (ups1 is not null) uptimeHours = ups1.Value / 3600.0;
            }
            else if (agentResult.Error is { } e1)
            {
                _logger.LogWarning("Agent system.getStats returned error {Code}: {Message}", e1.Code, e1.Message);
            }

            var resourceResult = await _agent.ExecuteAsync(new AgentRequest
            {
                Method = AgentMethods.AnalyticsServerResources,
                Params = []
            });

            if (resourceResult.IsSuccess && resourceResult.Result?.Data is { } data2)
            {
                var tmb = GetInt64(data2, "totalMemoryBytes");
                var umb = GetInt64(data2, "usedMemoryBytes");
                var tdb = GetInt64(data2, "totalDiskBytes");
                var udb = GetInt64(data2, "usedDiskBytes");
                var ups = GetInt64(data2, "uptimeSeconds");
                if (tmb is not null) totalMemMb = tmb.Value / (1024 * 1024);
                if (umb is not null) usedMemMb = umb.Value / (1024 * 1024);
                if (tdb is not null) totalDiskMb = tdb.Value / (1024 * 1024);
                if (udb is not null) usedDiskMb = udb.Value / (1024 * 1024);
                if (ups is not null) uptimeHours = ups.Value / 3600.0;
            }
            else if (resourceResult.Error is { } e2)
            {
                _logger.LogWarning("Agent analytics.serverResources returned error {Code}: {Message}", e2.Code, e2.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch server stats from agent");
        }

        return Ok(new ServerStatsDto(
            CpuUsagePercent: 0,
            TotalMemoryMb: totalMemMb,
            UsedMemoryMb: usedMemMb,
            TotalDiskMb: totalDiskMb,
            UsedDiskMb: usedDiskMb,
            UptimeHours: Math.Round(uptimeHours, 1),
            Hostname: hostname,
            OsVersion: osVersion,
            TotalDomains: totalDomains,
            TotalSubscriptions: totalSubscriptions,
            ActiveSubscriptions: activeSubscriptions
        ));
    }

    private static long? GetInt64(IDictionary<string, object?> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v is null) return null;
        return v switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var n) => n,
            JsonElement je when je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var n) => n,
            long n => n,
            int n => n,
            double d2 => (long)d2,
            string s when long.TryParse(s, out var n) => n,
            _ => null
        };
    }

    private static string? GetString(IDictionary<string, object?> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v is null) return null;
        return v switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            JsonElement je => je.ToString(),
            string s => s,
            _ => v.ToString()
        };
    }
}
