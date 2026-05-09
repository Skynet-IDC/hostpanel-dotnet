namespace HostPanel.Core.Models;

public class CronJob : BaseEntity
{
    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;
    public string Command { get; set; } = string.Empty;
    public string Schedule { get; set; } = string.Empty; // cron expression
    public bool Enabled { get; set; } = true;
    public string? Description { get; set; }
    public DateTime? LastRunAt { get; set; }
}
