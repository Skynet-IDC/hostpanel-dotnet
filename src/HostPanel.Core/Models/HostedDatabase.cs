using HostPanel.Core.Enums;

namespace HostPanel.Core.Models;

public class HostedDatabase : BaseEntity
{
    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public DatabaseType Type { get; set; }
    public string DbUsername { get; set; } = string.Empty;
    public string DbPasswordHash { get; set; } = string.Empty;
}
