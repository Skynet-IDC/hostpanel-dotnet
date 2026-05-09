using System.Net.Http.Headers;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Infrastructure.Services.Migration;

public class PleskMigrationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PleskMigrationService> _logger;

    public PleskMigrationService(AppDbContext db, ILogger<PleskMigrationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Import subscriptions and domains from Plesk XML API.
    /// Connect to Plesk's API at https://plesk-host:8443/enterprise/control/agent.php
    /// </summary>
    public async Task<MigrationResult> ImportFromPleskApiAsync(
        string pleskHost, string apiKey, string targetUserId, int servicePlanId,
        CancellationToken ct = default)
    {
        var result = new MigrationResult();

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("KEY", apiKey);
        http.BaseAddress = new Uri($"https://{pleskHost}:8443");

        _logger.LogInformation("Starting Plesk migration from {Host}", pleskHost);

        // 1. Get all subscriptions/domains from Plesk
        var domainsXml = await FetchPleskDataAsync(http, """
            <packet>
              <webspace><get><filter/><dataset><gen_info/><hosting/><stat/></dataset></get></webspace>
            </packet>
            """, ct);

        var webspaces = domainsXml.Descendants("result").Where(r => r.Element("status")?.Value == "ok");

        foreach (var ws in webspaces)
        {
            try
            {
                var domainName = ws.Element("data")?.Element("gen_info")?.Element("name")?.Value;
                if (string.IsNullOrEmpty(domainName)) continue;

                _logger.LogInformation("Migrating domain {Domain}", domainName);

                // Create subscription
                var systemUsername = $"hp_{domainName.Replace(".", "_").Replace("-", "_")}";
                if (systemUsername.Length > 30) systemUsername = systemUsername[..30];

                var subscription = new Subscription
                {
                    UserId = targetUserId,
                    ServicePlanId = servicePlanId,
                    Status = SubscriptionStatus.PendingSetup,
                    BillingPeriod = BillingPeriod.Monthly,
                    SystemUsername = systemUsername,
                    NextInvoiceDate = DateTime.UtcNow.AddMonths(1)
                };

                _db.Subscriptions.Add(subscription);
                await _db.SaveChangesAsync(ct);

                // Create domain
                var domain = new Domain
                {
                    SubscriptionId = subscription.Id,
                    Name = domainName,
                    DocumentRoot = $"/var/www/{systemUsername}/httpdocs",
                    IsMainDomain = true,
                    PhpVersion = "8.3"
                };
                _db.Domains.Add(domain);
                await _db.SaveChangesAsync(ct);

                result.DomainsImported++;

                // 2. Get email accounts for this domain
                await ImportEmailAccountsAsync(http, domainName, domain.Id, ct, result);

                // 3. Get databases
                await ImportDatabasesAsync(http, domainName, subscription.Id, ct, result);

                result.SubscriptionsCreated++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to migrate webspace: {ex.Message}");
                _logger.LogError(ex, "Failed to migrate webspace");
            }
        }

        _logger.LogInformation("Migration complete: {Subs} subscriptions, {Domains} domains, {Emails} emails, {Dbs} databases, {Errors} errors",
            result.SubscriptionsCreated, result.DomainsImported, result.EmailAccountsImported, result.DatabasesImported, result.Errors.Count);

        return result;
    }

    private async Task ImportEmailAccountsAsync(HttpClient http, string domainName, int domainId, CancellationToken ct, MigrationResult result)
    {
        try
        {
            var mailXml = await FetchPleskDataAsync(http, $"""
                <packet>
                  <mail><get_info><filter><site-name>{domainName}</site-name></filter><mailbox/></get_info></mail>
                </packet>
                """, ct);

            foreach (var mailbox in mailXml.Descendants("mailname"))
            {
                var localPart = mailbox.Element("name")?.Value;
                if (string.IsNullOrEmpty(localPart)) continue;

                var mb = new Mailbox
                {
                    DomainId = domainId,
                    Address = $"{localPart}@{domainName}",
                    PasswordHash = "{MIGRATED}",  // Password must be reset
                    QuotaMb = 500,
                    IsActive = true
                };
                _db.Mailboxes.Add(mb);
                result.EmailAccountsImported++;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Email import for {domainName}: {ex.Message}");
        }
    }

    private async Task ImportDatabasesAsync(HttpClient http, string domainName, int subscriptionId, CancellationToken ct, MigrationResult result)
    {
        try
        {
            var dbXml = await FetchPleskDataAsync(http, $"""
                <packet>
                  <database><get-db><filter><site-name>{domainName}</site-name></filter></get-db></database>
                </packet>
                """, ct);

            foreach (var dbNode in dbXml.Descendants("db"))
            {
                var dbName = dbNode.Element("name")?.Value;
                var dbType = dbNode.Element("type")?.Value;
                if (string.IsNullOrEmpty(dbName)) continue;

                var type = dbType?.ToLower() switch
                {
                    "mysql" => DatabaseType.MySql,
                    "postgresql" => DatabaseType.PostgreSql,
                    "mssql" => DatabaseType.MsSql,
                    _ => DatabaseType.MySql
                };

                var hostedDb = new HostedDatabase
                {
                    SubscriptionId = subscriptionId,
                    Name = dbName,
                    Type = type,
                    DbUsername = $"hp_{dbName}_u",
                    DbPasswordHash = "{MIGRATED}"
                };
                _db.HostedDatabases.Add(hostedDb);
                result.DatabasesImported++;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Database import for {domainName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Import from a Plesk backup file (PMB format).
    /// </summary>
    public async Task<MigrationResult> ImportFromBackupFileAsync(
        string backupFilePath, string targetUserId, int servicePlanId,
        CancellationToken ct = default)
    {
        var result = new MigrationResult();

        if (!File.Exists(backupFilePath))
            throw new FileNotFoundException("Backup file not found", backupFilePath);

        _logger.LogInformation("Starting Plesk backup import from {Path}", backupFilePath);

        // Plesk backups are tar.gz files with XML metadata
        // Extract and parse the dump.xml file for structure
        var tempDir = Path.Combine(Path.GetTempPath(), $"hostpanel_migration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Extract backup (agent would do this on Linux)
            // For now, log that this needs agent execution
            _logger.LogInformation("Backup file needs extraction to {TempDir} - use Agent for tar extraction on Linux", tempDir);

            result.Notes.Add("Backup file import requires the HostPanel Agent to run on Linux for file extraction.");
            result.Notes.Add($"Backup path: {backupFilePath}");
            result.Notes.Add("After extraction, domain configs will be read from dump.xml and files copied to the target user's home directory.");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        return result;
    }

    private static async Task<XDocument> FetchPleskDataAsync(HttpClient http, string xmlRequest, CancellationToken ct)
    {
        var content = new StringContent(xmlRequest, System.Text.Encoding.UTF8, "text/xml");
        var response = await http.PostAsync("/enterprise/control/agent.php", content, ct);
        response.EnsureSuccessStatusCode();
        var responseXml = await response.Content.ReadAsStringAsync(ct);
        return XDocument.Parse(responseXml);
    }
}

public class MigrationResult
{
    public int SubscriptionsCreated { get; set; }
    public int DomainsImported { get; set; }
    public int EmailAccountsImported { get; set; }
    public int DatabasesImported { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}
