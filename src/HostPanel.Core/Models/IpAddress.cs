namespace HostPanel.Core.Models;

public class ServerIpAddress : BaseEntity
{
    public string Address { get; set; } = string.Empty;
    public bool IsIpv6 { get; set; }
    public bool IsShared { get; set; } = true;
    public int? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }
    public string? Note { get; set; }
}

public class IpBlacklistEntry : BaseEntity
{
    public string Address { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
