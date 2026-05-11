using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Infrastructure.Services;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ReconciliationService _reconciler;
    private readonly WebhookDispatcher _webhooks;

    public SubscriptionsController(AppDbContext db, ReconciliationService reconciler, WebhookDispatcher webhooks)
    {
        _db = db;
        _reconciler = reconciler;
        _webhooks = webhooks;
    }

    [HttpGet]
    public async Task<ActionResult<List<SubscriptionDto>>> List()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        var query = _db.Subscriptions
            .Include(s => s.User)
            .Include(s => s.ServicePlan)
            .Include(s => s.Domains)
            .AsQueryable();

        // Customers see only their own; Resellers see their customers; Admins see all
        if (role == UserRole.Customer.ToString())
            query = query.Where(s => s.UserId == userId);
        else if (role == UserRole.Reseller.ToString())
            query = query.Where(s => s.User.ParentUserId == userId || s.UserId == userId);

        var subs = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
        return Ok(subs.Select(MapToDto).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SubscriptionDto>> Get(int id)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.User)
            .Include(s => s.ServicePlan)
            .Include(s => s.Domains)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sub is null) return NotFound();
        if (!CanAccess(sub)) return Forbid();

        return Ok(MapToDto(sub));
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<ActionResult<SubscriptionDto>> Create([FromBody] CreateSubscriptionRequest request)
    {
        var plan = await _db.ServicePlans.FindAsync(request.ServicePlanId);
        if (plan is null) return BadRequest(new { message = "Invalid service plan" });

        var user = await _db.Users.FindAsync(request.UserId);
        if (user is null) return BadRequest(new { message = "Invalid user" });

        // Verify caller can manage target user
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (currentRole == Core.Enums.UserRole.Reseller.ToString() && user.ParentUserId != currentUserId)
            return Forbid();

        // Generate system username from domain
        var systemUsername = GenerateSystemUsername(request.MainDomain);

        var subscription = new Subscription
        {
            UserId = request.UserId,
            ServicePlanId = request.ServicePlanId,
            Status = SubscriptionStatus.PendingSetup,
            BillingPeriod = request.BillingPeriod,
            SystemUsername = systemUsername,
            NextInvoiceDate = DateTime.UtcNow.AddDays(1)
        };

        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        // Add main domain
        var domain = new Domain
        {
            SubscriptionId = subscription.Id,
            Name = request.MainDomain,
            DocumentRoot = $"/var/www/{systemUsername}/httpdocs",
            IsMainDomain = true
        };
        _db.Domains.Add(domain);
        await _db.SaveChangesAsync();

        // Enqueue for reconciliation. Pass main-domain info so the reconciler can call
        // nginx.createVhost in step 3 (otherwise the domain has a DB row + docroot but
        // nginx never serves it).
        await _reconciler.EnqueueAsync(
            "subscription",
            subscription.Id,
            ProvisioningState.Pending,
            JsonSerializer.Serialize(new SubscriptionConfig(
                systemUsername,
                plan.DiskQuotaMb,
                plan.BandwidthLimitMb,
                MainDomain: domain.Name,
                MainDomainDocRoot: domain.DocumentRoot,
                PhpVersion: "8.3"
            ))
        );

        // Reload with includes
        var created = await _db.Subscriptions
            .Include(s => s.User)
            .Include(s => s.ServicePlan)
            .Include(s => s.Domains)
            .FirstAsync(s => s.Id == subscription.Id);

        var dto = MapToDto(created);

        // Fire webhook
        _ = _webhooks.FireAsync(WebhookEvents.SubscriptionCreated, new
        {
            subscriptionId = subscription.Id, domain = request.MainDomain,
            userId = request.UserId, planId = request.ServicePlanId
        });

        return CreatedAtAction(nameof(Get), new { id = subscription.Id }, dto);
    }

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Delete(int id)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sub is null) return NotFound();
        if (!CanAccess(sub)) return Forbid();

        sub.Status = SubscriptionStatus.Cancelled;
        await _db.SaveChangesAsync();

        // Enqueue deletion via reconciler
        await _reconciler.EnqueueAsync(
            "subscription",
            sub.Id,
            ProvisioningState.DeletePending,
            JsonSerializer.Serialize(new SubscriptionConfig(
                sub.SystemUsername ?? "",
                0, 0
            ))
        );

        _ = _webhooks.FireAsync(WebhookEvents.SubscriptionCancelled, new { subscriptionId = sub.Id });

        return NoContent();
    }

    private bool CanAccess(Subscription sub)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        if (role == UserRole.Admin.ToString()) return true;
        if (role == UserRole.Reseller.ToString())
            return sub.UserId == userId || sub.User.ParentUserId == userId;
        return sub.UserId == userId;
    }

    private static string GenerateSystemUsername(string domain)
    {
        var clean = domain.Replace(".", "_").Replace("-", "_");
        if (clean.Length > 28) clean = clean[..28];
        return $"hp_{clean}";
    }

    private static SubscriptionDto MapToDto(Subscription s) => new(
        Id: s.Id,
        UserId: s.UserId,
        UserEmail: s.User?.Email ?? "",
        ServicePlanId: s.ServicePlanId,
        ServicePlanName: s.ServicePlan?.Name ?? "",
        Status: s.Status,
        BillingPeriod: s.BillingPeriod,
        SystemUsername: s.SystemUsername,
        CreatedAt: s.CreatedAt,
        Domains: s.Domains.Select(d => new DomainDto(
            d.Id, d.SubscriptionId, d.Name, d.DocumentRoot,
            d.SslEnabled, d.PhpVersion, d.IsMainDomain, d.CreatedAt
        )).ToList()
    );
}
