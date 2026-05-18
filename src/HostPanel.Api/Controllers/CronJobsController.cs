using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Api.Middleware;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/subscriptions/{subscriptionId}/cron")]
[Authorize]
public class CronJobsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServerAgent _agent;
    private readonly TenantAuthorizationService _auth;

    public CronJobsController(AppDbContext db, IServerAgent agent, TenantAuthorizationService auth)
    {
        _db = db;
        _agent = agent;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> List(int subscriptionId)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();

        var jobs = await _db.Set<CronJob>()
            .Where(j => j.SubscriptionId == subscriptionId)
            .Select(j => new { j.Id, j.Command, j.Schedule, j.Enabled, j.Description, j.LastRunAt })
            .ToListAsync();
        return Ok(jobs);
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Create(int subscriptionId, [FromBody] CreateCronJobRequest request)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();
        var sub = await _db.Subscriptions.FindAsync(subscriptionId);
        if (sub is null) return NotFound();

        var job = new CronJob
        {
            SubscriptionId = subscriptionId,
            Command = request.Command,
            Schedule = request.Schedule,
            Description = request.Description,
            Enabled = true
        };

        _db.Set<CronJob>().Add(job);
        await _db.SaveChangesAsync();

        // Sync to system crontab via agent
        await SyncCrontabAsync(sub.SystemUsername ?? "", subscriptionId);

        return CreatedAtAction(nameof(List), new { subscriptionId }, new
        {
            job.Id, job.Command, job.Schedule, job.Enabled, job.Description
        });
    }

    [HttpPut("{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Update(int subscriptionId, int id, [FromBody] UpdateCronJobRequest request)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();
        var job = await _db.Set<CronJob>().FindAsync(id);
        if (job is null || job.SubscriptionId != subscriptionId) return NotFound();

        if (request.Command is not null) job.Command = request.Command;
        if (request.Schedule is not null) job.Schedule = request.Schedule;
        if (request.Enabled.HasValue) job.Enabled = request.Enabled.Value;
        if (request.Description is not null) job.Description = request.Description;

        await _db.SaveChangesAsync();

        var sub = await _db.Subscriptions.FindAsync(subscriptionId);
        await SyncCrontabAsync(sub?.SystemUsername ?? "", subscriptionId);

        return Ok(new { job.Id, job.Command, job.Schedule, job.Enabled, job.Description });
    }

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Delete(int subscriptionId, int id)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();
        var job = await _db.Set<CronJob>().FindAsync(id);
        if (job is null || job.SubscriptionId != subscriptionId) return NotFound();

        _db.Set<CronJob>().Remove(job);
        await _db.SaveChangesAsync();

        var sub = await _db.Subscriptions.FindAsync(subscriptionId);
        await SyncCrontabAsync(sub?.SystemUsername ?? "", subscriptionId);

        return NoContent();
    }

    private async Task SyncCrontabAsync(string username, int subscriptionId)
    {
        var jobs = await _db.Set<CronJob>()
            .Where(j => j.SubscriptionId == subscriptionId && j.Enabled)
            .ToListAsync();

        var crontab = string.Join("\n", jobs.Select(j => $"{j.Schedule} {j.Command}")) + "\n";

        // Write crontab via agent (no shell pipelines - uses crontab binary)
        await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "system.setCrontab",  // Would be added to AgentMethods
            Params = new Dictionary<string, object?>
            {
                ["username"] = username,
                ["content"] = crontab
            }
        });
    }
}

public record CreateCronJobRequest(string Command, string Schedule, string? Description);
public record UpdateCronJobRequest(string? Command, string? Schedule, bool? Enabled, string? Description);
