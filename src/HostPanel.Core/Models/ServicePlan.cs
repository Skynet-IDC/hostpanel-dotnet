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
    // Legacy single-cycle price. Kept for back-compat and as the default when no per-cycle
    // override is set. New plans should also populate PriceMonthly/PriceQuarterly/etc.
    public decimal PricePerPeriod { get; set; }
    public BillingPeriod DefaultBillingPeriod { get; set; } = BillingPeriod.Monthly;

    // F1: per-cycle override pricing. Operators can offer discounts on longer commitments
    // ($100/mo, $270/quarter, $510/6mo, $960/year) instead of strict linear pricing.
    // Null on any field falls back to PricePerPeriod × cycle-months — so existing plans
    // keep working without admin intervention.
    public decimal? PriceMonthly { get; set; }
    public decimal? PriceQuarterly { get; set; }
    public decimal? PriceSemiAnnual { get; set; }
    public decimal? PriceAnnual { get; set; }

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

    /// <summary>
    /// Resolve the price for a given billing cycle. Returns the per-cycle override if set,
    /// otherwise falls back to linear math against PricePerPeriod (which is treated as
    /// the price for the plan's DefaultBillingPeriod).
    /// </summary>
    public decimal GetPriceFor(BillingPeriod period)
    {
        var overrideValue = period switch
        {
            BillingPeriod.Monthly => PriceMonthly,
            BillingPeriod.Quarterly => PriceQuarterly,
            BillingPeriod.SemiAnnual => PriceSemiAnnual,
            BillingPeriod.Annual => PriceAnnual,
            _ => null
        };
        if (overrideValue.HasValue) return overrideValue.Value;

        // Linear fallback: convert PricePerPeriod into a monthly rate against the plan's
        // DefaultBillingPeriod, then multiply by the requested period's month count.
        var defaultMonths = MonthsFor(DefaultBillingPeriod);
        var requestedMonths = MonthsFor(period);
        if (defaultMonths == 0) return PricePerPeriod;
        return PricePerPeriod / defaultMonths * requestedMonths;
    }

    public static int MonthsFor(BillingPeriod period) => period switch
    {
        BillingPeriod.Monthly => 1,
        BillingPeriod.Quarterly => 3,
        BillingPeriod.SemiAnnual => 6,
        BillingPeriod.Annual => 12,
        _ => 1,
    };
}
