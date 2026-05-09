using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ApiKeysController : ControllerBase
{
    private readonly AppDbContext _db;

    public ApiKeysController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var keys = await _db.ApiKeys
            .Where(k => k.UserId == userId)
            .Select(k => new { k.Id, k.Name, k.KeyPrefix, k.Scopes, k.IsActive, k.ExpiresAt, k.LastUsedAt, k.CreatedAt })
            .ToListAsync();
        return Ok(keys);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Generate key: hp_ + 6-char prefix + _ + 32-char random
        var prefix = $"hp_{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLower()}";
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLower();
        var fullKey = $"{prefix}_{secret}";

        var keyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(fullKey)));

        var apiKey = new ApiKey
        {
            UserId = userId,
            Name = request.Name,
            KeyHash = keyHash,
            KeyPrefix = prefix[..6],
            Scopes = request.Scopes,
            ExpiresAt = request.ExpiresInDays.HasValue ? DateTime.UtcNow.AddDays(request.ExpiresInDays.Value) : null,
            IsActive = true
        };

        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync();

        // Return the full key only once at creation
        return CreatedAtAction(nameof(List), new
        {
            apiKey.Id,
            apiKey.Name,
            key = fullKey,
            apiKey.Scopes,
            apiKey.ExpiresAt,
            message = "Save this key now. It will not be shown again."
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (key is null) return NotFound();

        key.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateApiKeyRequest(string Name, string? Scopes = null, int? ExpiresInDays = null);
