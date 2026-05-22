using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Modules.Web;

public class WordPressToolkit
{
    private readonly AppDbContext _db;
    private readonly IServerAgent _agent;
    private readonly ILogger<WordPressToolkit> _logger;

    public WordPressToolkit(AppDbContext db, IServerAgent agent, ILogger<WordPressToolkit> logger)
    {
        _db = db;
        _agent = agent;
        _logger = logger;
    }

    public async Task<WordPressSite> InstallAsync(int domainId, string adminUser, string adminPassword, string adminEmail, string siteTitle, CancellationToken ct = default)
    {
        var domain = await _db.Domains
            .Include(d => d.Subscription)
            .FirstOrDefaultAsync(d => d.Id == domainId, ct)
            ?? throw new InvalidOperationException("Domain not found");

        // MySQL identifier rules: letters/digits/underscore/dollar only — no dots or hyphens.
        // Truncate dbName to MySQL's 64-char limit (subscription systemUsername + domain
        // segments can blow past it on long customer domains). UI-BUG-007: domain.Name
        // hyphens were left in the dbName, generating "wp_..._wf-test_example_com" which
        // MySQL rejects with "Invalid database name".
        static string Mysqlify(string s) => s.Replace('.', '_').Replace('-', '_');
        var dbName = $"wp_{Mysqlify(domain.Subscription.SystemUsername)}_{Mysqlify(domain.Name)}";
        if (dbName.Length > 64) dbName = dbName[..64];
        // Domain-unique user so each WP site has its own MySQL identity.
        // Truncate to 32 chars (MySQL username limit).
        var rawUser = $"wp_{Mysqlify(domain.Name)}";
        var dbUser = rawUser.Length > 32 ? rawUser[..32] : rawUser;
        var dbPass = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));

        _logger.LogInformation("Installing WordPress on {Domain}", domain.Name);

        // Pick http:// when SSL isn't enabled yet — pinning https:// before issue_ssl runs
        // leaves WP_HOME/WP_SITEURL pointing at a port that nginx isn't listening on, which
        // creates a fresh-install redirect loop. After SSL is issued, callers should run
        // `wp option update home/siteurl https://...` or re-issue with --skip-config.
        var scheme = domain.SslEnabled ? "https" : "http";
        var siteUrl = $"{scheme}://{domain.Name}";

        // Create MySQL database for WordPress
        var installResult = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "wordpress.install",
            Params = new Dictionary<string, object?>
            {
                ["docRoot"] = domain.DocumentRoot,
                ["username"] = domain.Subscription.SystemUsername,
                ["dbName"] = dbName,
                ["dbUser"] = dbUser,
                ["dbPass"] = dbPass,
                ["siteUrl"] = siteUrl,
                ["siteTitle"] = siteTitle,
                ["adminUser"] = adminUser,
                ["adminPassword"] = adminPassword,
                ["adminEmail"] = adminEmail
            }
        }, ct);

        if (!installResult.IsSuccess || installResult.Result?.Success != true)
        {
            var err = installResult.Error?.Message;
            if (string.IsNullOrEmpty(err) && installResult.Result?.Data is { } d
                && d.TryGetValue("error", out var e))
                err = e?.ToString();
            _logger.LogError("WordPress install failed on {Domain}: {Error}", domain.Name, err ?? "(unknown)");
            throw new InvalidOperationException($"WordPress install failed: {err ?? "(unknown)"}");
        }

        var site = new WordPressSite
        {
            DomainId = domainId,
            Path = "/",
            Version = "latest",
            SiteUrl = siteUrl,
            AdminUser = adminUser,
            DbName = dbName,
            DbUser = dbUser,
            AutoUpdate = true,
            SecurityStatus = "good"
        };

        _db.Set<WordPressSite>().Add(site);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("WordPress installed on {Domain}", domain.Name);
        return site;
    }

    public async Task<WordPressSite> CreateStagingAsync(int siteId, CancellationToken ct = default)
    {
        var production = await _db.Set<WordPressSite>()
            .Include(s => s.Domain).ThenInclude(d => d.Subscription)
            .FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new InvalidOperationException("Site not found");

        var stagingDomain = $"staging.{production.Domain.Name}";
        var stagingDocRoot = $"{production.Domain.DocumentRoot}_staging";

        // Clone files and database via agent
        await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "wordpress.clone",
            Params = new Dictionary<string, object?>
            {
                ["sourceDocRoot"] = production.Domain.DocumentRoot,
                ["destDocRoot"] = stagingDocRoot,
                ["sourceDb"] = production.DbName,
                ["destDb"] = $"{production.DbName}_staging",
                ["username"] = production.Domain.Subscription.SystemUsername,
                ["oldUrl"] = production.SiteUrl,
                ["newUrl"] = $"https://{stagingDomain}"
            }
        }, ct);

        var staging = new WordPressSite
        {
            DomainId = production.DomainId,
            Path = "/",
            Version = production.Version,
            SiteUrl = $"https://{stagingDomain}",
            AdminUser = production.AdminUser,
            DbName = $"{production.DbName}_staging",
            DbUser = production.DbUser,
            IsStaging = true,
            ProductionSiteId = siteId,
            AutoUpdate = false
        };

        _db.Set<WordPressSite>().Add(staging);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Staging site created for {Domain}", production.Domain.Name);
        return staging;
    }

    public async Task PushStagingToProductionAsync(int stagingSiteId, CancellationToken ct = default)
    {
        var staging = await _db.Set<WordPressSite>()
            .Include(s => s.ProductionSite).ThenInclude(p => p!.Domain).ThenInclude(d => d.Subscription)
            .FirstOrDefaultAsync(s => s.Id == stagingSiteId && s.IsStaging, ct)
            ?? throw new InvalidOperationException("Staging site not found");

        var production = staging.ProductionSite
            ?? throw new InvalidOperationException("Production site not linked");

        await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "wordpress.pushStaging",
            Params = new Dictionary<string, object?>
            {
                ["stagingDocRoot"] = $"{production.Domain.DocumentRoot}_staging",
                ["productionDocRoot"] = production.Domain.DocumentRoot,
                ["stagingDb"] = staging.DbName,
                ["productionDb"] = production.DbName,
                ["username"] = production.Domain.Subscription.SystemUsername,
                ["oldUrl"] = staging.SiteUrl,
                ["newUrl"] = production.SiteUrl
            }
        }, ct);

        _logger.LogInformation("Staging pushed to production for {Domain}", production.Domain.Name);
    }

    public async Task UpdateAllAsync(CancellationToken ct = default)
    {
        var sites = await _db.Set<WordPressSite>()
            .Include(s => s.Domain).ThenInclude(d => d.Subscription)
            .Where(s => s.AutoUpdate && !s.IsStaging)
            .ToListAsync(ct);

        _logger.LogInformation("Updating {Count} WordPress sites", sites.Count);

        foreach (var site in sites)
        {
            try
            {
                await _agent.ExecuteAsync(new AgentRequest
                {
                    Method = "wordpress.update",
                    Params = new Dictionary<string, object?>
                    {
                        ["docRoot"] = site.Domain.DocumentRoot,
                        ["username"] = site.Domain.Subscription.SystemUsername,
                        ["updateCore"] = true,
                        ["updatePlugins"] = true,
                        ["updateThemes"] = true
                    }
                }, ct);

                _logger.LogInformation("Updated WordPress on {Domain}", site.Domain.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update WordPress on {Domain}", site.Domain.Name);
            }
        }
    }

    public async Task<object> SecurityScanAsync(int siteId, CancellationToken ct = default)
    {
        var site = await _db.Set<WordPressSite>()
            .Include(s => s.Domain).ThenInclude(d => d.Subscription)
            .FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new InvalidOperationException("Site not found");

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "wordpress.securityScan",
            Params = new Dictionary<string, object?>
            {
                ["docRoot"] = site.Domain.DocumentRoot,
                ["username"] = site.Domain.Subscription.SystemUsername
            }
        }, ct);

        site.LastScannedAt = DateTime.UtcNow;
        site.SecurityStatus = result.Result?.Data?.TryGetValue("status", out var s) == true ? s?.ToString() ?? "unknown" : "unknown";
        await _db.SaveChangesAsync(ct);

        return result.Result?.Data ?? new Dictionary<string, object?>();
    }

    public async Task SetMaintenanceModeAsync(int siteId, bool enabled, CancellationToken ct = default)
    {
        var site = await _db.Set<WordPressSite>().FindAsync([siteId], ct)
            ?? throw new InvalidOperationException("Site not found");

        site.MaintenanceMode = enabled;
        await _db.SaveChangesAsync(ct);
    }
}
