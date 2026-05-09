using HostPanel.Core.Enums;

namespace HostPanel.Core.Models;

public class ServicePlan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long DiskQuotaMb { get; set; }
    public long BandwidthLimitMb { get; set; }
    public int MaxDomains { get; set; }
    public int MaxEmailAccounts { get; set; }
    public int MaxDatabases { get; set; }
    public int MaxFtpAccounts { get; set; }
    public decimal PricePerPeriod { get; set; }
    public BillingPeriod DefaultBillingPeriod { get; set; } = BillingPeriod.Monthly;
    public bool IsActive { get; set; } = true;

    // Non-payment policy
    public int GracePeriodDays { get; set; } = 7;
    public bool AutoSuspend { get; set; } = true;
    public string ReminderSchedule { get; set; } = "1,3,5";
    public int DeletionAfterDays { get; set; } = 30;

    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
