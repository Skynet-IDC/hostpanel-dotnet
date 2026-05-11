using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Api.Middleware;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Modules.Web;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/wordpress")]
[Authorize]
public class WordPressController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly WordPressToolkit _wp;
    private readonly TenantAuthorizationService _auth;

    public WordPressController(AppDbContext db, WordPressToolkit wp, TenantAuthorizationService auth)
    {
        _db = db;
        _wp = wp;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        // Join through Domain -> Subscription to apply tenant filter
        var domains = _auth.FilterDomains(User,
            _db.Domains.Include(d => d.Subscription).ThenInclude(s => s.User));

        var sites = await _db.Set<WordPressSite>()
            .Where(s => domains.Select(d => d.Id).Contains(s.DomainId))
            .Include(s => s.Domain)
            .Select(s => new
            {
                s.Id, s.DomainId, domainName = s.Domain.Name, s.SiteUrl,
                s.Version, s.AdminUser, s.AutoUpdate, s.MaintenanceMode,
                s.IsStaging, s.ProductionSiteId, s.SecurityStatus, s.LastScannedAt
            })
            .ToListAsync();

        return Ok(sites);
    }

    [HttpPost("install")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Install([FromBody] WpInstallRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, request.DomainId)) return Forbid();

        try
        {
            var site = await _wp.InstallAsync(request.DomainId, request.AdminUser,
                request.AdminPassword, request.AdminEmail, request.SiteTitle);
            return Ok(new { site.Id, site.SiteUrl, site.Version, message = "WordPress installed" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{siteId}/staging")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> CreateStaging(int siteId)
    {
        if (!await CanAccessSiteAsync(siteId)) return Forbid();
        var staging = await _wp.CreateStagingAsync(siteId);
        return Ok(new { staging.Id, staging.SiteUrl, message = "Staging site created" });
    }

    [HttpPost("{siteId}/push-staging")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> PushStaging(int siteId)
    {
        if (!await CanAccessSiteAsync(siteId)) return Forbid();
        await _wp.PushStagingToProductionAsync(siteId);
        return Ok(new { message = "Staging pushed to production" });
    }

    [HttpPost("update-all")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> UpdateAll()
    {
        await _wp.UpdateAllAsync();
        return Ok(new { message = "WordPress updates initiated" });
    }

    [HttpPost("{siteId}/security-scan")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> SecurityScan(int siteId)
    {
        if (!await CanAccessSiteAsync(siteId)) return Forbid();
        var result = await _wp.SecurityScanAsync(siteId);
        return Ok(result);
    }

    [HttpPut("{siteId}/maintenance")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> SetMaintenance(int siteId, [FromBody] MaintenanceRequest request)
    {
        if (!await CanAccessSiteAsync(siteId)) return Forbid();
        await _wp.SetMaintenanceModeAsync(siteId, request.Enabled);
        return Ok(new { maintenanceMode = request.Enabled });
    }

    [HttpPut("{siteId}/auto-update")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> SetAutoUpdate(int siteId, [FromBody] AutoUpdateRequest request)
    {
        if (!await CanAccessSiteAsync(siteId)) return Forbid();
        var site = await _db.Set<WordPressSite>().FindAsync(siteId);
        if (site is null) return NotFound();
        site.AutoUpdate = request.Enabled;
        await _db.SaveChangesAsync();
        return Ok(new { autoUpdate = request.Enabled });
    }

    private async Task<bool> CanAccessSiteAsync(int siteId)
    {
        var site = await _db.Set<WordPressSite>().FindAsync(siteId);
        if (site is null) return false;
        return await _auth.CanAccessDomainAsync(User, site.DomainId);
    }
}

public record WpInstallRequest(int DomainId, string AdminUser = "admin", string AdminPassword = "", string AdminEmail = "", string SiteTitle = "My Site");
public record MaintenanceRequest(bool Enabled);
public record AutoUpdateRequest(bool Enabled);
