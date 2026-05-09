using HostPanel.Core.Enums;

namespace HostPanel.Core.Models;

public class Invoice : BaseEntity
{
    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;
    public PaymentProvider Provider { get; set; }
    public string? ProviderInvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime DueDate { get; set; }
    public DateTime? PaidDate { get; set; }
}
