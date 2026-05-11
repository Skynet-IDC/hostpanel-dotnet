using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Api.Middleware;
using HostPanel.Infrastructure.Data;
using HostPanel.Modules.Ssl;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/domains/{domainId}/ssl")]
[Authorize]
public class SslController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SslService _sslService;
    private readonly TenantAuthorizationService _auth;

    public SslController(AppDbContext db, SslService sslService, TenantAuthorizationService auth)
    {
        _db = db;
        _sslService = sslService;
        _auth = auth;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var cert = await _db.SslCertificates
            .FirstOrDefaultAsync(c => c.DomainId == domainId);

        if (cert is null)
            return Ok(new { enabled = false });

        return Ok(new
        {
            enabled = true,
            provider = cert.Provider,
            issuedAt = cert.IssuedAt,
            expiresAt = cert.ExpiresAt,
            autoRenew = cert.AutoRenew,
            daysUntilExpiry = cert.ExpiresAt.HasValue
                ? (int)(cert.ExpiresAt.Value - DateTime.UtcNow).TotalDays
                : (int?)null
        });
    }

    [HttpPost("issue")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Issue(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        try
        {
            var cert = await _sslService.IssueCertificateAsync(domainId, HttpContext.RequestAborted);
            return Ok(new
            {
                success = true,
                provider = cert.Provider,
                issuedAt = cert.IssuedAt,
                expiresAt = cert.ExpiresAt
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("toggle-auto-renew")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> ToggleAutoRenew(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var cert = await _db.SslCertificates.FirstOrDefaultAsync(c => c.DomainId == domainId);
        if (cert is null) return NotFound();

        cert.AutoRenew = !cert.AutoRenew;
        await _db.SaveChangesAsync();

        return Ok(new { autoRenew = cert.AutoRenew });
    }
}
