using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Api.Middleware;
using HostPanel.Core.Interfaces;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public class BackupsController : ControllerBase
{
    private readonly IBackupService _backupService;
    private readonly TenantAuthorizationService _auth;
    private readonly AppDbContext _db;

    public BackupsController(IBackupService backupService, TenantAuthorizationService auth, AppDbContext db)
    {
        _backupService = backupService;
        _auth = auth;
        _db = db;
    }

    [HttpGet("subscriptions/{subscriptionId}/backups")]
    public async Task<IActionResult> List(int subscriptionId)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();

        var backups = await _backupService.ListBackupsAsync(subscriptionId);
        return Ok(backups.Select(b => new
        {
            b.Id, b.Type, b.SizeBytes, b.IsCompleted, b.CreatedAt
        }));
    }

    [HttpPost("subscriptions/{subscriptionId}/backups")]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> Create(int subscriptionId)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();

        var backup = await _backupService.CreateBackupAsync(subscriptionId);
        return Ok(new { backup.Id, backup.Type, backup.SizeBytes, backup.IsCompleted, backup.CreatedAt });
    }

    [HttpPost("backups/{backupId}/restore")]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> Restore(int backupId)
    {
        var backup = await _db.Backups.FindAsync(backupId);
        if (backup is null) return NotFound();

        if (!await _auth.CanAccessSubscriptionAsync(User, backup.SubscriptionId)) return Forbid();

        try
        {
            await _backupService.RestoreBackupAsync(backupId);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
