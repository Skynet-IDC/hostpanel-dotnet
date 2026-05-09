namespace HostPanel.Core.Models;

public class Mailbox : BaseEntity
{
    public int DomainId { get; set; }
    public Domain Domain { get; set; } = null!;
    public string Address { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public long QuotaMb { get; set; } = 500;
    public bool IsActive { get; set; } = true;
}
