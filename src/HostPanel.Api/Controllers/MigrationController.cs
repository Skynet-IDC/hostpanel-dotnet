using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Infrastructure.Services.Migration;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "Admin")]
public class MigrationController : ControllerBase
{
    private readonly PleskMigrationService _migration;

    public MigrationController(PleskMigrationService migration)
    {
        _migration = migration;
    }

    [HttpPost("plesk/api")]
    public async Task<IActionResult> MigrateFromPleskApi([FromBody] PleskApiMigrationRequest request)
    {
        var result = await _migration.ImportFromPleskApiAsync(
            request.PleskHost, request.ApiKey,
            request.TargetUserId, request.ServicePlanId,
            HttpContext.RequestAborted);

        return Ok(result);
    }

    [HttpPost("plesk/backup")]
    public async Task<IActionResult> MigrateFromPleskBackup([FromBody] PleskBackupMigrationRequest request)
    {
        var result = await _migration.ImportFromBackupFileAsync(
            request.BackupFilePath,
            request.TargetUserId, request.ServicePlanId,
            HttpContext.RequestAborted);

        return Ok(result);
    }
}

public record PleskApiMigrationRequest(string PleskHost, string ApiKey, string TargetUserId, int ServicePlanId);
public record PleskBackupMigrationRequest(string BackupFilePath, string TargetUserId, int ServicePlanId);
