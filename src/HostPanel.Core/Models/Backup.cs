namespace HostPanel.Core.Models;

public class Backup : BaseEntity
{
    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;
    public string Type { get; set; } = "full";
    public string? FilePath { get; set; }
    public long SizeBytes { get; set; }
    public bool IsCompleted { get; set; }
}
