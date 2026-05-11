using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Api.Middleware;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public class SecurityScanController : ControllerBase
{
    private readonly IServerAgent _agent;
    private readonly TenantAuthorizationService _auth;
    private readonly AppDbContext _db;

    public SecurityScanController(IServerAgent agent, TenantAuthorizationService auth, AppDbContext db)
    {
        _agent = agent;
        _auth = auth;
        _db = db;
    }

    /// <summary>ClamAV scan on a domain's document root.</summary>
    [HttpPost("domains/{domainId}/scan")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> ScanDomain(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var domain = await _db.Domains.FindAsync(domainId);
        if (domain is null) return NotFound();

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "security.clamScan",
            Params = new Dictionary<string, object?> { ["docRoot"] = domain.DocumentRoot }
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }

    /// <summary>Fail2Ban status and management.</summary>
    [HttpGet("security/fail2ban")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> Fail2BanStatus()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "security.fail2banStatus",
            Params = []
        });
        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }

    [HttpPost("security/fail2ban/unban")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> Fail2BanUnban([FromBody] UnbanRequest request)
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "security.fail2banUnban",
            Params = new Dictionary<string, object?>
            {
                ["jail"] = request.Jail,
                ["ip"] = request.Ip
            }
        });
        return Ok(new { success = result.IsSuccess });
    }

    /// <summary>Full server malware scan.</summary>
    [HttpPost("security/scan-server")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> ScanServer()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "security.clamScanAll",
            Params = []
        });
        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }
}

public record UnbanRequest(string Jail, string Ip);
