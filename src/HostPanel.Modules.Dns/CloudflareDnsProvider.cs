using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;

namespace HostPanel.Modules.Dns;

/// <summary>
/// Cloudflare DNS provider. Uses Cloudflare API v4.
/// </summary>
public class CloudflareDnsProvider : IDnsProvider
{
    public string ProviderName => "Cloudflare";

    private readonly HttpClient _http;
    private readonly ILogger<CloudflareDnsProvider> _logger;

    public CloudflareDnsProvider(IConfiguration config, ILogger<CloudflareDnsProvider> logger)
    {
        _logger = logger;
        var apiToken = config["Dns:Cloudflare:ApiToken"] ?? "";

        _http = new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiToken}");
    }

    public async Task<DnsProviderZone> CreateZoneAsync(string domain, CancellationToken cancellationToken = default)
    {
        var payload = new { name = domain, jump_start = true };
        var response = await _http.PostAsJsonAsync("zones", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var zoneId = json.GetProperty("result").GetProperty("id").GetString()!;

        _logger.LogInformation("Created Cloudflare zone {Zone} for {Domain}", zoneId, domain);
        return new DnsProviderZone(zoneId, domain);
    }

    public async Task DeleteZoneAsync(string zoneId, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"zones/{zoneId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<DnsProviderRecord> AddRecordAsync(string zoneId, DnsRecordType type, string name, string value, int ttl = 3600, int? priority = null, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = type.ToString().ToUpper(),
            ["name"] = name,
            ["content"] = value,
            ["ttl"] = ttl,
            ["proxied"] = false
        };
        if (priority.HasValue) payload["priority"] = priority.Value;

        var response = await _http.PostAsJsonAsync($"zones/{zoneId}/dns_records", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var recordId = json.GetProperty("result").GetProperty("id").GetString()!;

        return new DnsProviderRecord(recordId, type, name, value, ttl, priority);
    }

    public async Task UpdateRecordAsync(string zoneId, string recordId, string value, int ttl = 3600, int? priority = null, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["content"] = value,
            ["ttl"] = ttl
        };
        if (priority.HasValue) payload["priority"] = priority.Value;

        var response = await _http.PatchAsJsonAsync($"zones/{zoneId}/dns_records/{recordId}", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteRecordAsync(string zoneId, string recordId, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"zones/{zoneId}/dns_records/{recordId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<DnsProviderRecord>> ListRecordsAsync(string zoneId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"zones/{zoneId}/dns_records?per_page=100", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var records = new List<DnsProviderRecord>();

        foreach (var r in json.GetProperty("result").EnumerateArray())
        {
            var typeStr = r.GetProperty("type").GetString() ?? "";
            if (!Enum.TryParse<DnsRecordType>(typeStr, true, out var type)) continue;

            records.Add(new DnsProviderRecord(
                r.GetProperty("id").GetString()!,
                type,
                r.GetProperty("name").GetString() ?? "",
                r.GetProperty("content").GetString() ?? "",
                r.GetProperty("ttl").GetInt32(),
                r.TryGetProperty("priority", out var p) ? p.GetInt32() : null
            ));
        }

        return records;
    }
}
