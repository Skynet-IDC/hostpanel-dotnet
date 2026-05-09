using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/branding")]
public class BrandingController : ControllerBase
{
    private readonly AppDbContext _db;

    public BrandingController(AppDbContext db) => _db = db;

    /// <summary>
    /// Get branding config. Public endpoint - frontend calls this before login to skin the UI.
    /// If user is a reseller's customer, returns reseller branding if configured.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get([FromQuery] string? resellerId = null)
    {
        BrandingConfig? config = null;

        // Check for reseller-specific branding
        if (!string.IsNullOrEmpty(resellerId) && int.TryParse(resellerId, out var rid))
            config = await _db.Set<BrandingConfig>().FirstOrDefaultAsync(b => b.ResellerId == rid);

        // Fall back to global branding
        config ??= await _db.Set<BrandingConfig>().FirstOrDefaultAsync(b => b.ResellerId == null);

        // Return defaults if nothing configured
        config ??= new BrandingConfig();

        return Ok(new
        {
            config.PanelName,
            config.LogoUrl,
            config.FaviconUrl,
            config.PrimaryColor,
            config.SidebarColor,
            config.AccentColor,
            config.LoginBackgroundUrl,
            config.LoginMessage,
            config.FooterText,
            config.SupportUrl,
            config.SupportEmail,
            config.CustomCss,
            config.HidePoweredBy
        });
    }

    /// <summary>Update global branding (admin only).</summary>
    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateGlobal([FromBody] UpdateBrandingRequest request)
    {
        var config = await _db.Set<BrandingConfig>().FirstOrDefaultAsync(b => b.ResellerId == null);

        if (config is null)
        {
            config = new BrandingConfig();
            _db.Set<BrandingConfig>().Add(config);
        }

        ApplyUpdate(config, request);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Global branding updated" });
    }

    /// <summary>Update reseller-specific branding. Resellers can brand the panel for their customers.</summary>
    [HttpPut("reseller")]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> UpdateReseller([FromBody] UpdateBrandingRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        // Determine reseller ID
        int? resellerId = null;
        if (role == UserRole.Reseller.ToString())
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return Unauthorized();
            // Use a hash of the user ID as the reseller branding key
            resellerId = userId?.GetHashCode() & 0x7FFFFFFF;
        }
        else if (request.ResellerId.HasValue)
        {
            resellerId = request.ResellerId;
        }

        if (resellerId is null) return BadRequest(new { message = "Reseller ID required" });

        var config = await _db.Set<BrandingConfig>().FirstOrDefaultAsync(b => b.ResellerId == resellerId);

        if (config is null)
        {
            config = new BrandingConfig { ResellerId = resellerId };
            _db.Set<BrandingConfig>().Add(config);
        }

        ApplyUpdate(config, request);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Reseller branding updated", resellerId });
    }

    /// <summary>Upload a logo file.</summary>
    [HttpPost("logo")]
    [Authorize(Roles = "Admin,Reseller")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        var allowedTypes = new[] { "image/png", "image/jpeg", "image/svg+xml", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType))
            return BadRequest(new { message = "Invalid file type. Allowed: PNG, JPEG, SVG, WebP" });

        var uploadsDir = Path.Combine("/opt/hostpanel/wwwroot", "branding");
        Directory.CreateDirectory(uploadsDir);

        var ext = Path.GetExtension(file.FileName);
        var fileName = $"logo_{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);

        using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        return Ok(new { url = $"/branding/{fileName}" });
    }

    /// <summary>Reset branding to defaults.</summary>
    [HttpDelete]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ResetGlobal()
    {
        var config = await _db.Set<BrandingConfig>().FirstOrDefaultAsync(b => b.ResellerId == null);
        if (config is not null)
        {
            _db.Set<BrandingConfig>().Remove(config);
            await _db.SaveChangesAsync();
        }
        return Ok(new { message = "Branding reset to defaults" });
    }

    private static void ApplyUpdate(BrandingConfig config, UpdateBrandingRequest r)
    {
        if (r.PanelName is not null) config.PanelName = r.PanelName;
        if (r.LogoUrl is not null) config.LogoUrl = r.LogoUrl;
        if (r.FaviconUrl is not null) config.FaviconUrl = r.FaviconUrl;
        if (r.PrimaryColor is not null) config.PrimaryColor = r.PrimaryColor;
        if (r.SidebarColor is not null) config.SidebarColor = r.SidebarColor;
        if (r.AccentColor is not null) config.AccentColor = r.AccentColor;
        if (r.LoginBackgroundUrl is not null) config.LoginBackgroundUrl = r.LoginBackgroundUrl;
        if (r.LoginMessage is not null) config.LoginMessage = r.LoginMessage;
        if (r.FooterText is not null) config.FooterText = r.FooterText;
        if (r.SupportUrl is not null) config.SupportUrl = r.SupportUrl;
        if (r.SupportEmail is not null) config.SupportEmail = r.SupportEmail;
        if (r.CustomCss is not null) config.CustomCss = r.CustomCss;
        if (r.HidePoweredBy.HasValue) config.HidePoweredBy = r.HidePoweredBy.Value;
    }
}

public record UpdateBrandingRequest(
    string? PanelName = null,
    string? LogoUrl = null,
    string? FaviconUrl = null,
    string? PrimaryColor = null,
    string? SidebarColor = null,
    string? AccentColor = null,
    string? LoginBackgroundUrl = null,
    string? LoginMessage = null,
    string? FooterText = null,
    string? SupportUrl = null,
    string? SupportEmail = null,
    string? CustomCss = null,
    bool? HidePoweredBy = null,
    int? ResellerId = null
);
