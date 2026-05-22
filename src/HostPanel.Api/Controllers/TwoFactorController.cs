using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Infrastructure.Services;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/auth/2fa")]
[Authorize]
public class TwoFactorController : ControllerBase
{
    private readonly AppDbContext _db;

    public TwoFactorController(AppDbContext db) => _db = db;

    [HttpPost("setup")]
    public async Task<IActionResult> Setup()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var existing = await _db.Set<TotpToken>().FirstOrDefaultAsync(t => t.UserId == userId);
        if (existing?.IsVerified == true)
            return BadRequest(new { message = "2FA already enabled" });

        var secretBytes = RandomNumberGenerator.GetBytes(20);
        var secret = TotpHelper.Base32Encode(secretBytes);

        var recoveryCodes = Enumerable.Range(0, 8)
            .Select(_ => $"{RandomNumberGenerator.GetInt32(10000000, 99999999)}")
            .ToList();

        if (existing is not null)
        {
            existing.SecretKey = secret;
            existing.IsVerified = false;
            existing.RecoveryCodes = System.Text.Json.JsonSerializer.Serialize(recoveryCodes);
        }
        else
        {
            _db.Set<TotpToken>().Add(new TotpToken
            {
                UserId = userId,
                SecretKey = secret,
                IsVerified = false,
                RecoveryCodes = System.Text.Json.JsonSerializer.Serialize(recoveryCodes)
            });
        }

        await _db.SaveChangesAsync();

        var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var otpauthUrl = $"otpauth://totp/HostPanel:{userEmail}?secret={secret}&issuer=HostPanel&digits=6&period=30";

        return Ok(new { secret, otpauthUrl, recoveryCodes });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] TotpVerifyRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var token = await _db.Set<TotpToken>().FirstOrDefaultAsync(t => t.UserId == userId);
        if (token is null) return BadRequest(new { message = "Setup 2FA first" });

        if (!TotpHelper.ValidateTotp(token.SecretKey, request.Code))
            return BadRequest(new { message = "Invalid code" });

        token.IsVerified = true;
        await _db.SaveChangesAsync();

        return Ok(new { message = "2FA enabled successfully" });
    }

    [HttpDelete]
    public async Task<IActionResult> Disable([FromBody] TotpVerifyRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var token = await _db.Set<TotpToken>().FirstOrDefaultAsync(t => t.UserId == userId);
        if (token is null) return NotFound();

        if (!TotpHelper.ValidateTotp(token.SecretKey, request.Code))
            return BadRequest(new { message = "Invalid code" });

        _db.Set<TotpToken>().Remove(token);
        await _db.SaveChangesAsync();

        return Ok(new { message = "2FA disabled" });
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var token = await _db.Set<TotpToken>().FirstOrDefaultAsync(t => t.UserId == userId && t.IsVerified);
        // UI-BUG-013: surface recovery code count so the dashboard can render an accurate
        // "N remaining" instead of always reading 0 from a missing field.
        var recoveryCodesRemaining = 0;
        if (token is not null && !string.IsNullOrEmpty(token.RecoveryCodes))
        {
            try
            {
                var codes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(token.RecoveryCodes);
                recoveryCodesRemaining = codes?.Count ?? 0;
            }
            catch { recoveryCodesRemaining = 0; }
        }
        return Ok(new { enabled = token is not null, recoveryCodesRemaining });
    }
}

public record TotpVerifyRequest(string Code);
