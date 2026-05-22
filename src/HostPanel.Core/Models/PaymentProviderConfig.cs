namespace HostPanel.Core.Models;

/// <summary>
/// Per-provider payment credentials, stored in the DB so they can be updated via
/// the Settings UI (no more "edit appsettings + restart" workflow). The Secret
/// column is wrapped with IDataProtector before persistence so a DB dump
/// (e.g. an off-box backup landing in a third-party bucket) doesn't leak it.
///
/// Keyed by (Provider, Sandbox) — operators commonly keep both a sandbox and a
/// live app configured so they can flip the runtime switch without re-entering
/// credentials.
/// </summary>
public class PaymentProviderConfig : BaseEntity
{
    public string Provider { get; set; } = string.Empty;     // "PayPal" | "Stripe" | future
    public bool Sandbox { get; set; }
    public string ClientId { get; set; } = string.Empty;
    /// <summary>IDataProtector-encrypted client secret. Never read directly.</summary>
    public string ProtectedSecret { get; set; } = string.Empty;
    public string? WebhookId { get; set; }
    /// <summary>True if this row should be used at runtime. Only one per Provider can be Enabled.</summary>
    public bool Enabled { get; set; } = true;
    public DateTime? LastValidatedAt { get; set; }
    public string? LastValidationMessage { get; set; }
}
