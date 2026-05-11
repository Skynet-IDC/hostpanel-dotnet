using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Api.Middleware;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/domains/{domainId}/logs")]
[Authorize]
public class LogsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TenantAuthorizationService _auth;
    private readonly IServerAgent _agent;

    public LogsController(AppDbContext db, TenantAuthorizationService auth, IServerAgent agent)
    {
        _db = db;
        _auth = auth;
        _agent = agent;
    }

    [HttpGet("access")]
    public async Task<IActionResult> GetAccessLog(int domainId, [FromQuery] int lines = 100)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var domain = await _db.Domains.FindAsync(domainId);
        if (domain is null) return NotFound();

        // Read logs via Agent (respects privilege separation)
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "system.readLog",
            Params = new Dictionary<string, object?>
            {
                ["path"] = $"/var/log/nginx/{domain.Name}.access.log",
                ["lines"] = Math.Min(lines, 1000)
            }
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }

    [HttpGet("error")]
    public async Task<IActionResult> GetErrorLog(int domainId, [FromQuery] int lines = 100)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var domain = await _db.Domains.FindAsync(domainId);
        if (domain is null) return NotFound();

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "system.readLog",
            Params = new Dictionary<string, object?>
            {
                ["path"] = $"/var/log/nginx/{domain.Name}.error.log",
                ["lines"] = Math.Min(lines, 1000)
            }
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }

    [HttpGet("mail")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> GetMailLog([FromQuery] int lines = 100)
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "system.readLog",
            Params = new Dictionary<string, object?>
            {
                ["path"] = "/var/log/mail.log",
                ["lines"] = Math.Min(lines, 1000)
            }
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }
}
