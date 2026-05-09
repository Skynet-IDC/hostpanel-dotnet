using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Modules.Database;
using HostPanel.Api.Middleware;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class DatabasesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DbProvisioningFactory _dbFactory;
    private readonly TenantAuthorizationService _auth;

    public DatabasesController(AppDbContext db, DbProvisioningFactory dbFactory, TenantAuthorizationService auth)
    {
        _db = db;
        _dbFactory = dbFactory;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? subscriptionId)
    {
        if (subscriptionId.HasValue)
        {
            if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId.Value)) return Forbid();
        }

        var query = _db.HostedDatabases.AsQueryable();
        if (subscriptionId.HasValue)
            query = query.Where(d => d.SubscriptionId == subscriptionId.Value);

        var databases = await query
            .Select(d => new { d.Id, d.Name, d.Type, d.DbUsername, d.SubscriptionId, d.CreatedAt })
            .ToListAsync();

        // Filter results by tenant access
        var accessible = new List<object>();
        foreach (var db in databases)
        {
            if (await _auth.CanAccessSubscriptionAsync(User, db.SubscriptionId))
                accessible.Add(db);
        }
        return Ok(accessible);
    }

    [HttpGet("types")]
    public IActionResult GetAvailableTypes()
    {
        var types = _dbFactory.GetAvailableTypes()
            .Select(t => new { type = t.ToString(), value = (int)t })
            .ToList();
        return Ok(types);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> Create([FromBody] CreateDatabaseRequest request)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, request.SubscriptionId)) return Forbid();

        var sub = await _db.Subscriptions
            .Include(s => s.ServicePlan)
            .Include(s => s.Databases)
            .FirstOrDefaultAsync(s => s.Id == request.SubscriptionId);

        if (sub is null) return BadRequest(new { message = "Invalid subscription" });

        if (sub.Databases.Count >= sub.ServicePlan.MaxDatabases)
            return BadRequest(new { message = "Database limit reached for this plan" });

        // Generate safe names
        var dbName = $"hp_{sub.SystemUsername}_{request.Name}";
        var dbUser = $"hp_{sub.SystemUsername}_{request.Name}_u";
        var dbPassword = GeneratePassword();

        try
        {
            var provider = _dbFactory.GetProvider(request.Type);
            var result = await provider.CreateDatabaseAsync(dbName, dbUser, dbPassword);

            var hostedDb = new HostedDatabase
            {
                SubscriptionId = request.SubscriptionId,
                Name = dbName,
                Type = request.Type,
                DbUsername = dbUser,
                DbPasswordHash = HashForStorage(dbPassword)
            };

            _db.HostedDatabases.Add(hostedDb);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(List), new { subscriptionId = request.SubscriptionId }, new
            {
                hostedDb.Id,
                hostedDb.Name,
                hostedDb.Type,
                hostedDb.DbUsername,
                password = dbPassword, // Only returned once at creation
                connectionString = result.ConnectionString
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Failed to create database: {ex.Message}" });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await _auth.CanAccessDatabaseAsync(User, id)) return Forbid();

        var hostedDb = await _db.HostedDatabases.FindAsync(id);
        if (hostedDb is null) return NotFound();

        try
        {
            var provider = _dbFactory.GetProvider(hostedDb.Type);
            await provider.DropDatabaseAsync(hostedDb.Name);
            await provider.DropUserAsync(hostedDb.DbUsername);
        }
        catch (Exception ex)
        {
            // Log but continue with DB record removal
            HttpContext.RequestServices.GetRequiredService<ILogger<DatabasesController>>()
                .LogWarning(ex, "Failed to drop database {Name} from engine", hostedDb.Name);
        }

        _db.HostedDatabases.Remove(hostedDb);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetEngineStatus()
    {
        var results = new List<object>();
        foreach (var type in _dbFactory.GetAvailableTypes())
        {
            var provider = _dbFactory.GetProvider(type);
            var ok = await provider.TestConnectionAsync();
            results.Add(new { engine = type.ToString(), available = ok });
        }
        return Ok(results);
    }

    private static string GeneratePassword()
    {
        var bytes = new byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "x").Replace("/", "y")[..24];
    }

    private static string HashForStorage(string password)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password)));
    }
}

public record CreateDatabaseRequest(int SubscriptionId, string Name, DatabaseType Type);
