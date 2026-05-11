using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Infrastructure.Services;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
public class ServersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ServerAgentFactory _agentFactory;

    public ServersController(AppDbContext db, ServerAgentFactory agentFactory)
    {
        _db = db;
        _agentFactory = agentFactory;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var servers = await _db.Set<Server>()
            .OrderBy(s => s.Hostname)
            .Select(s => new
            {
                s.Id, s.Hostname, s.IpAddress, s.IsLocal, s.IsActive,
                s.IsHealthy, s.LastHealthCheck, s.OsInfo, s.ServerGroup
            })
            .ToListAsync();
        return Ok(servers);
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddServerRequest request)
    {
        var server = new Server
        {
            Hostname = request.Hostname,
            IpAddress = request.IpAddress,
            AgentPort = request.AgentPort,
            IsLocal = false,
            IsActive = true,
            AgentToken = request.AgentToken,
            ServerGroup = request.ServerGroup
        };

        _db.Set<Server>().Add(server);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new { id = server.Id }, new
        {
            server.Id, server.Hostname, server.IpAddress, server.IsActive
        });
    }

    [HttpPost("{id}/health-check")]
    public async Task<IActionResult> HealthCheck(int id)
    {
        var server = await _db.Set<Server>().FindAsync(id);
        if (server is null) return NotFound();

        var agent = _agentFactory.GetAgent(server);
        var healthy = await agent.IsAvailableAsync();

        server.IsHealthy = healthy;
        server.LastHealthCheck = DateTime.UtcNow;

        if (healthy)
        {
            var stats = await agent.ExecuteAsync(new AgentRequest
            {
                Method = AgentMethods.SystemGetStats,
                Params = []
            });
            if (stats.Result?.Data?.TryGetValue("osVersion", out var os) == true)
                server.OsInfo = os?.ToString();
        }

        await _db.SaveChangesAsync();

        return Ok(new { server.IsHealthy, server.LastHealthCheck, server.OsInfo });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(int id)
    {
        var server = await _db.Set<Server>().FindAsync(id);
        if (server is null) return NotFound();
        if (server.IsLocal) return BadRequest(new { message = "Cannot remove the local server" });

        _db.Set<Server>().Remove(server);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record AddServerRequest(string Hostname, string IpAddress, int AgentPort = 50051, string? AgentToken = null, string? ServerGroup = null);
