using Microsoft.Extensions.DependencyInjection;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;

namespace HostPanel.Modules.Dns;

public class DnsProviderFactory
{
    private readonly IServiceProvider _services;

    public DnsProviderFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IDnsProvider GetProvider(DnsProviderType type) => type switch
    {
        DnsProviderType.PowerDns => (IDnsProvider)_services.GetRequiredService(typeof(PowerDnsProvider)),
        DnsProviderType.Cloudflare => (IDnsProvider)_services.GetRequiredService(typeof(CloudflareDnsProvider)),
        _ => throw new ArgumentException($"Unknown DNS provider: {type}")
    };
}
