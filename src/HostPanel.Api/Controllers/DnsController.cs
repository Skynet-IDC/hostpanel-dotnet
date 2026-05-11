using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Modules.Dns;
using HostPanel.Api.Middleware;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public class DnsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DnsProviderFactory _dnsFactory;
    private readonly TenantAuthorizationService _auth;

    public DnsController(AppDbContext db, DnsProviderFactory dnsFactory, TenantAuthorizationService auth)
    {
        _db = db;
        _dnsFactory = dnsFactory;
        _auth = auth;
    }

    [HttpGet("domains/{domainId}/dns")]
    public async Task<IActionResult> ListRecords(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var records = await _db.DnsRecords
            .Where(r => r.DnsZone.DomainId == domainId)
            .Select(r => new
            {
                r.Id, r.Type, r.Name, r.Value, r.Ttl, r.Priority,
                r.ProviderRecordId, r.DnsZoneId
            })
            .ToListAsync();

        return Ok(records);
    }

    [HttpPost("domains/{domainId}/dns")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> AddRecord(int domainId, [FromBody] AddDnsRecordRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var zone = await _db.DnsZones.FirstOrDefaultAsync(z => z.DomainId == domainId);
        if (zone is null)
        {
            // Auto-create zone for this domain
            var domain = await _db.Domains.FindAsync(domainId);
            if (domain is null) return NotFound();

            var provider = _dnsFactory.GetProvider(request.Provider ?? DnsProviderType.PowerDns);
            var providerZone = await provider.CreateZoneAsync(domain.Name);

            zone = new DnsZone
            {
                DomainId = domainId,
                Name = domain.Name,
                Provider = request.Provider ?? DnsProviderType.PowerDns,
                ProviderZoneId = providerZone.ZoneId
            };
            _db.DnsZones.Add(zone);
            await _db.SaveChangesAsync();
        }

        // Add to provider
        var dnsProvider = _dnsFactory.GetProvider(zone.Provider);
        var providerRecord = await dnsProvider.AddRecordAsync(
            zone.ProviderZoneId ?? zone.Name,
            request.Type, request.Name, request.Value,
            request.Ttl, request.Priority);

        // Save to DB
        var record = new DnsRecord
        {
            DnsZoneId = zone.Id,
            Type = request.Type,
            Name = request.Name,
            Value = request.Value,
            Ttl = request.Ttl,
            Priority = request.Priority,
            ProviderRecordId = providerRecord.RecordId
        };
        _db.DnsRecords.Add(record);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(ListRecords), new { domainId }, new
        {
            record.Id, record.Type, record.Name, record.Value, record.Ttl, record.Priority
        });
    }

    [HttpPut("dns/{recordId}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> UpdateRecord(int recordId, [FromBody] UpdateDnsRecordRequest request)
    {
        var record = await _db.DnsRecords
            .Include(r => r.DnsZone)
            .FirstOrDefaultAsync(r => r.Id == recordId);
        if (record is null) return NotFound();

        if (!await _auth.CanAccessDomainAsync(User, record.DnsZone.DomainId)) return Forbid();

        // Update in provider
        var provider = _dnsFactory.GetProvider(record.DnsZone.Provider);
        await provider.UpdateRecordAsync(
            record.DnsZone.ProviderZoneId ?? record.DnsZone.Name,
            record.ProviderRecordId ?? "",
            request.Value, request.Ttl, request.Priority);

        record.Value = request.Value;
        record.Ttl = request.Ttl;
        record.Priority = request.Priority;
        await _db.SaveChangesAsync();

        return Ok(new { record.Id, record.Type, record.Name, record.Value, record.Ttl, record.Priority });
    }

    [HttpDelete("dns/{recordId}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> DeleteRecord(int recordId)
    {
        var record = await _db.DnsRecords
            .Include(r => r.DnsZone)
            .FirstOrDefaultAsync(r => r.Id == recordId);
        if (record is null) return NotFound();

        if (!await _auth.CanAccessDomainAsync(User, record.DnsZone.DomainId)) return Forbid();

        // Delete from provider
        var provider = _dnsFactory.GetProvider(record.DnsZone.Provider);
        try
        {
            await provider.DeleteRecordAsync(
                record.DnsZone.ProviderZoneId ?? record.DnsZone.Name,
                record.ProviderRecordId ?? "");
        }
        catch (Exception) { /* Best effort - record may not exist in provider */ }

        _db.DnsRecords.Remove(record);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record AddDnsRecordRequest(
    DnsRecordType Type,
    string Name,
    string Value,
    int Ttl = 3600,
    int? Priority = null,
    DnsProviderType? Provider = null
);

public record UpdateDnsRecordRequest(string Value, int Ttl = 3600, int? Priority = null);
