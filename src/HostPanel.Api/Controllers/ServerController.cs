using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ServerController : ControllerBase
{
    private readonly AppDbContext _db;

    public ServerController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ServerStatsDto>> GetStats()
    {
        var totalDomains = await _db.Domains.CountAsync();
        var totalSubscriptions = await _db.Subscriptions.CountAsync();
        var activeSubscriptions = await _db.Subscriptions
            .CountAsync(s => s.Status == SubscriptionStatus.Active);

        // System stats will come from the Agent in production.
        // For now, return placeholder values for development.
        return Ok(new ServerStatsDto(
            CpuUsagePercent: 0,
            TotalMemoryMb: 0,
            UsedMemoryMb: 0,
            TotalDiskMb: 0,
            UsedDiskMb: 0,
            UptimeHours: 0,
            Hostname: Environment.MachineName,
            OsVersion: Environment.OSVersion.ToString(),
            TotalDomains: totalDomains,
            TotalSubscriptions: totalSubscriptions,
            ActiveSubscriptions: activeSubscriptions
        ));
    }
}
