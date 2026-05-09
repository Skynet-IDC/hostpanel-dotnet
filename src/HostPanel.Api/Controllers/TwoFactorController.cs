using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

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

        // Generate TOTP secret (160-bit)
        var secretBytes = RandomNumberGenerator.GetBytes(20);
        var secret = Base32Encode(secretBytes);

        // Generate recovery codes
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

        if (!ValidateTotp(token.SecretKey, request.Code))
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

        if (!ValidateTotp(token.SecretKey, request.Code))
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
        return Ok(new { enabled = token is not null });
    }

    private static bool ValidateTotp(string secret, string code)
    {
        var secretBytes = Base32Decode(secret);
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        // Check current and adjacent windows for clock skew
        for (var i = -1; i <= 1; i++)
        {
            var step = timeStep + i;
            var stepBytes = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian) Array.Reverse(stepBytes);
            var paddedStep = new byte[8];
            Array.Copy(stepBytes, 0, paddedStep, 8 - stepBytes.Length, stepBytes.Length);

            using var hmac = new HMACSHA1(secretBytes);
            var hash = hmac.ComputeHash(paddedStep);
            var offset = hash[^1] & 0x0f;
            var otp = ((hash[offset] & 0x7f) << 24 |
                       (hash[offset + 1] & 0xff) << 16 |
                       (hash[offset + 2] & 0xff) << 8 |
                       (hash[offset + 3] & 0xff)) % 1000000;

            if (otp.ToString("D6") == code)
                return true;
        }
        return false;
    }

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new System.Text.StringBuilder();
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                result.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1f]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
            result.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1f]);
        return result.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bitsLeft = 0;
        foreach (var c in input.ToUpper())
        {
            var val = alphabet.IndexOf(c);
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }
        return output.ToArray();
    }
}

public record TotpVerifyRequest(string Code);
