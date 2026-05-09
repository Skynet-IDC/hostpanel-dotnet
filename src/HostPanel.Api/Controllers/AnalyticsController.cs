using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Api.Middleware;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/domains/{domainId}/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly IServerAgent _agent;
    private readonly TenantAuthorizationService _auth;
    private readonly AppDbContext _db;

    public AnalyticsController(IServerAgent agent, TenantAuthorizationService auth, AppDbContext db)
    {
        _agent = agent;
        _auth = auth;
        _db = db;
    }

    /// <summary>Get visitor stats from Nginx access logs (parsed via agent).</summary>
    [HttpGet("visitors")]
    public async Task<IActionResult> GetVisitorStats(int domainId, [FromQuery] string period = "today")
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var domain = await _db.Domains.FindAsync(domainId);
        if (domain is null) return NotFound();

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "analytics.visitors",
            Params = new Dictionary<string, object?>
            {
                ["domainName"] = domain.Name,
                ["period"] = period
            }
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }

    /// <summary>Get bandwidth usage over time.</summary>
    [HttpGet("bandwidth")]
    public async Task<IActionResult> GetBandwidth(int domainId, [FromQuery] string period = "month")
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "analytics.bandwidth",
            Params = new Dictionary<string, object?>
            {
                ["domainId"] = domainId,
                ["period"] = period
            }
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }

    /// <summary>Get resource usage history (CPU, RAM, disk over time).</summary>
    [HttpGet("/api/v1/server/analytics")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetServerAnalytics([FromQuery] string period = "day")
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "analytics.serverResources",
            Params = new Dictionary<string, object?> { ["period"] = period }
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }
}
