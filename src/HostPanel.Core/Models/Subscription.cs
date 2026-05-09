using HostPanel.Core.Enums;

namespace HostPanel.Core.Models;

public class Subscription : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;
    public int ServicePlanId { get; set; }
    public ServicePlan ServicePlan { get; set; } = null!;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.PendingSetup;
    public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;
    public int BillingDay { get; set; } = 1;
    public DateTime? NextInvoiceDate { get; set; }
    public string? SystemUsername { get; set; }
    public PaymentProvider PreferredPaymentProvider { get; set; } = PaymentProvider.PayPal;

    public ICollection<Domain> Domains { get; set; } = [];
    public ICollection<HostedDatabase> Databases { get; set; } = [];
    public ICollection<Backup> Backups { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
}
