using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/ip")]
[Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
public class IpManagementController : ControllerBase
{
    private readonly AppDbContext _db;

    public IpManagementController(AppDbContext db) => _db = db;

    // --- IP Pool ---
    [HttpGet("addresses")]
    public async Task<IActionResult> ListAddresses()
    {
        var ips = await _db.Set<ServerIpAddress>()
            .Select(i => new { i.Id, i.Address, i.IsIpv6, i.IsShared, i.SubscriptionId, i.Note })
            .ToListAsync();
        return Ok(ips);
    }

    [HttpPost("addresses")]
    public async Task<IActionResult> AddAddress([FromBody] AddIpRequest request)
    {
        var ip = new ServerIpAddress
        {
            Address = request.Address,
            IsIpv6 = request.Address.Contains(':'),
            IsShared = request.IsShared,
            SubscriptionId = request.SubscriptionId,
            Note = request.Note
        };
        _db.Set<ServerIpAddress>().Add(ip);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(ListAddresses), new { id = ip.Id }, ip);
    }

    [HttpPut("addresses/{id}/assign")]
    public async Task<IActionResult> AssignToSubscription(int id, [FromBody] AssignIpRequest request)
    {
        var ip = await _db.Set<ServerIpAddress>().FindAsync(id);
        if (ip is null) return NotFound();
        ip.SubscriptionId = request.SubscriptionId;
        ip.IsShared = false;
        await _db.SaveChangesAsync();
        return Ok(ip);
    }

    [HttpDelete("addresses/{id}")]
    public async Task<IActionResult> RemoveAddress(int id)
    {
        var ip = await _db.Set<ServerIpAddress>().FindAsync(id);
        if (ip is null) return NotFound();
        _db.Set<ServerIpAddress>().Remove(ip);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Blacklist ---
    [HttpGet("blacklist")]
    public async Task<IActionResult> ListBlacklist()
    {
        var entries = await _db.Set<IpBlacklistEntry>()
            .Select(b => new { b.Id, b.Address, b.Reason, b.ExpiresAt, b.CreatedAt })
            .ToListAsync();
        return Ok(entries);
    }

    [HttpPost("blacklist")]
    public async Task<IActionResult> AddToBlacklist([FromBody] BlacklistRequest request)
    {
        var entry = new IpBlacklistEntry
        {
            Address = request.Address,
            Reason = request.Reason,
            ExpiresAt = request.ExpiresInHours.HasValue ? DateTime.UtcNow.AddHours(request.ExpiresInHours.Value) : null
        };
        _db.Set<IpBlacklistEntry>().Add(entry);
        await _db.SaveChangesAsync();
        return Ok(entry);
    }

    [HttpDelete("blacklist/{id}")]
    public async Task<IActionResult> RemoveFromBlacklist(int id)
    {
        var entry = await _db.Set<IpBlacklistEntry>().FindAsync(id);
        if (entry is null) return NotFound();
        _db.Set<IpBlacklistEntry>().Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record AddIpRequest(string Address, bool IsShared = true, int? SubscriptionId = null, string? Note = null);
public record AssignIpRequest(int? SubscriptionId);
public record BlacklistRequest(string Address, string? Reason = null, int? ExpiresInHours = null);
