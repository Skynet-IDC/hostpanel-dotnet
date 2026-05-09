namespace HostPanel.Core.Models;

public class DnsZone : BaseEntity
{
    public int DomainId { get; set; }
    public Domain Domain { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public Core.Enums.DnsProviderType Provider { get; set; } = Core.Enums.DnsProviderType.PowerDns;
    public string? ProviderZoneId { get; set; }

    public ICollection<DnsRecord> Records { get; set; } = [];
}
