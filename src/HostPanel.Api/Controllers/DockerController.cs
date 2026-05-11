using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Api.Middleware;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/subscriptions/{subscriptionId}/docker")]
[Authorize]
public class DockerController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServerAgent _agent;
    private readonly TenantAuthorizationService _auth;

    public DockerController(AppDbContext db, IServerAgent agent, TenantAuthorizationService auth)
    {
        _db = db;
        _agent = agent;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> List(int subscriptionId)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();

        var containers = await _db.Set<DockerContainer>()
            .Where(c => c.SubscriptionId == subscriptionId)
            .Select(c => new { c.Id, c.Name, c.Image, c.Status, c.Ports, c.ProxyPort, c.CreatedAt })
            .ToListAsync();
        return Ok(containers);
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Create(int subscriptionId, [FromBody] CreateContainerRequest request)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "docker.create",
            Params = new Dictionary<string, object?>
            {
                ["image"] = request.Image,
                ["name"] = request.Name,
                ["ports"] = request.Ports,
                ["envVars"] = request.EnvVars,
                ["volumes"] = request.Volumes
            }
        });

        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error?.Message });

        var container = new DockerContainer
        {
            SubscriptionId = subscriptionId,
            ContainerId = result.Result?.Data?.TryGetValue("containerId", out var cid) == true ? cid?.ToString() ?? "" : "",
            Name = request.Name,
            Image = request.Image,
            Status = "running",
            Ports = request.Ports,
            EnvVars = request.EnvVars,
            Volumes = request.Volumes,
            ProxyPort = request.ProxyPort
        };

        _db.Set<DockerContainer>().Add(container);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new { subscriptionId }, new
        {
            container.Id, container.Name, container.Image, container.Status, container.ContainerId
        });
    }

    [HttpPost("{id}/start")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Start(int subscriptionId, int id) => await ContainerAction(subscriptionId, id, "docker.start");

    [HttpPost("{id}/stop")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Stop(int subscriptionId, int id) => await ContainerAction(subscriptionId, id, "docker.stop");

    [HttpPost("{id}/restart")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Restart(int subscriptionId, int id) => await ContainerAction(subscriptionId, id, "docker.restart");

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> Delete(int subscriptionId, int id)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();
        var container = await _db.Set<DockerContainer>().FindAsync(id);
        if (container is null || container.SubscriptionId != subscriptionId) return NotFound();

        await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "docker.remove",
            Params = new Dictionary<string, object?> { ["containerId"] = container.ContainerId }
        });

        _db.Set<DockerContainer>().Remove(container);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<IActionResult> ContainerAction(int subscriptionId, int id, string method)
    {
        if (!await _auth.CanAccessSubscriptionAsync(User, subscriptionId)) return Forbid();
        var container = await _db.Set<DockerContainer>().FindAsync(id);
        if (container is null || container.SubscriptionId != subscriptionId) return NotFound();

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = method,
            Params = new Dictionary<string, object?> { ["containerId"] = container.ContainerId }
        });

        return Ok(new { success = result.IsSuccess, error = result.Error?.Message });
    }
}

public record CreateContainerRequest(string Name, string Image, string? Ports = null, string? EnvVars = null, string? Volumes = null, int? ProxyPort = null);
