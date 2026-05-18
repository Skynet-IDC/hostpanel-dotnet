using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

/// <summary>
/// First-launch SetupWizard endpoints. The frontend posts each wizard step here;
/// SMTP/DNS/CF steps are optional and may no-op if the operator skips them.
///
/// DNS resolver + Cloudflare token are stored in the generic <see cref="SystemSetting"/>
/// key/value table under the "setup" category.
/// </summary>
[ApiController]
[Route("api/v1/setup")]
public class SetupController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<SetupController> _logger;

    public SetupController(AppDbContext db, ILogger<SetupController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Tells the frontend whether the first-launch wizard should be shown.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> Status()
    {
        var branding = await _db.Set<BrandingConfig>().FirstOrDefaultAsync(b => b.ResellerId == null);
        var planCount = await _db.Set<ServicePlan>().CountAsync();
        return Ok(new
        {
            configured = branding?.Configured == true,
            hasPlans = planCount > 0,
            hasBranding = branding != null,
        });
    }

    /// <summary>Saves an SMTP relay used by the panel for outbound mail
    /// (welcome emails, alerts, etc.). Maps to <see cref="SmtpRelayConfig"/> singleton.</summary>
    [HttpPost("smtp")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> SaveSmtp([FromBody] SetupSmtpRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Host))
            return BadRequest(new { message = "Host is required" });

        var config = await _db.Set<SmtpRelayConfig>().FirstOrDefaultAsync(c => c.IsGlobal && c.DomainId == null);
        if (config is null)
        {
            config = new SmtpRelayConfig { IsGlobal = true };
            _db.Set<SmtpRelayConfig>().Add(config);
        }

        config.RelayHost = r.Host.Trim();
        config.RelayPort = r.Port > 0 ? r.Port : 587;
        config.RelayUsername = string.IsNullOrWhiteSpace(r.Username) ? null : r.Username.Trim();
        config.RelayPassword = string.IsNullOrWhiteSpace(r.Password) ? null : r.Password;
        config.UseTls = r.UseTls ?? true;

        if (!string.IsNullOrWhiteSpace(r.FromAddress))
            await UpsertSettingAsync("setup.smtp.fromAddress", r.FromAddress.Trim(), "setup", isSensitive: false);

        await _db.SaveChangesAsync();
        _logger.LogInformation("SMTP relay configured via SetupWizard: host={Host} port={Port}", config.RelayHost, config.RelayPort);
        return Ok(new { message = "SMTP relay saved" });
    }

    /// <summary>Stores the upstream DNS resolver that the panel's PowerDNS forwards to.</summary>
    [HttpPut("dns")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> SaveDns([FromBody] SetupDnsRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Resolver))
            return BadRequest(new { message = "Resolver is required" });

        await UpsertSettingAsync("setup.dns.resolver", r.Resolver.Trim(), "setup", isSensitive: false);
        await _db.SaveChangesAsync();
        _logger.LogInformation("DNS resolver saved via SetupWizard: {Resolver}", r.Resolver);
        return Ok(new { message = "DNS resolver saved", note = "Apply manually to /etc/powerdns/recursor.conf or wait for the agent's recursor sync." });
    }

    /// <summary>Stores a Cloudflare API token for ACME DNS-01 wildcards
    /// and for auto-DNS on Cloudflare-hosted zones.</summary>
    [HttpPut("cloudflare")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> SaveCloudflare([FromBody] SetupCloudflareRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ApiToken))
            return BadRequest(new { message = "Cloudflare API token is required" });

        await UpsertSettingAsync("setup.cloudflare.apiToken", r.ApiToken.Trim(), "setup", isSensitive: true);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Cloudflare API token saved via SetupWizard (length={Len})", r.ApiToken.Length);
        return Ok(new { message = "Cloudflare token saved" });
    }

    /// <summary>Returns non-sensitive settings. Sensitive values are returned as boolean has-flag only.</summary>
    [HttpGet("settings")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> ListSettings()
    {
        var settings = await _db.Set<SystemSetting>().ToListAsync();
        return Ok(settings.Select(s => new
        {
            s.Key,
            s.Category,
            s.IsSensitive,
            Value = s.IsSensitive ? null : s.Value,
            HasValue = !string.IsNullOrEmpty(s.Value),
            s.UpdatedAt,
        }));
    }

    private async Task UpsertSettingAsync(string key, string value, string category, bool isSensitive)
    {
        var existing = await _db.Set<SystemSetting>().FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
        {
            _db.Set<SystemSetting>().Add(new SystemSetting
            {
                Key = key,
                Value = value,
                Category = category,
                IsSensitive = isSensitive,
            });
        }
        else
        {
            existing.Value = value;
            existing.Category = category;
            existing.IsSensitive = isSensitive;
        }
    }
}

public record SetupSmtpRequest(
    string Host,
    int Port = 587,
    string? Username = null,
    string? Password = null,
    string? FromAddress = null,
    bool? UseTls = null
);

public record SetupDnsRequest(string Resolver);
public record SetupCloudflareRequest(string ApiToken);
