namespace HostPanel.Core.Models;

public class Domain : BaseEntity
{
    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string DocumentRoot { get; set; } = string.Empty;
    public bool SslEnabled { get; set; }
    public string PhpVersion { get; set; } = "8.3";
    public bool IsMainDomain { get; set; }

    public ICollection<DnsZone> DnsZones { get; set; } = [];
    public ICollection<Mailbox> Mailboxes { get; set; } = [];
    public ICollection<MailAlias> MailAliases { get; set; } = [];
    public SslCertificate? SslCertificate { get; set; }
}
