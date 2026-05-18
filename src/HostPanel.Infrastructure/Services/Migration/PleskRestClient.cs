using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HostPanel.Core.Models;

namespace HostPanel.Infrastructure.Services.Migration;

/// <summary>
/// Thin wrapper around Plesk REST API v2. Auth via `X-API-Key` header.
/// Hostinger Plesk Obsidian 18.0.77 verified working with this header.
/// </summary>
public class PleskRestClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public PleskRestClient(MigrationSource source, HttpClient? http = null)
    {
        if (http is null)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _ownsHttp = false;
        }

        _http.BaseAddress = new Uri($"https://{source.Host}:{source.PleskApiPort}");
        _http.DefaultRequestHeaders.Remove("X-API-Key");
        _http.DefaultRequestHeaders.Add("X-API-Key", source.PleskApiKey);
    }

    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(path, ct);
        resp.EnsureSuccessStatusCode();
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonNode.ParseAsync(stream, cancellationToken: ct);
    }

    public async Task<int> ProbeAsync(string path = "/api/v2/server", CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(path, ct);
        return (int)resp.StatusCode;
    }

    public Task<JsonNode?> GetServerAsync(CancellationToken ct = default) =>
        GetAsync("/api/v2/server", ct);

    public Task<JsonNode?> GetDomainsAsync(CancellationToken ct = default) =>
        GetAsync("/api/v2/domains", ct);

    public Task<JsonNode?> GetDomainAsync(int id, CancellationToken ct = default) =>
        GetAsync($"/api/v2/domains/{id}", ct);

    public Task<JsonNode?> GetClientsAsync(CancellationToken ct = default) =>
        GetAsync("/api/v2/clients", ct);

    /// <summary>
    /// Plesk's /api/v2/databases returns ALL databases. We fetch and filter by
    /// parent_domain client-side because the `?domain=` filter actually matches
    /// by domain name not id and 404s on non-string values.
    /// </summary>
    public async Task<JsonArray> GetDatabasesForDomainAsync(int domainId, CancellationToken ct = default)
    {
        var all = await GetAsync("/api/v2/databases", ct) as JsonArray ?? new JsonArray();
        var filtered = new JsonArray();
        foreach (var d in all.OfType<JsonObject>())
        {
            if (d["parent_domain"]?.GetValue<int>() == domainId)
                filtered.Add(d.DeepClone());
        }
        return filtered;
    }

    /// <summary>
    /// Plesk has no GET /mail listing endpoint — uses the CLI bridge POST.
    /// Returns mail names parsed from `plesk bin mail --list` stdout, filtered to the given domain.
    /// </summary>
    public async Task<JsonArray> GetMailAccountsForDomainAsync(string domain, CancellationToken ct = default)
    {
        var body = new JsonObject { ["params"] = new JsonArray("--list") };
        using var content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync("/api/v2/cli/mail/call", content, ct);
        resp.EnsureSuccessStatusCode();
        var node = await JsonNode.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct) as JsonObject;
        var stdout = node?["stdout"]?.GetValue<string>() ?? "";

        // `plesk bin mail --list` output is `Mail name\t<address1>\t<address2>...` on a single line
        // (per-instance behaviour) OR one address per line on newer Plesk builds. Handle both.
        var suffix = "@" + domain;
        var result = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in stdout.Split(new[] { '\n', '\t', ' ', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = line.Trim().TrimEnd(',', ';');
            if (string.IsNullOrEmpty(token) || !token.Contains('@')) continue;
            if (!token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(token)) continue;
            result.Add(new JsonObject
            {
                ["address"] = token,
                ["name"] = token[..^suffix.Length]
            });
        }
        return result;
    }

    public Task<JsonNode?> GetSubscriptionsAsync(CancellationToken ct = default) =>
        GetAsync("/api/v2/subscriptions", ct);

    public Task<JsonNode?> GetSubscriptionAsync(int id, CancellationToken ct = default) =>
        GetAsync($"/api/v2/subscriptions/{id}", ct);

    public Task<JsonNode?> GetIpAddressesAsync(CancellationToken ct = default) =>
        GetAsync("/api/v2/ip", ct);

    public Task<JsonNode?> GetSslCertificatesAsync(int domainId, CancellationToken ct = default) =>
        GetAsync($"/api/v2/cli/certificate/call?params=--list-certificates%20-domain%20{domainId}", ct);

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
