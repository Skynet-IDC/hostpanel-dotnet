namespace HostPanel.Core.Enums;

public static class WebhookEvents
{
    // Subscriptions
    public const string SubscriptionCreated = "subscription.created";
    public const string SubscriptionActivated = "subscription.activated";
    public const string SubscriptionSuspended = "subscription.suspended";
    public const string SubscriptionCancelled = "subscription.cancelled";
    public const string SubscriptionDeleted = "subscription.deleted";

    // Domains
    public const string DomainCreated = "domain.created";
    public const string DomainDeleted = "domain.deleted";
    public const string DomainSslIssued = "domain.ssl.issued";
    public const string DomainSslExpiring = "domain.ssl.expiring";

    // Email
    public const string MailboxCreated = "mailbox.created";
    public const string MailboxDeleted = "mailbox.deleted";

    // Databases
    public const string DatabaseCreated = "database.created";
    public const string DatabaseDeleted = "database.deleted";

    // Billing
    public const string InvoiceCreated = "invoice.created";
    public const string InvoicePaid = "invoice.paid";
    public const string InvoiceOverdue = "invoice.overdue";
    public const string PaymentReceived = "payment.received";

    // Customers
    public const string CustomerCreated = "customer.created";
    public const string CustomerDeactivated = "customer.deactivated";

    // Server
    public const string ServerHealthDown = "server.health.down";
    public const string ServerDiskWarning = "server.disk.warning";
    public const string BackupCompleted = "backup.completed";
    public const string BackupFailed = "backup.failed";

    public static readonly string[] All =
    [
        SubscriptionCreated, SubscriptionActivated, SubscriptionSuspended,
        SubscriptionCancelled, SubscriptionDeleted,
        DomainCreated, DomainDeleted, DomainSslIssued, DomainSslExpiring,
        MailboxCreated, MailboxDeleted,
        DatabaseCreated, DatabaseDeleted,
        InvoiceCreated, InvoicePaid, InvoiceOverdue, PaymentReceived,
        CustomerCreated, CustomerDeactivated,
        ServerHealthDown, ServerDiskWarning, BackupCompleted, BackupFailed
    ];
}
