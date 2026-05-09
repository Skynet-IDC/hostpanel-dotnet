using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Infrastructure.Services;

public interface IFailoverStrategy
{
    FailoverStrategy Strategy { get; }
    Task ExecuteFailoverAsync(Server standby, Server primary, HaSyncState state, CancellationToken ct);
    Task ExecuteFailbackAsync(Server recovered, Server currentPrimary, HaSyncState state, CancellationToken ct);
}

/// <summary>Strategy 1: Swap DNS A records to point domains to standby IP.</summary>
public class DnsSwapStrategy : IFailoverStrategy
{
    public FailoverStrategy Strategy => FailoverStrategy.DnsSwap;

    private readonly AppDbContext _db;
    private readonly IServiceProvider _services;
    private readonly ILogger<DnsSwapStrategy> _logger;

    public DnsSwapStrategy(AppDbContext db, IServiceProvider services, ILogger<DnsSwapStrategy> logger)
    {
        _db = db;
        _services = services;
        _logger = logger;
    }

    private IDnsProvider GetDnsProvider(DnsProviderType type) =>
        _services.GetServices<IDnsProvider>().FirstOrDefault(p => p.ProviderName == type.ToString())
        ?? throw new InvalidOperationException($"No DNS provider for {type}");

    public async Task ExecuteFailoverAsync(Server standby, Server primary, HaSyncState state, CancellationToken ct)
    {
        _logger.LogInformation("DNS failover: pointing all domains to {StandbyIp}", standby.IpAddress);

        var zones = await _db.DnsZones.Include(z => z.Records).Include(z => z.Domain).ToListAsync(ct);

        foreach (var zone in zones)
        {
            var provider = GetDnsProvider(zone.Provider);
            var aRecords = zone.Records.Where(r => r.Type == DnsRecordType.A).ToList();

            foreach (var record in aRecords)
            {
                if (record.Value == primary.IpAddress)
                {
                    await provider.UpdateRecordAsync(zone.ProviderZoneId ?? zone.Name,
                        record.ProviderRecordId ?? "", standby.IpAddress, state.DnsTtlSeconds, cancellationToken: ct);
                    record.Value = standby.IpAddress;
                    _logger.LogInformation("DNS: {Domain} A record -> {Ip}", zone.Domain.Name, standby.IpAddress);
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ExecuteFailbackAsync(Server recovered, Server currentPrimary, HaSyncState state, CancellationToken ct)
    {
        _logger.LogInformation("DNS failback: pointing all domains back to {RecoveredIp}", recovered.IpAddress);

        var zones = await _db.DnsZones.Include(z => z.Records).Include(z => z.Domain).ToListAsync(ct);

        foreach (var zone in zones)
        {
            var provider = GetDnsProvider(zone.Provider);
            var aRecords = zone.Records.Where(r => r.Type == DnsRecordType.A).ToList();

            foreach (var record in aRecords)
            {
                if (record.Value == currentPrimary.IpAddress)
                {
                    await provider.UpdateRecordAsync(zone.ProviderZoneId ?? zone.Name,
                        record.ProviderRecordId ?? "", recovered.IpAddress, state.DnsTtlSeconds, cancellationToken: ct);
                    record.Value = recovered.IpAddress;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>Strategy 2: Floating/virtual IP. Standby claims the VIP on failover.</summary>
public class FloatingIpStrategy : IFailoverStrategy
{
    public FailoverStrategy Strategy => FailoverStrategy.FloatingIp;

    private readonly ServerAgentFactory _agentFactory;
    private readonly ILogger<FloatingIpStrategy> _logger;

    public FloatingIpStrategy(ServerAgentFactory agentFactory, ILogger<FloatingIpStrategy> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    public async Task ExecuteFailoverAsync(Server standby, Server primary, HaSyncState state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(state.FloatingIp))
            throw new InvalidOperationException("Floating IP not configured");

        var iface = state.FloatingIpInterface ?? "eth0";

        _logger.LogInformation("Floating IP failover: {Ip} moving to {Standby}", state.FloatingIp, standby.Hostname);

        // Remove VIP from primary (best effort - it may be down)
        try
        {
            var primaryAgent = _agentFactory.GetAgent(primary);
            await primaryAgent.ExecuteAsync(new AgentRequest
            {
                Method = "ha.removeFloatingIp",
                Params = new Dictionary<string, object?> { ["ip"] = state.FloatingIp, ["interface"] = iface }
            }, ct);
        }
        catch { _logger.LogWarning("Could not remove floating IP from primary (may be down)"); }

        // Add VIP to standby
        var standbyAgent = _agentFactory.GetAgent(standby);
        await standbyAgent.ExecuteAsync(new AgentRequest
        {
            Method = "ha.addFloatingIp",
            Params = new Dictionary<string, object?> { ["ip"] = state.FloatingIp, ["interface"] = iface }
        }, ct);

        // Send gratuitous ARP to update network caches
        await standbyAgent.ExecuteAsync(new AgentRequest
        {
            Method = "ha.sendGratuitousArp",
            Params = new Dictionary<string, object?> { ["ip"] = state.FloatingIp, ["interface"] = iface }
        }, ct);
    }

    public async Task ExecuteFailbackAsync(Server recovered, Server currentPrimary, HaSyncState state, CancellationToken ct)
    {
        // Same as failover but reversed
        await ExecuteFailoverAsync(recovered, currentPrimary, state, ct);
    }
}

/// <summary>Strategy 3: Cloudflare API. Swap origin via A record or use Load Balancer.</summary>
public class CloudflareFailoverStrategy : IFailoverStrategy
{
    public FailoverStrategy Strategy => FailoverStrategy.Cloudflare;

    private readonly AppDbContext _db;
    private readonly ILogger<CloudflareFailoverStrategy> _logger;

    public CloudflareFailoverStrategy(AppDbContext db, ILogger<CloudflareFailoverStrategy> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteFailoverAsync(Server standby, Server primary, HaSyncState state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(state.CloudflareApiToken) || string.IsNullOrEmpty(state.CloudflareZoneId))
            throw new InvalidOperationException("Cloudflare API token and zone ID required");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {state.CloudflareApiToken}");
        var baseUrl = $"https://api.cloudflare.com/client/v4/zones/{state.CloudflareZoneId}";

        if (state.CloudflareUseLoadBalancer && !string.IsNullOrEmpty(state.CloudflareLoadBalancerId))
        {
            _logger.LogInformation("Cloudflare LB failover: disabling primary origin pool");

            // Disable the primary origin in the load balancer
            // This triggers Cloudflare's built-in health-check failover
            await http.PatchAsJsonAsync(
                $"{baseUrl}/load_balancers/{state.CloudflareLoadBalancerId}",
                new { fallback_pool = state.CloudflareLoadBalancerId }, ct);
        }
        else
        {
            _logger.LogInformation("Cloudflare DNS failover: swapping A records to {StandbyIp}", standby.IpAddress);

            // Free tier: swap all A records from primary IP to standby IP
            var resp = await http.GetAsync($"{baseUrl}/dns_records?type=A&per_page=100", ct);
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);

            foreach (var record in json.GetProperty("result").EnumerateArray())
            {
                var content = record.GetProperty("content").GetString();
                if (content == primary.IpAddress)
                {
                    var recordId = record.GetProperty("id").GetString();
                    var name = record.GetProperty("name").GetString();
                    var proxied = record.TryGetProperty("proxied", out var p) && p.GetBoolean();

                    await http.PatchAsJsonAsync($"{baseUrl}/dns_records/{recordId}",
                        new { content = standby.IpAddress, ttl = state.DnsTtlSeconds, proxied }, ct);

                    _logger.LogInformation("Cloudflare: {Name} -> {Ip} (proxied={Proxied})",
                        name, standby.IpAddress, proxied);
                }
            }
        }
    }

    public async Task ExecuteFailbackAsync(Server recovered, Server currentPrimary, HaSyncState state, CancellationToken ct)
    {
        // Same logic, just swap directions
        await ExecuteFailoverAsync(recovered, currentPrimary, state, ct);
    }
}

/// <summary>Strategy 4: Per-domain failover. Only move specific domains, not the whole server.</summary>
public class PerDomainFailoverStrategy : IFailoverStrategy
{
    public FailoverStrategy Strategy => FailoverStrategy.PerDomain;

    private readonly AppDbContext _db;
    private readonly IServiceProvider _services;
    private readonly ILogger<PerDomainFailoverStrategy> _logger;

    public PerDomainFailoverStrategy(AppDbContext db, IServiceProvider services, ILogger<PerDomainFailoverStrategy> logger)
    {
        _db = db;
        _services = services;
        _logger = logger;
    }

    private IDnsProvider GetDnsProvider(DnsProviderType type) =>
        _services.GetServices<IDnsProvider>().FirstOrDefault(p => p.ProviderName == type.ToString())
        ?? throw new InvalidOperationException($"No DNS provider for {type}");

    public async Task ExecuteFailoverAsync(Server standby, Server primary, HaSyncState state, CancellationToken ct)
    {
        var domainIds = GetDomainIds(state);

        _logger.LogInformation("Per-domain failover: moving {Count} domains to {Standby}",
            domainIds?.Count ?? 0, standby.Hostname);

        var zones = await _db.DnsZones
            .Include(z => z.Records).Include(z => z.Domain)
            .Where(z => domainIds == null || domainIds.Contains(z.DomainId))
            .ToListAsync(ct);

        foreach (var zone in zones)
        {
            var provider = GetDnsProvider(zone.Provider);
            foreach (var record in zone.Records.Where(r => r.Type == DnsRecordType.A && r.Value == primary.IpAddress))
            {
                await provider.UpdateRecordAsync(zone.ProviderZoneId ?? zone.Name,
                    record.ProviderRecordId ?? "", standby.IpAddress, state.DnsTtlSeconds, cancellationToken: ct);
                record.Value = standby.IpAddress;
                _logger.LogInformation("Per-domain: {Domain} -> {Ip}", zone.Domain.Name, standby.IpAddress);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ExecuteFailbackAsync(Server recovered, Server currentPrimary, HaSyncState state, CancellationToken ct)
    {
        await ExecuteFailoverAsync(recovered, currentPrimary, state, ct);
    }

    private static List<int>? GetDomainIds(HaSyncState state)
    {
        if (string.IsNullOrEmpty(state.FailoverDomains)) return null;
        try { return JsonSerializer.Deserialize<List<int>>(state.FailoverDomains); }
        catch { return null; }
    }
}

/// <summary>Factory to get the right strategy based on configuration.</summary>
public class FailoverStrategyFactory
{
    private readonly Dictionary<FailoverStrategy, IFailoverStrategy> _strategies;

    public FailoverStrategyFactory(IEnumerable<IFailoverStrategy> strategies)
    {
        _strategies = strategies.GroupBy(s => s.Strategy).ToDictionary(g => g.Key, g => g.Last());
    }

    public IFailoverStrategy Get(FailoverStrategy strategy)
    {
        if (!_strategies.TryGetValue(strategy, out var impl))
            throw new ArgumentException($"No failover strategy registered for: {strategy}");
        return impl;
    }
}
