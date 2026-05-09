using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Api.Middleware;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class DomainsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TenantAuthorizationService _auth;

    public DomainsController(AppDbContext db, TenantAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    [HttpGet]
    public async Task<ActionResult<List<DomainDto>>> List([FromQuery] int? subscriptionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        var query = _db.Domains
            .Include(d => d.Subscription)
            .ThenInclude(s => s.User)
            .AsQueryable();

        if (subscriptionId.HasValue)
            query = query.Where(d => d.SubscriptionId == subscriptionId.Value);

        if (role == UserRole.Customer.ToString())
            query = query.Where(d => d.Subscription.UserId == userId);
        else if (role == UserRole.Reseller.ToString())
            query = query.Where(d =>
                d.Subscription.UserId == userId ||
                d.Subscription.User.ParentUserId == userId);

        var domains = await query.OrderBy(d => d.Name).ToListAsync();
        return Ok(domains.Select(MapToDto).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DomainDto>> Get(int id)
    {
        if (!await _auth.CanAccessDomainAsync(User, id)) return Forbid();

        var domain = await _db.Domains
            .Include(d => d.Subscription)
            .ThenInclude(s => s.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (domain is null) return NotFound();

        return Ok(MapToDto(domain));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<ActionResult<DomainDto>> Create([FromBody] CreateDomainRequest request)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, request.SubscriptionId)) return Forbid();

        var sub = await _db.Subscriptions
            .Include(s => s.ServicePlan)
            .Include(s => s.Domains)
            .FirstOrDefaultAsync(s => s.Id == request.SubscriptionId);

        if (sub is null) return BadRequest(new { message = "Invalid subscription" });

        // Check domain limit
        if (sub.Domains.Count >= sub.ServicePlan.MaxDomains)
            return BadRequest(new { message = "Domain limit reached for this plan" });

        // Check uniqueness
        if (await _db.Domains.AnyAsync(d => d.Name == request.Name))
            return Conflict(new { message = "Domain already exists" });

        var domain = new Domain
        {
            SubscriptionId = request.SubscriptionId,
            Name = request.Name,
            DocumentRoot = $"/var/www/{sub.SystemUsername}/domains/{request.Name}",
            PhpVersion = request.PhpVersion,
            IsMainDomain = false
        };

        _db.Domains.Add(domain);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = domain.Id }, MapToDto(domain));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<ActionResult<DomainDto>> Update(int id, [FromBody] UpdateDomainRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, id)) return Forbid();

        var domain = await _db.Domains.FindAsync(id);
        if (domain is null) return NotFound();

        if (request.PhpVersion is not null)
            domain.PhpVersion = request.PhpVersion;
        if (request.SslEnabled.HasValue)
            domain.SslEnabled = request.SslEnabled.Value;

        await _db.SaveChangesAsync();
        return Ok(MapToDto(domain));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await _auth.CanAccessDomainAsync(User, id)) return Forbid();

        var domain = await _db.Domains.FindAsync(id);
        if (domain is null) return NotFound();

        if (domain.IsMainDomain)
            return BadRequest(new { message = "Cannot delete the main domain. Delete the subscription instead." });

        _db.Domains.Remove(domain);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static DomainDto MapToDto(Domain d) => new(
        d.Id, d.SubscriptionId, d.Name, d.DocumentRoot,
        d.SslEnabled, d.PhpVersion, d.IsMainDomain, d.CreatedAt
    );
}
