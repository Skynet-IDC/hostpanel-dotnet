using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Infrastructure.Services;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "Admin")]
public class WebhooksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly WebhookDispatcher _dispatcher;

    public WebhooksController(AppDbContext db, WebhookDispatcher dispatcher)
    {
        _db = db;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// List all registered webhook endpoints.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var endpoints = await _db.Set<WebhookEndpoint>()
            .Select(w => new
            {
                w.Id, w.Url, w.Events, w.IsActive, w.Description,
                w.FailureCount, w.LastTriggeredAt, w.LastFailureAt, w.CreatedAt
            })
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
        return Ok(endpoints);
    }

    /// <summary>
    /// Register a new webhook endpoint (e.g., an n8n webhook URL).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWebhookRequest request)
    {
        // Validate events
        if (request.Events != "*")
        {
            var requestedEvents = request.Events.Split(',').Select(e => e.Trim()).ToList();
            var invalid = requestedEvents.Except(WebhookEvents.All).ToList();
            if (invalid.Count > 0)
                return BadRequest(new { message = $"Invalid events: {string.Join(", ", invalid)}" });
        }

        var endpoint = new WebhookEndpoint
        {
            Url = request.Url,
            Secret = request.Secret ?? Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            Events = request.Events,
            Description = request.Description,
            IsActive = true
        };

        _db.Set<WebhookEndpoint>().Add(endpoint);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new { id = endpoint.Id }, new
        {
            endpoint.Id, endpoint.Url, endpoint.Events, endpoint.Secret,
            endpoint.Description, endpoint.IsActive,
            message = "Webhook registered. Use the secret to verify payloads via X-HostPanel-Signature header."
        });
    }

    /// <summary>
    /// Update a webhook endpoint.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateWebhookRequest request)
    {
        var endpoint = await _db.Set<WebhookEndpoint>().FindAsync(id);
        if (endpoint is null) return NotFound();

        if (request.Url is not null) endpoint.Url = request.Url;
        if (request.Events is not null) endpoint.Events = request.Events;
        if (request.IsActive.HasValue) endpoint.IsActive = request.IsActive.Value;
        if (request.Description is not null) endpoint.Description = request.Description;

        await _db.SaveChangesAsync();
        return Ok(new { endpoint.Id, endpoint.Url, endpoint.Events, endpoint.IsActive });
    }

    /// <summary>
    /// Delete a webhook endpoint.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var endpoint = await _db.Set<WebhookEndpoint>().FindAsync(id);
        if (endpoint is null) return NotFound();

        _db.Set<WebhookEndpoint>().Remove(endpoint);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Test a webhook endpoint by sending a test event.
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<IActionResult> Test(int id)
    {
        var endpoint = await _db.Set<WebhookEndpoint>().FindAsync(id);
        if (endpoint is null) return NotFound();

        await _dispatcher.FireAsync("test.ping", new
        {
            message = "Test webhook from HostPanel",
            endpointId = id,
            timestamp = DateTime.UtcNow
        });

        return Ok(new { success = true, message = "Test event dispatched" });
    }

    /// <summary>
    /// List all available webhook event types.
    /// </summary>
    [HttpGet("events")]
    public IActionResult ListEvents()
    {
        var events = WebhookEvents.All.Select(e =>
        {
            var parts = e.Split('.');
            return new { name = e, category = parts[0], action = string.Join(".", parts.Skip(1)) };
        });
        return Ok(events);
    }

    /// <summary>
    /// Reset failure count for an endpoint (re-enable after failures).
    /// </summary>
    [HttpPost("{id}/reset")]
    public async Task<IActionResult> ResetFailures(int id)
    {
        var endpoint = await _db.Set<WebhookEndpoint>().FindAsync(id);
        if (endpoint is null) return NotFound();

        endpoint.FailureCount = 0;
        endpoint.IsActive = true;
        await _db.SaveChangesAsync();

        return Ok(new { endpoint.Id, endpoint.FailureCount, endpoint.IsActive });
    }
}

public record CreateWebhookRequest(string Url, string Events = "*", string? Secret = null, string? Description = null);
public record UpdateWebhookRequest(string? Url = null, string? Events = null, bool? IsActive = null, string? Description = null);
