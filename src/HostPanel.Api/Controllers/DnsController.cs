using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
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
            string zoneId;
            try
            {
                var providerZone = await provider.CreateZoneAsync(domain.Name);
                zoneId = providerZone.ZoneId;
            }
            catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("409"))
            {
                // Zone already exists in the provider — use the domain name as zone ID
                zoneId = domain.Name;
            }

            zone = new DnsZone
            {
                DomainId = domainId,
                Name = domain.Name,
                Provider = request.Provider ?? DnsProviderType.PowerDns,
                ProviderZoneId = zoneId
            };
            _db.DnsZones.Add(zone);
            await _db.SaveChangesAsync();
        }

        // Add to provider. Bug #46: external Cloudflare zones (or any zone the panel's
        // PowerDNS doesn't actually authority for) surface as 404/500 from the provider —
        // translate into a clear 422 with remediation hints rather than leaking 500s.
        var dnsProvider = _dnsFactory.GetProvider(zone.Provider);
        DnsProviderRecord providerRecord;
        try
        {
            providerRecord = await dnsProvider.AddRecordAsync(
                zone.ProviderZoneId ?? zone.Name,
                request.Type, request.Name, request.Value,
                request.Ttl, request.Priority);
        }
        catch (System.Net.Http.HttpRequestException ex) when (
            ex.Message.Contains("404", System.StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Not Found", System.StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new
            {
                message = $"DNS zone for {zone.Name} is not provisioned in {zone.Provider}.",
                hint = zone.Provider == DnsProviderType.PowerDns
                    ? "This domain's authoritative DNS is hosted elsewhere (likely Cloudflare). Add the record at the external provider, or migrate the zone to the panel's PowerDNS by NS-delegating it."
                    : "The external provider rejected the zone ID. Verify the zone exists and the API token has Edit access.",
                zone = zone.Name,
                provider = zone.Provider.ToString(),
            });
        }

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

    /// <summary>
    /// F2: list zones available on the configured Cloudflare account so the operator can
    /// pick one to import. Names + IDs only; record-level fetch is a separate call.
    /// </summary>
    [HttpGet("dns/cf/zones")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> ListCloudflareZones(CancellationToken ct)
    {
        var cf = _dnsFactory.GetProvider(DnsProviderType.Cloudflare) as CloudflareDnsProvider;
        if (cf is null)
            return StatusCode(503, new { message = "Cloudflare provider not configured" });

        var zones = await cf.ListZonesAsync(ct);
        return Ok(zones);
    }

    /// <summary>
    /// F2: pull records from a Cloudflare zone into HostPanel. Upserts a DnsZone for the
    /// domain (with Provider=Cloudflare + ProviderZoneId), then reconciles records:
    /// existing rows matched by ProviderRecordId get updated, new CF records get inserted.
    /// Local-only records (no ProviderRecordId match) are preserved by default; pass
    /// removeLocalOnly=true to clean them out.
    /// </summary>
    [HttpPost("domains/{domainId}/dns/cf/import")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> ImportFromCloudflare(int domainId, [FromBody] CfImportRequest request, CancellationToken ct)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var domain = await _db.Domains.FirstOrDefaultAsync(d => d.Id == domainId, ct);
        if (domain is null) return NotFound(new { message = "Domain not found" });

        var cf = _dnsFactory.GetProvider(DnsProviderType.Cloudflare) as CloudflareDnsProvider;
        if (cf is null) return StatusCode(503, new { message = "Cloudflare provider not configured" });

        // Upsert zone — same domain may be PowerDns today; we flip it to Cloudflare here.
        var zone = await _db.DnsZones.Include(z => z.Records).FirstOrDefaultAsync(z => z.DomainId == domainId, ct);
        if (zone is null)
        {
            zone = new DnsZone
            {
                DomainId = domainId,
                Name = domain.Name,
                Provider = DnsProviderType.Cloudflare,
                ProviderZoneId = request.ZoneId,
            };
            _db.DnsZones.Add(zone);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            zone.Provider = DnsProviderType.Cloudflare;
            zone.ProviderZoneId = request.ZoneId;
        }

        var cfRecords = await cf.ListRecordsAsync(request.ZoneId, ct);

        var existingByProviderId = zone.Records
            .Where(r => !string.IsNullOrEmpty(r.ProviderRecordId))
            .ToDictionary(r => r.ProviderRecordId!, r => r);

        var imported = 0;
        var updated = 0;
        foreach (var cfr in cfRecords)
        {
            if (existingByProviderId.TryGetValue(cfr.RecordId, out var existing))
            {
                if (existing.Type != cfr.Type || existing.Name != cfr.Name ||
                    existing.Value != cfr.Value || existing.Ttl != cfr.Ttl || existing.Priority != cfr.Priority)
                {
                    existing.Type = cfr.Type;
                    existing.Name = cfr.Name;
                    existing.Value = cfr.Value;
                    existing.Ttl = cfr.Ttl;
                    existing.Priority = cfr.Priority;
                    updated++;
                }
            }
            else
            {
                _db.DnsRecords.Add(new DnsRecord
                {
                    DnsZoneId = zone.Id,
                    Type = cfr.Type,
                    Name = cfr.Name,
                    Value = cfr.Value,
                    Ttl = cfr.Ttl,
                    Priority = cfr.Priority,
                    ProviderRecordId = cfr.RecordId,
                });
                imported++;
            }
        }

        // Optional: prune local-only records (no matching ProviderRecordId in CF response).
        var pruned = 0;
        if (request.RemoveLocalOnly)
        {
            var cfIds = cfRecords.Select(r => r.RecordId).ToHashSet();
            foreach (var local in zone.Records.ToList())
            {
                if (!string.IsNullOrEmpty(local.ProviderRecordId) && !cfIds.Contains(local.ProviderRecordId))
                {
                    _db.DnsRecords.Remove(local);
                    pruned++;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { imported, updated, pruned, totalInCloudflare = cfRecords.Count });
    }

    /// <summary>
    /// F2: push HostPanel's local DnsRecords back to Cloudflare. Records without a
    /// ProviderRecordId are created; mismatches get updated. Records orphaned in CF (have
    /// ProviderRecordId but no local row) are left alone unless removeRemoteOrphans=true.
    /// </summary>
    [HttpPost("domains/{domainId}/dns/cf/export")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> ExportToCloudflare(int domainId, [FromBody] CfExportRequest request, CancellationToken ct)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var zone = await _db.DnsZones.Include(z => z.Records).FirstOrDefaultAsync(z => z.DomainId == domainId, ct);
        if (zone is null) return NotFound(new { message = "No DNS zone for this domain" });
        if (string.IsNullOrEmpty(zone.ProviderZoneId))
            return BadRequest(new { message = "Zone has no ProviderZoneId — import first to associate with a Cloudflare zone" });

        var cf = _dnsFactory.GetProvider(DnsProviderType.Cloudflare) as CloudflareDnsProvider;
        if (cf is null) return StatusCode(503, new { message = "Cloudflare provider not configured" });

        var created = 0;
        var updated = 0;
        var errors = new List<string>();

        foreach (var local in zone.Records)
        {
            try
            {
                if (string.IsNullOrEmpty(local.ProviderRecordId))
                {
                    var created_ = await cf.AddRecordAsync(zone.ProviderZoneId, local.Type, local.Name, local.Value, local.Ttl, local.Priority, ct);
                    local.ProviderRecordId = created_.RecordId;
                    created++;
                }
                else
                {
                    await cf.UpdateRecordAsync(zone.ProviderZoneId, local.ProviderRecordId, local.Value, local.Ttl, local.Priority, ct);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{local.Type} {local.Name}: {ex.Message}");
            }
        }

        // Optional: delete CF records that have no local counterpart.
        var deletedRemote = 0;
        if (request.RemoveRemoteOrphans)
        {
            var remote = await cf.ListRecordsAsync(zone.ProviderZoneId, ct);
            var localIds = zone.Records.Where(r => !string.IsNullOrEmpty(r.ProviderRecordId))
                .Select(r => r.ProviderRecordId!).ToHashSet();
            foreach (var r in remote)
            {
                if (!localIds.Contains(r.RecordId))
                {
                    try
                    {
                        await cf.DeleteRecordAsync(zone.ProviderZoneId, r.RecordId, ct);
                        deletedRemote++;
                    }
                    catch (Exception ex) { errors.Add($"delete {r.Type} {r.Name}: {ex.Message}"); }
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { created, updated, deletedRemote, errors });
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

public record CfImportRequest(string ZoneId, bool RemoveLocalOnly = false);
public record CfExportRequest(bool RemoveRemoteOrphans = false);
