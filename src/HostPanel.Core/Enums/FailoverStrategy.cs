namespace HostPanel.Core.Enums;

public enum FailoverStrategy
{
    /// <summary>Update DNS A records to point to standby IP. Free, but depends on TTL propagation.</summary>
    DnsSwap = 0,

    /// <summary>Floating/virtual IP shared between servers. Standby claims IP on failover. Instant, same LAN only.</summary>
    FloatingIp = 1,

    /// <summary>Cloudflare API: swap origin or use Load Balancing. Works with free plan (A record swap) or $5/mo (LB with health checks).</summary>
    Cloudflare = 2,

    /// <summary>Per-domain failover. Each domain can be individually failed over to a different server.</summary>
    PerDomain = 3
}
