namespace HostPanel.Core.Models;

public class FtpAccount : BaseEntity
{
    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string HomeDirectory { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public long QuotaMb { get; set; }
}
