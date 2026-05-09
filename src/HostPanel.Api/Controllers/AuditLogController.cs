using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/audit")]
[Authorize(Roles = "Admin")]
public class AuditLogController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditLogController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? userId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? entityType = null)
    {
        var query = _db.AuditLogs.AsQueryable();

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(l => l.UserId == userId);
        if (!string.IsNullOrEmpty(action))
            query = query.Where(l => l.Action.Contains(action));
        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(l => l.EntityType == entityType);

        var result = await query
            .OrderByDescending(l => l.CreatedAt)
            .ToPaginatedAsync(page, pageSize);

        return Ok(result);
    }
}
