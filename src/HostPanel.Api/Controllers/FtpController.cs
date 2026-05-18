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
[Route("api/v1/subscriptions/{subscriptionId}/ftp")]
[Authorize]
public class FtpController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServerAgent _agent;
    private readonly TenantAuthorizationService _auth;

    public FtpController(AppDbContext db, IServerAgent agent, TenantAuthorizationService auth)
    {
        _db = db;
        _agent = agent;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> List(int subscriptionId)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();

        var accounts = await _db.Set<FtpAccount>()
            .Where(f => f.SubscriptionId == subscriptionId)
            .Select(f => new { f.Id, f.Username, f.HomeDirectory, f.IsActive, f.QuotaMb, f.CreatedAt })
            .ToListAsync();
        return Ok(accounts);
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Create(int subscriptionId, [FromBody] CreateFtpRequest request)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();

        var sub = await _db.Subscriptions.FindAsync(subscriptionId);
        if (sub is null) return NotFound();

        // If caller supplies an absolute path, use it directly; otherwise treat as relative to subscription base.
        var homeDir = string.IsNullOrEmpty(request.Directory) || !request.Directory.StartsWith('/')
            ? $"/var/www/{sub.SystemUsername}/{request.Directory ?? ""}"
            : request.Directory;
        var ftpUser = $"{sub.SystemUsername}_{request.Username}";

        // Create system FTP user via agent
        await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "ftp.createAccount",
            Params = new Dictionary<string, object?>
            {
                ["username"] = ftpUser,
                ["password"] = request.Password,
                ["homeDir"] = homeDir,
                ["quotaMb"] = request.QuotaMb
            }
        });

        var account = new FtpAccount
        {
            SubscriptionId = subscriptionId,
            Username = ftpUser,
            PasswordHash = "{MANAGED}",
            HomeDirectory = homeDir,
            QuotaMb = request.QuotaMb
        };

        _db.Set<FtpAccount>().Add(account);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new { subscriptionId }, new
        {
            account.Id, account.Username, account.HomeDirectory, account.QuotaMb
        });
    }

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Delete(int subscriptionId, int id)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();

        var account = await _db.Set<FtpAccount>().FindAsync(id);
        if (account is null || account.SubscriptionId != subscriptionId) return NotFound();

        await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "ftp.deleteAccount",
            Params = new Dictionary<string, object?> { ["username"] = account.Username }
        });

        _db.Set<FtpAccount>().Remove(account);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateFtpRequest(string Username, string Password, string Directory = "httpdocs", long QuotaMb = 500);
