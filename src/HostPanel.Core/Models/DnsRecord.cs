using HostPanel.Core.Enums;

namespace HostPanel.Core.Models;

public class DnsRecord : BaseEntity
{
    public int DnsZoneId { get; set; }
    public DnsZone DnsZone { get; set; } = null!;
    public DnsRecordType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int Ttl { get; set; } = 3600;
    public int? Priority { get; set; }
    public string? ProviderRecordId { get; set; }
}
