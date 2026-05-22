namespace HostPanel.Core.Models;

public class Domain : BaseEntity
{
    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string DocumentRoot { get; set; } = string.Empty;
    public bool SslEnabled { get; set; }
    public string PhpVersion { get; set; } = "8.3";

    /// <summary>JSON-serialized list of enabled PHP extension package suffixes
    /// (e.g. ["mbstring","gd","intl","curl","opcache","redis","imagick","xdebug"]).
    /// Empty list = use the distro default extension set for the PhpVersion.</summary>
    public string PhpExtensionsJson { get; set; } = "[]";

    /// <summary>JSON-serialized dict of php.ini directive overrides written
    /// to the FPM pool's php_admin_value lines. Common keys: memory_limit,
    /// upload_max_filesize, post_max_size, max_execution_time, display_errors,
    /// opcache.enable, opcache.memory_consumption.</summary>
    public string PhpIniJson { get; set; } = "{}";

    public bool IsMainDomain { get; set; }

    public ICollection<DnsZone> DnsZones { get; set; } = [];
    public ICollection<Mailbox> Mailboxes { get; set; } = [];
    public ICollection<MailAlias> MailAliases { get; set; } = [];
    public SslCertificate? SslCertificate { get; set; }
}
