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
    /// `customerId` or `customerEmail` query params layer a per-customer override on top —
    /// resellers can email customers a "?customer=alice@acme.com" link that shows the customer's logo
    /// before they even sign in.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(
        [FromQuery] string? resellerId = null,
        [FromQuery] string? customerId = null,
        [FromQuery] string? customerEmail = null)
    {
        BrandingConfig? config = null;

        // Check for reseller-specific branding
        if (!string.IsNullOrEmpty(resellerId) && int.TryParse(resellerId, out var rid))
            config = await _db.Set<BrandingConfig>().FirstOrDefaultAsync(b => b.ResellerId == rid);

        // Fall back to global branding
        config ??= await _db.Set<BrandingConfig>().FirstOrDefaultAsync(b => b.ResellerId == null);

        // Return defaults if nothing configured
        config ??= new BrandingConfig();

        // Layer per-customer override (only fields the customer is allowed to set)
        CustomerBranding? cust = null;
        if (!string.IsNullOrEmpty(customerId))
            cust = await _db.CustomerBrandings.FirstOrDefaultAsync(c => c.UserId == customerId);
        else if (!string.IsNullOrEmpty(customerEmail))
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == customerEmail);
            if (user != null)
                cust = await _db.CustomerBrandings.FirstOrDefaultAsync(c => c.UserId == user.Id);
        }

        return Ok(new
        {
            PanelName = cust?.PanelName ?? config.PanelName,
            LogoUrl = cust?.LogoUrl ?? config.LogoUrl,
            config.FaviconUrl,
            PrimaryColor = cust?.PrimaryColor ?? config.PrimaryColor,
            config.SidebarColor,
            config.AccentColor,
            LoginBackgroundUrl = cust?.LoginBackgroundUrl ?? config.LoginBackgroundUrl,
            LoginMessage = cust?.LoginMessage ?? config.LoginMessage,
            config.FooterText,
            config.SupportUrl,
            config.SupportEmail,
            config.CustomCss,
            config.HidePoweredBy,
            config.Configured,
            CustomerOverride = cust != null
        });
    }

    /// <summary>Get current signed-in customer's own override (returns nulls if none set).</summary>
    [HttpGet("customer/me")]
    [Authorize(AuthenticationSchemes = "MultiAuth")]
    public async Task<IActionResult> GetMyOverride()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();
        var cust = await _db.CustomerBrandings.FirstOrDefaultAsync(c => c.UserId == userId);
        return Ok(cust ?? new CustomerBranding { UserId = userId });
    }

    /// <summary>Set/update per-customer branding override. Admin: any user. Otherwise: self only.</summary>
    [HttpPut("customer/{userId}")]
    [Authorize(AuthenticationSchemes = "MultiAuth")]
    public async Task<IActionResult> UpdateCustomer(string userId, [FromBody] UpdateCustomerBrandingRequest request)
    {
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var callerRole = User.FindFirstValue(ClaimTypes.Role);
        if (callerRole != UserRole.Admin.ToString() && callerId != userId)
            return Forbid();

        if (!await _db.Users.AnyAsync(u => u.Id == userId))
            return NotFound(new { message = "User not found" });

        var cust = await _db.CustomerBrandings.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cust is null)
        {
            cust = new CustomerBranding { UserId = userId };
            _db.CustomerBrandings.Add(cust);
        }

        if (request.PanelName is not null) cust.PanelName = string.IsNullOrWhiteSpace(request.PanelName) ? null : request.PanelName;
        if (request.LogoUrl is not null) cust.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl;
        if (request.PrimaryColor is not null) cust.PrimaryColor = string.IsNullOrWhiteSpace(request.PrimaryColor) ? null : request.PrimaryColor;
        if (request.LoginMessage is not null) cust.LoginMessage = string.IsNullOrWhiteSpace(request.LoginMessage) ? null : request.LoginMessage;
        if (request.LoginBackgroundUrl is not null) cust.LoginBackgroundUrl = string.IsNullOrWhiteSpace(request.LoginBackgroundUrl) ? null : request.LoginBackgroundUrl;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Customer branding updated", cust.UserId, cust.LogoUrl, cust.PrimaryColor });
    }

    /// <summary>Upload a logo and assign it to a customer. Admin: any. Otherwise: self.</summary>
    [HttpPost("customer/{userId}/logo")]
    [Authorize(AuthenticationSchemes = "MultiAuth")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadCustomerLogo(string userId, IFormFile file)
    {
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var callerRole = User.FindFirstValue(ClaimTypes.Role);
        if (callerRole != UserRole.Admin.ToString() && callerId != userId)
            return Forbid();

        if (!await _db.Users.AnyAsync(u => u.Id == userId))
            return NotFound(new { message = "User not found" });

        var allowedTypes = new[] { "image/png", "image/jpeg", "image/svg+xml", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType))
            return BadRequest(new { message = "Invalid file type. Allowed: PNG, JPEG, SVG, WebP" });

        var uploadsDir = Path.Combine("/opt/hostpanel/wwwroot", "branding", "customers");
        Directory.CreateDirectory(uploadsDir);

        var ext = Path.GetExtension(file.FileName);
        var fileName = $"cust_{userId[..Math.Min(userId.Length, 8)]}_{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);

        using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        var url = $"/branding/customers/{fileName}";

        var cust = await _db.CustomerBrandings.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cust is null)
        {
            cust = new CustomerBranding { UserId = userId, LogoUrl = url };
            _db.CustomerBrandings.Add(cust);
        }
        else
        {
            cust.LogoUrl = url;
        }
        await _db.SaveChangesAsync();

        return Ok(new { url });
    }

    /// <summary>Clear a customer's override. Admin: any. Otherwise: self.</summary>
    [HttpDelete("customer/{userId}")]
    [Authorize(AuthenticationSchemes = "MultiAuth")]
    public async Task<IActionResult> DeleteCustomer(string userId)
    {
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var callerRole = User.FindFirstValue(ClaimTypes.Role);
        if (callerRole != UserRole.Admin.ToString() && callerId != userId)
            return Forbid();

        var cust = await _db.CustomerBrandings.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cust is not null)
        {
            _db.CustomerBrandings.Remove(cust);
            await _db.SaveChangesAsync();
        }
        return Ok(new { message = "Customer override removed" });
    }

    /// <summary>Update global branding (admin only).</summary>
    [HttpPut]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
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
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
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
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
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
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
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
        // Any PUT through the global endpoint counts as configured. The SetupWizard
        // explicitly passes Configured=true; treat that as canonical.
        if (r.Configured.HasValue) config.Configured = r.Configured.Value;
        else config.Configured = true;
    }
}

public record UpdateCustomerBrandingRequest(
    string? PanelName = null,
    string? LogoUrl = null,
    string? PrimaryColor = null,
    string? LoginMessage = null,
    string? LoginBackgroundUrl = null
);

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
    int? ResellerId = null,
    bool? Configured = null
);
