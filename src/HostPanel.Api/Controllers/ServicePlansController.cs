using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ServicePlansController : ControllerBase
{
    private readonly AppDbContext _db;

    public ServicePlansController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<ServicePlan>>> List([FromQuery] bool includeCustom = false, [FromQuery] string? customerUserId = null)
    {
        var q = _db.ServicePlans.Where(p => p.IsActive).AsQueryable();
        if (!includeCustom && customerUserId is null)
            q = q.Where(p => !p.IsCustom);
        if (customerUserId is not null)
            q = q.Where(p => p.CustomerUserId == customerUserId || !p.IsCustom);
        var plans = await q.OrderBy(p => p.PricePerPeriod).ToListAsync();
        return Ok(plans);
    }

    /// <summary>List a customer's custom (one-off) plans only.</summary>
    [HttpGet("custom/{customerUserId}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> ListCustom(string customerUserId)
    {
        var plans = await _db.ServicePlans
            .Where(p => p.IsCustom && p.CustomerUserId == customerUserId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        return Ok(plans);
    }

    /// <summary>
    /// Create a one-off plan bound to a single customer. Hidden from the public catalog.
    /// Same body as POST /plans but auto-sets IsCustom=true and CustomerUserId.
    /// </summary>
    [HttpPost("custom/{customerUserId}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> CreateCustom(string customerUserId, [FromBody] ServicePlan plan)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == customerUserId))
            return NotFound(new { message = "Customer not found" });
        plan.IsCustom = true;
        plan.CustomerUserId = customerUserId;
        plan.IsActive = true;
        if (string.IsNullOrEmpty(plan.Name)) plan.Name = "Custom plan";
        _db.ServicePlans.Add(plan);
        await _db.SaveChangesAsync();
        return Ok(plan);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ServicePlan>> Get(int id)
    {
        var plan = await _db.ServicePlans.FindAsync(id);
        if (plan is null) return NotFound();
        return Ok(plan);
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<ActionResult<ServicePlan>> Create([FromBody] ServicePlan plan)
    {
        _db.ServicePlans.Add(plan);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = plan.Id }, plan);
    }

    [HttpPut("{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<ActionResult<ServicePlan>> Update(int id, [FromBody] UpdateServicePlanRequest req)
    {
        var plan = await _db.ServicePlans.FindAsync(id);
        if (plan is null) return NotFound();

        // Partial update — only apply fields that were explicitly provided (non-null)
        if (req.Name is not null) plan.Name = req.Name;
        if (req.Description is not null) plan.Description = req.Description;
        if (req.DiskQuotaMb.HasValue) plan.DiskQuotaMb = req.DiskQuotaMb.Value;
        if (req.BandwidthLimitMb.HasValue) plan.BandwidthLimitMb = req.BandwidthLimitMb.Value;
        if (req.MaxDomains.HasValue) plan.MaxDomains = req.MaxDomains.Value;
        if (req.MaxEmailAccounts.HasValue) plan.MaxEmailAccounts = req.MaxEmailAccounts.Value;
        if (req.MaxDatabases.HasValue) plan.MaxDatabases = req.MaxDatabases.Value;
        if (req.MaxFtpAccounts.HasValue) plan.MaxFtpAccounts = req.MaxFtpAccounts.Value;
        if (req.PricePerPeriod.HasValue) plan.PricePerPeriod = req.PricePerPeriod.Value;
        if (req.GracePeriodDays.HasValue) plan.GracePeriodDays = req.GracePeriodDays.Value;
        if (req.AutoSuspend.HasValue) plan.AutoSuspend = req.AutoSuspend.Value;
        if (req.ReminderSchedule is not null) plan.ReminderSchedule = req.ReminderSchedule;
        if (req.DeletionAfterDays.HasValue) plan.DeletionAfterDays = req.DeletionAfterDays.Value;

        plan.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(plan);
    }

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var plan = await _db.ServicePlans
            .Include(p => p.Subscriptions)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (plan is null) return NotFound();

        var activeCount = plan.Subscriptions.Count(s =>
            s.Status != HostPanel.Core.Enums.SubscriptionStatus.Cancelled);
        if (activeCount > 0)
            return Conflict(new { message = $"Cannot delete plan — {activeCount} active subscription(s) reference it" });

        plan.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record UpdateServicePlanRequest(
    string? Name = null,
    string? Description = null,
    int? DiskQuotaMb = null,
    int? BandwidthLimitMb = null,
    int? MaxDomains = null,
    int? MaxEmailAccounts = null,
    int? MaxDatabases = null,
    int? MaxFtpAccounts = null,
    decimal? PricePerPeriod = null,
    int? GracePeriodDays = null,
    bool? AutoSuspend = null,
    string? ReminderSchedule = null,
    int? DeletionAfterDays = null
);
