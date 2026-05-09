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

        var dbName = $"wp_{domain.Subscription.SystemUsername}_{domain.Name.Replace(".", "_")}";
        var dbUser = $"wp_{domain.Subscription.SystemUsername}";
        var dbPass = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));

        _logger.LogInformation("Installing WordPress on {Domain}", domain.Name);

        // Create MySQL database for WordPress
        await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "wordpress.install",
            Params = new Dictionary<string, object?>
            {
                ["docRoot"] = domain.DocumentRoot,
                ["username"] = domain.Subscription.SystemUsername,
                ["dbName"] = dbName,
                ["dbUser"] = dbUser,
                ["dbPass"] = dbPass,
                ["siteUrl"] = $"https://{domain.Name}",
                ["siteTitle"] = siteTitle,
                ["adminUser"] = adminUser,
                ["adminPassword"] = adminPassword,
                ["adminEmail"] = adminEmail
            }
        }, ct);

        var site = new WordPressSite
        {
            DomainId = domainId,
            Path = "/",
            Version = "latest",
            SiteUrl = $"https://{domain.Name}",
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
