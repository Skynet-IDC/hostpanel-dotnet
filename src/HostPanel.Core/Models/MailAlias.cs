namespace HostPanel.Core.Models;

public class MailAlias : BaseEntity
{
    public int DomainId { get; set; }
    public Domain Domain { get; set; } = null!;
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
}
