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

    public ServerController(AppDbContext db, IServerAgent agent)
    {
        _db = db;
        _agent = agent;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ServerStatsDto>> GetStats()
    {
        var totalDomains = await _db.Domains.CountAsync();
        var totalSubscriptions = await _db.Subscriptions.CountAsync();
        var activeSubscriptions = await _db.Subscriptions
            .CountAsync(s => s.Status == SubscriptionStatus.Active);

        // Get real system stats from Agent
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

            if (agentResult.IsSuccess && agentResult.Result?.Data is not null)
            {
                var data = agentResult.Result.Data;
                if (data.TryGetValue("hostname", out var h)) hostname = h?.ToString() ?? hostname;
                if (data.TryGetValue("osVersion", out var os)) osVersion = os?.ToString() ?? osVersion;
                if (data.TryGetValue("totalMemoryMb", out var tm)) totalMemMb = Convert.ToInt64(tm);
                if (data.TryGetValue("uptimeSeconds", out var up)) uptimeHours = Convert.ToDouble(up) / 3600;
            }

            // Get detailed resource stats from analytics handler
            var resourceResult = await _agent.ExecuteAsync(new AgentRequest
            {
                Method = AgentMethods.AnalyticsServerResources,
                Params = []
            });

            if (resourceResult.IsSuccess && resourceResult.Result?.Data is not null)
            {
                var data = resourceResult.Result.Data;
                if (data.TryGetValue("totalMemoryBytes", out var tmb)) totalMemMb = Convert.ToInt64(tmb) / (1024 * 1024);
                if (data.TryGetValue("usedMemoryBytes", out var umb)) usedMemMb = Convert.ToInt64(umb) / (1024 * 1024);
                if (data.TryGetValue("totalDiskBytes", out var tdb)) totalDiskMb = Convert.ToInt64(tdb) / (1024 * 1024);
                if (data.TryGetValue("usedDiskBytes", out var udb)) usedDiskMb = Convert.ToInt64(udb) / (1024 * 1024);
                if (data.TryGetValue("uptimeSeconds", out var ups)) uptimeHours = Convert.ToDouble(ups) / 3600;
            }
        }
        catch { /* Agent unavailable — return zeros */ }

        return Ok(new ServerStatsDto(
            CpuUsagePercent: 0, // CPU requires sampling over time
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
}
