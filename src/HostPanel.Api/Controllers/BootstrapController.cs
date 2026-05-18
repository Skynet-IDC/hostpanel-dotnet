using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Infrastructure.Services;

namespace HostPanel.Api.Controllers;

/// <summary>
/// First-launch admin bootstrap. Anonymous endpoints — must self-disable
/// the moment any admin user exists. Designed to replace the installer's
/// --admin-email/--admin-password baked-in credentials.
/// </summary>
[ApiController]
[Route("api/v1/setup")]
[AllowAnonymous]
public class BootstrapController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly JwtService _jwt;
    private readonly ILogger<BootstrapController> _logger;

    public BootstrapController(
        AppDbContext db,
        UserManager<AppUser> userManager,
        JwtService jwt,
        ILogger<BootstrapController> logger)
    {
        _db = db;
        _userManager = userManager;
        _jwt = jwt;
        _logger = logger;
    }

    [HttpGet("bootstrap-status")]
    public async Task<IActionResult> Status()
    {
        var adminExists = await _userManager.Users.AnyAsync(u => u.Role == UserRole.Admin);
        var brandingConfigured = await _db.BrandingConfigs.AnyAsync(b => b.Configured);
        return Ok(new
        {
            adminExists,
            brandingConfigured,
            // Set when the installer left a one-time setup token for the customer to claim.
            // Reserved for future use; null today.
            setupToken = (string?)null
        });
    }

    public record BootstrapAdminRequest(string Email, string Password, string? FirstName, string? LastName);

    [HttpPost("bootstrap-admin")]
    public async Task<IActionResult> BootstrapAdmin([FromBody] BootstrapAdminRequest req)
    {
        // Self-disable: any admin in the system → 409.
        if (await _userManager.Users.AnyAsync(u => u.Role == UserRole.Admin))
            return Conflict(new { error = "Admin already exists. Bootstrap endpoint is closed." });

        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Email and password are required." });

        if (req.Password.Length < 10)
            return BadRequest(new { error = "Password must be at least 10 characters." });

        // Identity password policy enforces complexity (configured in Program.cs).
        var admin = new AppUser
        {
            UserName = req.Email,
            Email = req.Email,
            FirstName = req.FirstName ?? "System",
            LastName = req.LastName ?? "Admin",
            Role = UserRole.Admin,
            EmailConfirmed = true,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(admin, req.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToArray() });

        await _userManager.AddToRoleAsync(admin, UserRole.Admin.ToString());
        _logger.LogInformation("Bootstrap admin created: {Email}", admin.Email);

        // Auto-login so the wizard can proceed without a separate login round-trip.
        var token = _jwt.GenerateToken(admin);
        var refreshToken = _jwt.GenerateRefreshToken();
        return Ok(new AuthResponse(
            Token: token,
            RefreshToken: refreshToken,
            ExpiresAt: DateTime.UtcNow.AddHours(24),
            User: new UserDto(admin.Id, admin.Email!, admin.FirstName, admin.LastName, admin.CompanyName, admin.Role, admin.IsActive)
        ));
    }
}
