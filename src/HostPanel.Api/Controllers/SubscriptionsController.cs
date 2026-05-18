using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
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
    private readonly IBillingMeter _meter;

    public SubscriptionsController(AppDbContext db, ReconciliationService reconciler, WebhookDispatcher webhooks, IBillingMeter meter)
    {
        _db = db;
        _reconciler = reconciler;
        _webhooks = webhooks;
        _meter = meter;
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

    /// <summary>
    /// Cancels (default) or hard-deletes a subscription (with <c>?force=true</c>).
    ///
    /// Soft cancel marks Status=Cancelled, enqueues reconciler to remove the system user,
    /// AND deletes the associated <see cref="Domain"/> rows so their names can be reused
    /// (fix for bug #25 — IX_Domains_Name unique constraint was blocking re-creates).
    /// The Subscription row is kept for audit/billing history.
    ///
    /// Force-delete also removes the Subscription row itself. Reserved for admin cleanup
    /// where the audit trail isn't needed.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool force = false)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.User)
            .Include(s => s.Domains)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sub is null) return NotFound();
        if (!CanAccess(sub)) return Forbid();

        var domainNames = sub.Domains.Select(d => d.Name).ToList();
        sub.Status = SubscriptionStatus.Cancelled;

        // Free up domain names so they can be reused (bug #25).
        // The reconciler tears down the nginx vhost + docroot separately from the OS user removal.
        if (sub.Domains.Count > 0)
            _db.Domains.RemoveRange(sub.Domains);

        if (force && User.IsInRole(UserRole.Admin.ToString()))
            _db.Subscriptions.Remove(sub);

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

        _ = _webhooks.FireAsync(WebhookEvents.SubscriptionCancelled, new
        {
            subscriptionId = sub.Id,
            releasedDomains = domainNames,
            hardDeleted = force && User.IsInRole(UserRole.Admin.ToString()),
        });

        return NoContent();
    }

    [HttpPost("{id}/suspend")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Suspend(int id)
    {
        var sub = await _db.Subscriptions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!CanAccess(sub)) return Forbid();
        if (sub.Status == SubscriptionStatus.Suspended)
            return BadRequest(new { message = "Subscription is already suspended" });

        sub.Status = SubscriptionStatus.Suspended;
        await _db.SaveChangesAsync();
        _ = _webhooks.FireAsync(WebhookEvents.SubscriptionSuspended, new { subscriptionId = sub.Id });
        return Ok(new { id = sub.Id, status = sub.Status.ToString() });
    }

    [HttpPost("{id}/resume")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Resume(int id)
    {
        var sub = await _db.Subscriptions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!CanAccess(sub)) return Forbid();
        if (sub.Status == SubscriptionStatus.Active)
            return BadRequest(new { message = "Subscription is already active" });

        sub.Status = SubscriptionStatus.Active;
        await _db.SaveChangesAsync();
        _ = _webhooks.FireAsync(WebhookEvents.SubscriptionActivated, new { subscriptionId = sub.Id });
        return Ok(new { id = sub.Id, status = sub.Status.ToString() });
    }

    /// <summary>Change which ServicePlan backs a subscription. Used for upgrades/downgrades + attaching custom plans.</summary>
    [HttpPut("{id}/plan")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> ChangePlan(int id, [FromBody] ChangePlanRequest req)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.User)
            .Include(s => s.ServicePlan)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!CanAccess(sub)) return Forbid();

        var newPlan = await _db.ServicePlans.FindAsync(req.PlanId);
        if (newPlan is null || !newPlan.IsActive) return NotFound(new { message = "Plan not found or inactive" });
        // Custom plans only attach to their bound customer
        if (newPlan.IsCustom && newPlan.CustomerUserId != sub.UserId)
            return BadRequest(new { message = "This custom plan is bound to a different customer." });

        var oldPlanId = sub.ServicePlanId;
        sub.ServicePlanId = newPlan.Id;
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _webhooks.FireAsync("subscription.planChanged", new
        {
            subscriptionId = sub.Id,
            userId = sub.UserId,
            oldPlanId,
            newPlanId = newPlan.Id,
            newPlanName = newPlan.Name,
            newPrice = newPlan.PricePerPeriod,
        });

        return Ok(new { message = "Plan changed", subscriptionId = sub.Id, oldPlanId, newPlanId = newPlan.Id });
    }

    /// <summary>
    /// Current resource usage for a subscription vs the plan's limits.
    /// Powers the customer billing/usage panel and is also called server-side
    /// before file uploads/db creates to enforce quota (#54-56).
    /// Bandwidth tracking requires the nginx log_format to include `host=$host`;
    /// the installer ships that now, but pre-existing installs return `bandwidthTracked: false`.
    /// </summary>
    [HttpGet("{id}/usage")]
    public async Task<IActionResult> GetUsage(int id)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.User)
            .Include(s => s.ServicePlan)
            .Include(s => s.Domains)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!CanAccess(sub)) return Forbid();

        var diskBytes = await _meter.GetDiskUsageBytesAsync(id);
        var periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var bandwidthBytes = await _meter.GetBandwidthUsageBytesAsync(id, periodStart, DateTime.UtcNow);

        var mailboxCount = await _db.Mailboxes.CountAsync(m => m.Domain.SubscriptionId == id);
        var aliasCount = await _db.MailAliases.CountAsync(a => a.Domain.SubscriptionId == id);
        var dbCount = await _db.HostedDatabases.CountAsync(d => d.SubscriptionId == id);
        var ftpCount = await _db.FtpAccounts.CountAsync(f => f.SubscriptionId == id);
        var domainCount = sub.Domains.Count;

        var plan = sub.ServicePlan;
        var diskLimitBytes = plan.DiskQuotaMb * 1024L * 1024L;
        var bandwidthLimitBytes = plan.BandwidthLimitMb * 1024L * 1024L;

        return Ok(new
        {
            subscriptionId = id,
            servicePlan = plan.Name,
            periodStart,
            periodEnd = periodStart.AddMonths(1).AddSeconds(-1),
            usage = new
            {
                diskBytes,
                diskMb = diskBytes / (1024.0 * 1024.0),
                bandwidthBytes,
                bandwidthMb = bandwidthBytes / (1024.0 * 1024.0),
                bandwidthTracked = bandwidthBytes > 0,
                domainCount,
                mailboxCount,
                aliasCount,
                databaseCount = dbCount,
                ftpAccountCount = ftpCount,
            },
            limits = new
            {
                diskBytes = diskLimitBytes,
                diskMb = plan.DiskQuotaMb,
                bandwidthBytes = bandwidthLimitBytes,
                bandwidthMb = plan.BandwidthLimitMb,
                maxDomains = plan.MaxDomains,
                maxMailboxes = plan.MaxEmailAccounts,
                maxDatabases = plan.MaxDatabases,
                maxFtpAccounts = plan.MaxFtpAccounts,
            },
            ratios = new
            {
                disk = diskLimitBytes > 0 ? (double)diskBytes / diskLimitBytes : 0,
                bandwidth = bandwidthLimitBytes > 0 ? (double)bandwidthBytes / bandwidthLimitBytes : 0,
                domains = plan.MaxDomains > 0 ? (double)domainCount / plan.MaxDomains : 0,
                mailboxes = plan.MaxEmailAccounts > 0 ? (double)mailboxCount / plan.MaxEmailAccounts : 0,
                databases = plan.MaxDatabases > 0 ? (double)dbCount / plan.MaxDatabases : 0,
                ftpAccounts = plan.MaxFtpAccounts > 0 ? (double)ftpCount / plan.MaxFtpAccounts : 0,
            }
        });
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

public record ChangePlanRequest(int PlanId);
