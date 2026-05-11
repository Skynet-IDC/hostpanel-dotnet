using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Api.Middleware;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/domains/{domainId}/git")]
[Authorize]
public class GitDeployController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServerAgent _agent;
    private readonly TenantAuthorizationService _auth;

    public GitDeployController(AppDbContext db, IServerAgent agent, TenantAuthorizationService auth)
    {
        _db = db;
        _agent = agent;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> Get(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var deployment = await _db.Set<GitDeployment>().FirstOrDefaultAsync(g => g.DomainId == domainId);
        if (deployment is null) return Ok(new { configured = false });

        return Ok(new
        {
            configured = true,
            deployment.RepoUrl, deployment.Branch, deployment.Provider,
            deployment.AutoDeploy, deployment.LastCommitHash, deployment.LastDeployedAt
        });
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Configure(int domainId, [FromBody] GitConfigRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var domain = await _db.Domains.Include(d => d.Subscription).FirstOrDefaultAsync(d => d.Id == domainId);
        if (domain is null) return NotFound();

        var existing = await _db.Set<GitDeployment>().FirstOrDefaultAsync(g => g.DomainId == domainId);
        var webhookSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLower();

        if (existing is not null)
        {
            existing.RepoUrl = request.RepoUrl;
            existing.Branch = request.Branch;
            existing.Provider = request.Provider;
            existing.AutoDeploy = request.AutoDeploy;
            existing.WebhookSecret = webhookSecret;
            existing.DeployPath = domain.DocumentRoot;
        }
        else
        {
            _db.Set<GitDeployment>().Add(new GitDeployment
            {
                DomainId = domainId,
                RepoUrl = request.RepoUrl,
                Branch = request.Branch,
                Provider = request.Provider,
                AutoDeploy = request.AutoDeploy,
                WebhookSecret = webhookSecret,
                DeployPath = domain.DocumentRoot
            });
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            webhookUrl = $"/api/v1/domains/{domainId}/git/webhook",
            webhookSecret,
            message = $"Configure this webhook URL in your {request.Provider} repo settings"
        });
    }

    [HttpPost("deploy")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> ManualDeploy(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var deployment = await _db.Set<GitDeployment>()
            .Include(g => g.Domain).ThenInclude(d => d.Subscription)
            .FirstOrDefaultAsync(g => g.DomainId == domainId);
        if (deployment is null) return BadRequest(new { message = "Git not configured for this domain" });

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "git.deploy",
            Params = new Dictionary<string, object?>
            {
                ["repoUrl"] = deployment.RepoUrl,
                ["branch"] = deployment.Branch,
                ["deployPath"] = deployment.DeployPath,
                ["username"] = deployment.Domain.Subscription.SystemUsername
            }
        });

        if (result.IsSuccess)
        {
            deployment.LastDeployedAt = DateTime.UtcNow;
            deployment.LastCommitHash = result.Result?.Data?.TryGetValue("commitHash", out var h) == true ? h?.ToString() : null;
            deployment.LastDeployLog = result.Result?.Data?.TryGetValue("output", out var o) == true ? o?.ToString() : null;
            await _db.SaveChangesAsync();
        }

        return Ok(new { success = result.IsSuccess, data = result.Result?.Data, error = result.Error?.Message });
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(int domainId)
    {
        var deployment = await _db.Set<GitDeployment>()
            .Include(g => g.Domain).ThenInclude(d => d.Subscription)
            .FirstOrDefaultAsync(g => g.DomainId == domainId && g.AutoDeploy);
        if (deployment is null) return NotFound();

        // Webhook secret is mandatory - reject if not configured
        if (string.IsNullOrEmpty(deployment.WebhookSecret))
            return StatusCode(500, new { message = "Webhook secret not configured for this deployment" });

        // Verify webhook signature
        var body = await new StreamReader(Request.Body).ReadToEndAsync();

        // GitHub: X-Hub-Signature-256 (HMAC-SHA256 of body)
        // GitLab: X-Gitlab-Token (plain secret comparison)
        var ghSignature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        var glToken = Request.Headers["X-Gitlab-Token"].FirstOrDefault();

        if (!string.IsNullOrEmpty(ghSignature))
        {
            // GitHub / Bitbucket HMAC verification
            using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(deployment.WebhookSecret));
            var expected = "sha256=" + Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(body))).ToLower();
            if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expected),
                System.Text.Encoding.UTF8.GetBytes(ghSignature)))
                return Unauthorized();
        }
        else if (!string.IsNullOrEmpty(glToken))
        {
            // GitLab sends secret as plain header value
            if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(deployment.WebhookSecret),
                System.Text.Encoding.UTF8.GetBytes(glToken)))
                return Unauthorized();
        }
        else
        {
            return Unauthorized(new { message = "Missing webhook signature header" });
        }

        // Deploy in background
        _ = Task.Run(async () =>
        {
            await _agent.ExecuteAsync(new AgentRequest
            {
                Method = "git.deploy",
                Params = new Dictionary<string, object?>
                {
                    ["repoUrl"] = deployment.RepoUrl,
                    ["branch"] = deployment.Branch,
                    ["deployPath"] = deployment.DeployPath,
                    ["username"] = deployment.Domain.Subscription.SystemUsername
                }
            });
            deployment.LastDeployedAt = DateTime.UtcNow;
        });

        return Ok(new { message = "Deploy triggered" });
    }
}

public record GitConfigRequest(string RepoUrl, string Branch = "main", string Provider = "github", bool AutoDeploy = true);
