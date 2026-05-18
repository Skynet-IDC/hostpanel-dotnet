using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;

namespace HostPanel.Modules.Dns;

/// <summary>
/// PowerDNS API provider. Communicates with PowerDNS Authoritative Server REST API.
/// </summary>
public class PowerDnsProvider : IDnsProvider
{
    public string ProviderName => "PowerDNS";

    private readonly HttpClient _http;
    private readonly string _serverId;
    private readonly ILogger<PowerDnsProvider> _logger;

    public PowerDnsProvider(IConfiguration config, ILogger<PowerDnsProvider> logger)
    {
        _logger = logger;
        _serverId = config["Dns:PowerDns:ServerId"] ?? "localhost";
        var apiUrl = config["Dns:PowerDns:ApiUrl"] ?? "http://127.0.0.1:8081";
        var apiKey = config["Dns:PowerDns:ApiKey"] ?? "";

        _http = new HttpClient { BaseAddress = new Uri(apiUrl) };
        _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    public async Task<DnsProviderZone> CreateZoneAsync(string domain, CancellationToken cancellationToken = default)
    {
        var zoneName = domain.EndsWith('.') ? domain : $"{domain}.";
        var payload = new
        {
            name = zoneName,
            kind = "Native",
            nameservers = new[] { $"ns1.{domain}.", $"ns2.{domain}." }
        };

        var response = await _http.PostAsJsonAsync($"/api/v1/servers/{_serverId}/zones", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Created PowerDNS zone for {Domain}", domain);
        return new DnsProviderZone(zoneName, domain);
    }

    public async Task DeleteZoneAsync(string zoneId, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/servers/{_serverId}/zones/{zoneId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Deleted PowerDNS zone {Zone}", zoneId);
    }

    public async Task<DnsProviderRecord> AddRecordAsync(string zoneId, DnsRecordType type, string name, string value, int ttl = 3600, int? priority = null, CancellationToken cancellationToken = default)
    {
        // PowerDNS requires fully qualified names: "sub.zone.tld."
        // If name is "@" or already FQDN, handle accordingly.
        string recordName;
        if (name == "@")
            recordName = zoneId.EndsWith('.') ? zoneId : $"{zoneId}.";
        else if (name.EndsWith('.'))
            recordName = name;
        else
            recordName = $"{name}.{(zoneId.EndsWith('.') ? zoneId : zoneId + ".")}";
        // PowerDNS requires MX content as "priority target", TXT as quoted strings
        var recordValue = type switch
        {
            DnsRecordType.MX => $"{priority ?? 10} {value}",
            DnsRecordType.TXT => value.StartsWith('"') ? value : $"\"{value}\"",
            _ => value
        };

        var payload = new
        {
            rrsets = new[]
            {
                new
                {
                    name = recordName,
                    type = type.ToString().ToUpper(),
                    ttl,
                    changetype = "REPLACE",
                    records = new[] { new { content = recordValue, disabled = false } }
                }
            }
        };

        var response = await _http.PatchAsJsonAsync($"/api/v1/servers/{_serverId}/zones/{zoneId}", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var recordId = $"{recordName}:{type}";
        return new DnsProviderRecord(recordId, type, name, value, ttl, priority);
    }

    public async Task UpdateRecordAsync(string zoneId, string recordId, string value, int ttl = 3600, int? priority = null, CancellationToken cancellationToken = default)
    {
        var parts = recordId.Split(':');
        var name = parts[0];
        var type = Enum.Parse<DnsRecordType>(parts[1], ignoreCase: true);
        await AddRecordAsync(zoneId, type, name, value, ttl, priority, cancellationToken);
    }

    public async Task DeleteRecordAsync(string zoneId, string recordId, CancellationToken cancellationToken = default)
    {
        var parts = recordId.Split(':');
        var payload = new
        {
            rrsets = new[]
            {
                new
                {
                    name = parts[0],
                    type = parts[1].ToUpper(),
                    changetype = "DELETE"
                }
            }
        };

        var response = await _http.PatchAsJsonAsync($"/api/v1/servers/{_serverId}/zones/{zoneId}", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<DnsProviderRecord>> ListRecordsAsync(string zoneId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"/api/v1/servers/{_serverId}/zones/{zoneId}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var records = new List<DnsProviderRecord>();

        if (json.TryGetProperty("rrsets", out var rrsets))
        {
            foreach (var rrset in rrsets.EnumerateArray())
            {
                var name = rrset.GetProperty("name").GetString() ?? "";
                var typeStr = rrset.GetProperty("type").GetString() ?? "";
                var ttl = rrset.GetProperty("ttl").GetInt32();

                if (!Enum.TryParse<DnsRecordType>(typeStr, true, out var type)) continue;

                foreach (var record in rrset.GetProperty("records").EnumerateArray())
                {
                    var content = record.GetProperty("content").GetString() ?? "";
                    records.Add(new DnsProviderRecord($"{name}:{typeStr}", type, name, content, ttl, null));
                }
            }
        }

        return records;
    }
}
