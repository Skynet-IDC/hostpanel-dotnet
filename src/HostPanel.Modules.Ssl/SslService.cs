using System.Net.Http.Json;
using Certes;
using Certes.Acme;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Modules.Ssl;

public class SslService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<SslService> _logger;
    private readonly IServerAgent _agent;
    private readonly string _certBasePath;
    private readonly bool _useStaging;

    public SslService(AppDbContext db, IConfiguration config, ILogger<SslService> logger, IServerAgent agent)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _agent = agent;
        _certBasePath = config["Ssl:CertPath"] ?? "/etc/hostpanel/ssl";
        _useStaging = config.GetValue("Ssl:UseStaging", true);
    }

    public async Task<SslCertificate> IssueCertificateAsync(int domainId, CancellationToken cancellationToken)
    {
        var domain = await _db.Domains
            .Include(d => d.SslCertificate)
            .FirstOrDefaultAsync(d => d.Id == domainId, cancellationToken)
            ?? throw new InvalidOperationException($"Domain {domainId} not found");

        _logger.LogInformation("Issuing SSL certificate for {Domain}", domain.Name);

        var acmeServer = _useStaging ? WellKnownServers.LetsEncryptStagingV2 : WellKnownServers.LetsEncryptV2;
        var accountEmail = _config["Ssl:AccountEmail"] ?? "admin@hostpanel.local";

        // Load or create ACME account
        var accountKeyPath = Path.Combine(_certBasePath, "account.pem");
        AcmeContext acme;

        if (File.Exists(accountKeyPath))
        {
            var accountKey = KeyFactory.FromPem(await File.ReadAllTextAsync(accountKeyPath, cancellationToken));
            acme = new AcmeContext(acmeServer, accountKey);
            await acme.Account();
        }
        else
        {
            acme = new AcmeContext(acmeServer);
            await acme.NewAccount(accountEmail, termsOfServiceAgreed: true);
            Directory.CreateDirectory(_certBasePath);
            await File.WriteAllTextAsync(accountKeyPath, acme.AccountKey.ToPem(), cancellationToken);
        }

        // Create order
        var order = await acme.NewOrder(new[] { domain.Name });

        // HTTP-01 challenge
        var authz = (await order.Authorizations()).First();
        var challenge = await authz.Http();
        var token = challenge.Token;
        var keyAuth = challenge.KeyAuthz;

        // Write challenge file to the domain's web root
        var challengeDir = Path.Combine(domain.DocumentRoot, ".well-known", "acme-challenge");
        Directory.CreateDirectory(challengeDir);
        var challengePath = Path.Combine(challengeDir, token);
        await File.WriteAllTextAsync(challengePath, keyAuth, cancellationToken);

        _logger.LogInformation("Challenge file written for {Domain}, validating...", domain.Name);

        // Validate
        var result = await challenge.Validate();

        // Poll until valid or invalid
        var maxAttempts = 20;
        for (var i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(3000, cancellationToken);
            var status = await authz.Resource();
            if (status.Status == Certes.Acme.Resource.AuthorizationStatus.Valid) break;
            if (status.Status == Certes.Acme.Resource.AuthorizationStatus.Invalid)
            {
                // Capture LE's specific failure reason from the authz.Challenges array
                var detail = "no detail provided by ACME server";
                try
                {
                    var failedChallenges = status.Challenges?
                        .Where(c => c.Error is not null)
                        .Select(c => $"[{c.Type}] {c.Error?.Type ?? "?"}: {c.Error?.Detail ?? "?"}")
                        .ToArray();
                    if (failedChallenges is { Length: > 0 })
                        detail = string.Join(" | ", failedChallenges);
                }
                catch (Exception ex)
                {
                    detail = $"(failed to extract challenge errors: {ex.Message})";
                }
                _logger.LogError("ACME validation failed for {Domain}. LE response: {Detail}", domain.Name, detail);
                throw new InvalidOperationException($"ACME validation failed for {domain.Name}: {detail}");
            }
        }

        // Generate certificate
        var privateKey = KeyFactory.NewKey(Certes.KeyAlgorithm.RS256);
        var cert = await order.Generate(new CsrInfo
        {
            CommonName = domain.Name
        }, privateKey);

        // Save certificate files
        var domainCertDir = Path.Combine(_certBasePath, domain.Name);
        Directory.CreateDirectory(domainCertDir);

        var certPath = Path.Combine(domainCertDir, "fullchain.pem");
        var keyPath = Path.Combine(domainCertDir, "privkey.pem");

        await File.WriteAllTextAsync(certPath, cert.ToPem(), cancellationToken);
        await File.WriteAllTextAsync(keyPath, privateKey.ToPem(), cancellationToken);

        // Reconfigure nginx vhost to listen on :443 with the new cert and redirect HTTP -> HTTPS
        // (keeping /.well-known/acme-challenge/ on plain HTTP so renewals via HTTP-01 still work)
        var vhostResult = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.NginxCreateVhost,
            Params = new Dictionary<string, object?>
            {
                ["domain"] = domain.Name,
                ["docRoot"] = domain.DocumentRoot,
                ["phpVersion"] = domain.PhpVersion ?? "8.3",
                ["sslCertPath"] = certPath,
                ["sslKeyPath"] = keyPath
            }
        }, cancellationToken);

        // Two-level success check: outer envelope (Error is null) AND inner handler result (Result.Success).
        // `IsSuccess` alone only checks the envelope; handler-level failures show up in Result.Success=false
        // with the actual error message in Result.Data["error"].
        var vhostHandlerOk = vhostResult.IsSuccess && vhostResult.Result?.Success == true;
        if (!vhostHandlerOk)
        {
            var errMsg = vhostResult.Error?.Message;
            if (string.IsNullOrEmpty(errMsg) && vhostResult.Result?.Data is { } data
                && data.TryGetValue("error", out var e))
                errMsg = e?.ToString();
            _logger.LogWarning("SSL issued for {Domain} but nginx vhost reconfigure failed: {Error}",
                domain.Name, errMsg ?? "(no detail)");
        }

        // Cleanup challenge file
        try { File.Delete(challengePath); } catch { }

        // Update or create certificate record
        var sslCert = domain.SslCertificate;
        if (sslCert is null)
        {
            sslCert = new SslCertificate
            {
                DomainId = domainId,
                Provider = "LetsEncrypt",
                CertificatePath = certPath,
                PrivateKeyPath = keyPath,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(90),
                AutoRenew = true
            };
            _db.SslCertificates.Add(sslCert);
        }
        else
        {
            sslCert.CertificatePath = certPath;
            sslCert.PrivateKeyPath = keyPath;
            sslCert.IssuedAt = DateTime.UtcNow;
            sslCert.ExpiresAt = DateTime.UtcNow.AddDays(90);
        }

        domain.SslEnabled = true;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("SSL certificate issued for {Domain}, expires {Expiry}", domain.Name, sslCert.ExpiresAt);
        return sslCert;
    }

    /// <summary>
    /// Issue a wildcard certificate via DNS-01 challenge using Cloudflare API.
    /// </summary>
    public async Task<SslCertificate> IssueWildcardCertificateAsync(int domainId, string cloudflareApiToken, CancellationToken cancellationToken)
    {
        var domain = await _db.Domains
            .Include(d => d.SslCertificate)
            .FirstOrDefaultAsync(d => d.Id == domainId, cancellationToken)
            ?? throw new InvalidOperationException($"Domain {domainId} not found");

        _logger.LogInformation("Issuing wildcard SSL for *.{Domain} via DNS-01", domain.Name);

        var acmeServer = _useStaging ? WellKnownServers.LetsEncryptStagingV2 : WellKnownServers.LetsEncryptV2;
        var accountEmail = _config["Ssl:AccountEmail"] ?? "admin@hostpanel.local";

        var accountKeyPath = Path.Combine(_certBasePath, "account.pem");
        AcmeContext acme;
        if (File.Exists(accountKeyPath))
        {
            acme = new AcmeContext(acmeServer, KeyFactory.FromPem(await File.ReadAllTextAsync(accountKeyPath, cancellationToken)));
            await acme.Account();
        }
        else
        {
            acme = new AcmeContext(acmeServer);
            await acme.NewAccount(accountEmail, termsOfServiceAgreed: true);
            Directory.CreateDirectory(_certBasePath);
            await File.WriteAllTextAsync(accountKeyPath, acme.AccountKey.ToPem(), cancellationToken);
        }

        // Order wildcard
        var order = await acme.NewOrder(new[] { $"*.{domain.Name}", domain.Name });

        foreach (var authz in await order.Authorizations())
        {
            var dnsChallenge = await authz.Dns();
            var txtValue = acme.AccountKey.DnsTxt(dnsChallenge.Token);
            var recordName = $"_acme-challenge.{domain.Name}";

            // Create DNS TXT record via Cloudflare
            using var cfHttp = new HttpClient();
            cfHttp.DefaultRequestHeaders.Add("Authorization", $"Bearer {cloudflareApiToken}");

            // Find zone ID
            var zonesResp = await cfHttp.GetAsync($"https://api.cloudflare.com/client/v4/zones?name={domain.Name}", cancellationToken);
            var zonesJson = await zonesResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
            var zoneId = zonesJson.GetProperty("result")[0].GetProperty("id").GetString();

            // Create TXT record
            var createResp = await cfHttp.PostAsJsonAsync(
                $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records",
                new { type = "TXT", name = recordName, content = txtValue, ttl = 120 },
                cancellationToken);
            var recordJson = await createResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
            var recordId = recordJson.GetProperty("result").GetProperty("id").GetString();

            // Wait for propagation
            await Task.Delay(15000, cancellationToken);

            // Validate
            await dnsChallenge.Validate();
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(5000, cancellationToken);
                var status = await authz.Resource();
                if (status.Status == Certes.Acme.Resource.AuthorizationStatus.Valid) break;
                if (status.Status == Certes.Acme.Resource.AuthorizationStatus.Invalid)
                    throw new InvalidOperationException("DNS-01 validation failed");
            }

            // Cleanup TXT record
            await cfHttp.DeleteAsync($"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records/{recordId}", cancellationToken);
        }

        var privateKey = KeyFactory.NewKey(Certes.KeyAlgorithm.RS256);
        var cert = await order.Generate(new CsrInfo { CommonName = $"*.{domain.Name}" }, privateKey);

        var domainCertDir = Path.Combine(_certBasePath, domain.Name);
        Directory.CreateDirectory(domainCertDir);
        var certPath = Path.Combine(domainCertDir, "fullchain.pem");
        var keyPath = Path.Combine(domainCertDir, "privkey.pem");
        await File.WriteAllTextAsync(certPath, cert.ToPem(), cancellationToken);
        await File.WriteAllTextAsync(keyPath, privateKey.ToPem(), cancellationToken);

        var sslCert = domain.SslCertificate ?? new SslCertificate { DomainId = domainId };
        sslCert.Provider = "LetsEncrypt-Wildcard";
        sslCert.CertificatePath = certPath;
        sslCert.PrivateKeyPath = keyPath;
        sslCert.IssuedAt = DateTime.UtcNow;
        sslCert.ExpiresAt = DateTime.UtcNow.AddDays(90);
        sslCert.AutoRenew = true;

        if (domain.SslCertificate is null) _db.SslCertificates.Add(sslCert);
        domain.SslEnabled = true;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Wildcard SSL issued for *.{Domain}", domain.Name);
        return sslCert;
    }

    public async Task RenewExpiringCertificatesAsync(CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow.AddDays(30);
        var expiring = await _db.SslCertificates
            .Include(c => c.Domain)
            .Where(c => c.AutoRenew && c.ExpiresAt != null && c.ExpiresAt < threshold)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Found {Count} certificates to renew", expiring.Count);

        foreach (var cert in expiring)
        {
            try
            {
                await IssueCertificateAsync(cert.DomainId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to renew certificate for domain {Domain}", cert.Domain.Name);
            }
        }
    }

    public string GenerateNginxSslBlock(string domain)
    {
        var certDir = Path.Combine(_certBasePath, domain);
        return $$"""
                listen 443 ssl;
                listen [::]:443 ssl;
                ssl_certificate {{certDir}}/fullchain.pem;
                ssl_certificate_key {{certDir}}/privkey.pem;
                ssl_protocols TLSv1.2 TLSv1.3;
                ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384;
                ssl_prefer_server_ciphers off;
            """;
    }
}
