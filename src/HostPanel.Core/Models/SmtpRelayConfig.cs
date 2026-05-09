namespace HostPanel.Core.Models;

public class SmtpRelayConfig : BaseEntity
{
    public string? RelayHost { get; set; }          // e.g., "smtp.sendgrid.net", "email-smtp.us-east-1.amazonaws.com"
    public int RelayPort { get; set; } = 587;
    public string? RelayUsername { get; set; }
    public string? RelayPassword { get; set; }
    public bool UseTls { get; set; } = true;
    public bool IsGlobal { get; set; } = true;      // Applies to all domains if true
    public int? DomainId { get; set; }               // Per-domain relay override
    public Domain? Domain { get; set; }
}
