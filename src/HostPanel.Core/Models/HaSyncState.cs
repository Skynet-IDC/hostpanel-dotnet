namespace HostPanel.Core.Models;

public class HaSyncState : BaseEntity
{
    public int ServerId { get; set; }
    public Server Server { get; set; } = null!;
    public string Role { get; set; } = "standby";          // primary, standby, recovering, promoting
    public DateTime? LastSyncAt { get; set; }
    public string SyncDirection { get; set; } = "pull";     // pull (standby pulls from primary), push (primary pushes)
    public bool AutoFailback { get; set; } = true;          // Auto-promote original primary when it recovers
    public int HealthCheckIntervalSeconds { get; set; } = 30;
    public int FailoverAfterMissedChecks { get; set; } = 3; // Trigger failover after N consecutive failed checks
    public int ConsecutiveFailures { get; set; }
    public DateTime? FailoverTriggeredAt { get; set; }
    public DateTime? FailbackCompletedAt { get; set; }
    public string? LastSyncLog { get; set; }
    public bool SyncFiles { get; set; } = true;
    public bool SyncDatabases { get; set; } = true;
    public bool SyncEmail { get; set; } = true;
    public string? SyncExcludes { get; set; }               // Comma-separated paths to exclude from rsync

    // Failover strategy
    public Core.Enums.FailoverStrategy FailoverStrategy { get; set; } = Core.Enums.FailoverStrategy.DnsSwap;

    // DNS strategy config
    public string? DnsProvider { get; set; }                // "powerdns" or "cloudflare"
    public int DnsTtlSeconds { get; set; } = 60;

    // Floating IP strategy config
    public string? FloatingIp { get; set; }                 // The shared virtual IP
    public string? FloatingIpInterface { get; set; }        // e.g., "eth0"

    // Cloudflare strategy config
    public string? CloudflareApiToken { get; set; }
    public string? CloudflareZoneId { get; set; }
    public bool CloudflareUseLoadBalancer { get; set; }     // true = $5/mo LB, false = free A-record swap
    public string? CloudflareLoadBalancerId { get; set; }

    // Per-domain strategy config
    public string? FailoverDomains { get; set; }            // JSON array of domain IDs to failover (null = all)
}
