using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Api.Middleware;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/domains/{domainId}/settings")]
[Authorize]
public class DomainSettingsController : ControllerBase
{
    private readonly IServerAgent _agent;
    private readonly TenantAuthorizationService _auth;

    public DomainSettingsController(IServerAgent agent, TenantAuthorizationService auth)
    {
        _agent = agent;
        _auth = auth;
    }

    /// <summary>Enable/disable hotlink protection.</summary>
    [HttpPut("hotlink-protection")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> SetHotlinkProtection(int domainId, [FromBody] HotlinkProtectionRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "domain.setHotlinkProtection",
            Params = new Dictionary<string, object?>
            {
                ["domainId"] = domainId,
                ["enabled"] = request.Enabled,
                ["allowedDomains"] = request.AllowedDomains,
                ["fileExtensions"] = request.FileExtensions ?? "jpg,jpeg,png,gif,bmp,svg,webp"
            }
        });

        return Ok(new { success = result.IsSuccess, hotlinkProtection = request.Enabled });
    }

    /// <summary>IP deny manager - block IPs at the domain level via .htaccess or Nginx.</summary>
    [HttpPut("ip-deny")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> SetIpDeny(int domainId, [FromBody] IpDenyRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "domain.setIpDeny",
            Params = new Dictionary<string, object?>
            {
                ["domainId"] = domainId,
                ["deniedIps"] = request.DeniedIps
            }
        });

        return Ok(new { success = result.IsSuccess });
    }

    /// <summary>Directory password protection.</summary>
    [HttpPut("password-protect")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> PasswordProtectDirectory(int domainId, [FromBody] PasswordProtectRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "domain.passwordProtect",
            Params = new Dictionary<string, object?>
            {
                ["domainId"] = domainId,
                ["directory"] = request.Directory,
                ["username"] = request.Username,
                ["password"] = request.Password,
                ["enabled"] = request.Enabled
            }
        });

        return Ok(new { success = result.IsSuccess });
    }
}

public record HotlinkProtectionRequest(bool Enabled, string? AllowedDomains = null, string? FileExtensions = null);
public record IpDenyRequest(string DeniedIps);
public record PasswordProtectRequest(string Directory, string Username, string Password, bool Enabled = true);
public record CatchAllRequest(string? ForwardTo);
