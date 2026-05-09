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
    public async Task<ActionResult<List<ServicePlan>>> List()
    {
        var plans = await _db.ServicePlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.PricePerPeriod)
            .ToListAsync();
        return Ok(plans);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ServicePlan>> Get(int id)
    {
        var plan = await _db.ServicePlans.FindAsync(id);
        if (plan is null) return NotFound();
        return Ok(plan);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ServicePlan>> Create([FromBody] ServicePlan plan)
    {
        _db.ServicePlans.Add(plan);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = plan.Id }, plan);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ServicePlan>> Update(int id, [FromBody] ServicePlan updated)
    {
        var plan = await _db.ServicePlans.FindAsync(id);
        if (plan is null) return NotFound();

        plan.Name = updated.Name;
        plan.Description = updated.Description;
        plan.DiskQuotaMb = updated.DiskQuotaMb;
        plan.BandwidthLimitMb = updated.BandwidthLimitMb;
        plan.MaxDomains = updated.MaxDomains;
        plan.MaxEmailAccounts = updated.MaxEmailAccounts;
        plan.MaxDatabases = updated.MaxDatabases;
        plan.MaxFtpAccounts = updated.MaxFtpAccounts;
        plan.PricePerPeriod = updated.PricePerPeriod;
        plan.DefaultBillingPeriod = updated.DefaultBillingPeriod;
        plan.GracePeriodDays = updated.GracePeriodDays;
        plan.AutoSuspend = updated.AutoSuspend;
        plan.ReminderSchedule = updated.ReminderSchedule;
        plan.DeletionAfterDays = updated.DeletionAfterDays;

        await _db.SaveChangesAsync();
        return Ok(plan);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var plan = await _db.ServicePlans.FindAsync(id);
        if (plan is null) return NotFound();

        plan.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
