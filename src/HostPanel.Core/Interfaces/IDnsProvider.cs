using HostPanel.Core.Enums;

namespace HostPanel.Core.Interfaces;

public interface IDnsProvider
{
    string ProviderName { get; }
    Task<DnsProviderZone> CreateZoneAsync(string domain, CancellationToken cancellationToken = default);
    Task DeleteZoneAsync(string zoneId, CancellationToken cancellationToken = default);
    Task<DnsProviderRecord> AddRecordAsync(string zoneId, DnsRecordType type, string name, string value, int ttl = 3600, int? priority = null, CancellationToken cancellationToken = default);
    Task UpdateRecordAsync(string zoneId, string recordId, string value, int ttl = 3600, int? priority = null, CancellationToken cancellationToken = default);
    Task DeleteRecordAsync(string zoneId, string recordId, CancellationToken cancellationToken = default);
    Task<List<DnsProviderRecord>> ListRecordsAsync(string zoneId, CancellationToken cancellationToken = default);
}

public record DnsProviderZone(string ZoneId, string Domain);
public record DnsProviderRecord(string RecordId, DnsRecordType Type, string Name, string Value, int Ttl, int? Priority);
