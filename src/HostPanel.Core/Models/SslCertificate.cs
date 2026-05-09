namespace HostPanel.Core.Models;

public class SslCertificate : BaseEntity
{
    public int DomainId { get; set; }
    public Domain Domain { get; set; } = null!;
    public string Provider { get; set; } = "LetsEncrypt";
    public string? CertificatePath { get; set; }
    public string? PrivateKeyPath { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool AutoRenew { get; set; } = true;
}
