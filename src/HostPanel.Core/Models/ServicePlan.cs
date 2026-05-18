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

    /// <summary>
    /// True for one-off plans attached to a single customer (e.g. a bespoke quote).
    /// Hidden from the public catalog (`/plans` endpoint filters them out).
    /// When true, CustomerUserId is the owning user; deleting that user cascades the plan.
    /// </summary>
    public bool IsCustom { get; set; }

    /// <summary>Owner of an IsCustom plan. Null for standard catalog plans.</summary>
    public string? CustomerUserId { get; set; }
    public AppUser? CustomerUser { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
