using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Api.Middleware;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/domains/{domainId}/apps")]
[Authorize]
public class AppInstallerController : ControllerBase
{
    private readonly IServerAgent _agent;
    private readonly TenantAuthorizationService _auth;

    public AppInstallerController(IServerAgent agent, TenantAuthorizationService auth)
    {
        _agent = agent;
        _auth = auth;
    }

    /// <summary>List available one-click apps.</summary>
    [HttpGet("available")]
    public IActionResult ListAvailable()
    {
        var apps = new[]
        {
            new { id = "wordpress", name = "WordPress", category = "CMS", description = "The world's most popular CMS" },
            new { id = "joomla", name = "Joomla", category = "CMS", description = "Flexible CMS platform" },
            new { id = "drupal", name = "Drupal", category = "CMS", description = "Enterprise-grade CMS" },
            new { id = "prestashop", name = "PrestaShop", category = "eCommerce", description = "Free open-source eCommerce" },
            new { id = "opencart", name = "OpenCart", category = "eCommerce", description = "Open-source shopping cart" },
            new { id = "nextcloud", name = "Nextcloud", category = "Cloud", description = "Self-hosted file sync and share" },
            new { id = "moodle", name = "Moodle", category = "Education", description = "Learning management system" },
            new { id = "phpbb", name = "phpBB", category = "Forum", description = "Bulletin board software" },
            new { id = "mediawiki", name = "MediaWiki", category = "Wiki", description = "Wiki software (powers Wikipedia)" },
            new { id = "ghost", name = "Ghost", category = "Blog", description = "Professional publishing platform" },
            new { id = "matomo", name = "Matomo", category = "Analytics", description = "Privacy-focused web analytics" },
            new { id = "roundcube", name = "Roundcube", category = "Email", description = "Webmail client" },
            new { id = "gitea", name = "Gitea", category = "Development", description = "Lightweight Git service" },
            new { id = "n8n", name = "n8n", category = "Automation", description = "Workflow automation tool" },
        };
        return Ok(apps);
    }

    /// <summary>Install a one-click app.</summary>
    [HttpPost("install")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Install(int domainId, [FromBody] InstallAppRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "apps.install",
            Params = new Dictionary<string, object?>
            {
                ["domainId"] = domainId,
                ["appId"] = request.AppId,
                ["adminUser"] = request.AdminUser,
                ["adminPassword"] = request.AdminPassword,
                ["adminEmail"] = request.AdminEmail
            }
        });

        return Ok(new { success = result.IsSuccess, data = result.Result?.Data, error = result.Error?.Message });
    }
}

public record InstallAppRequest(string AppId, string AdminUser = "admin", string AdminPassword = "", string AdminEmail = "");
