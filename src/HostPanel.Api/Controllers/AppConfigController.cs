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
[Route("api/v1/domains/{domainId}/app")]
[Authorize]
public class AppConfigController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServerAgent _agent;
    private readonly TenantAuthorizationService _auth;

    public AppConfigController(AppDbContext db, IServerAgent agent, TenantAuthorizationService auth)
    {
        _db = db;
        _agent = agent;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> Get(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var config = await _db.Set<AppConfig>().FirstOrDefaultAsync(a => a.DomainId == domainId);
        if (config is null) return Ok(new { configured = false });

        return Ok(new
        {
            configured = true,
            config.Runtime, config.EntryPoint, config.Port, config.NodeVersion,
            config.RestartMethod, config.BuildCommand, config.StartCommand,
            config.EnvironmentVariables, config.WorkingDirectory, config.AutoDeploy
        });
    }

    [HttpPut]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> Configure(int domainId, [FromBody] ConfigureAppRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var domain = await _db.Domains.Include(d => d.Subscription).FirstOrDefaultAsync(d => d.Id == domainId);
        if (domain is null) return NotFound();

        var config = await _db.Set<AppConfig>().FirstOrDefaultAsync(a => a.DomainId == domainId);
        if (config is null)
        {
            config = new AppConfig { DomainId = domainId };
            _db.Set<AppConfig>().Add(config);
        }

        config.Runtime = request.Runtime ?? config.Runtime;
        config.EntryPoint = request.EntryPoint ?? config.EntryPoint;
        config.Port = request.Port ?? config.Port;
        config.NodeVersion = request.NodeVersion ?? config.NodeVersion;
        config.RestartMethod = request.RestartMethod ?? config.RestartMethod;
        config.BuildCommand = request.BuildCommand ?? config.BuildCommand;
        config.StartCommand = request.StartCommand ?? config.StartCommand;
        config.EnvironmentVariables = request.EnvironmentVariables ?? config.EnvironmentVariables;
        config.WorkingDirectory = request.WorkingDirectory ?? domain.DocumentRoot;
        config.AutoDeploy = request.AutoDeploy ?? config.AutoDeploy;

        await _db.SaveChangesAsync();

        // Apply: generate systemd unit or Passenger config + Nginx reverse proxy
        await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "app.configure",
            Params = new Dictionary<string, object?>
            {
                ["domain"] = domain.Name,
                ["docRoot"] = domain.DocumentRoot,
                ["username"] = domain.Subscription.SystemUsername,
                ["runtime"] = config.Runtime,
                ["entryPoint"] = config.EntryPoint,
                ["port"] = config.Port,
                ["nodeVersion"] = config.NodeVersion,
                ["restartMethod"] = config.RestartMethod,
                ["buildCommand"] = config.BuildCommand,
                ["startCommand"] = config.StartCommand,
                ["envVars"] = config.EnvironmentVariables,
                ["workingDirectory"] = config.WorkingDirectory
            }
        });

        return Ok(new { message = "App configured", config.Runtime, config.RestartMethod, config.Port });
    }

    /// <summary>Run build command (npm run build, prisma migrate, etc.)</summary>
    [HttpPost("build")]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> Build(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var config = await _db.Set<AppConfig>().Include(a => a.Domain).ThenInclude(d => d.Subscription)
            .FirstOrDefaultAsync(a => a.DomainId == domainId);
        if (config is null) return BadRequest(new { message = "App not configured" });

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "app.build",
            Params = new Dictionary<string, object?>
            {
                ["workingDirectory"] = config.WorkingDirectory ?? config.Domain.DocumentRoot,
                ["buildCommand"] = config.BuildCommand,
                ["username"] = config.Domain.Subscription.SystemUsername,
                ["envVars"] = config.EnvironmentVariables
            }
        });

        return Ok(new { success = result.IsSuccess, data = result.Result?.Data, error = result.Error?.Message });
    }

    /// <summary>Restart the app (systemd restart, touch tmp/restart.txt, or pm2 restart).</summary>
    [HttpPost("restart")]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> Restart(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var config = await _db.Set<AppConfig>().Include(a => a.Domain).ThenInclude(d => d.Subscription)
            .FirstOrDefaultAsync(a => a.DomainId == domainId);
        if (config is null) return BadRequest(new { message = "App not configured" });

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "app.restart",
            Params = new Dictionary<string, object?>
            {
                ["domain"] = config.Domain.Name,
                ["restartMethod"] = config.RestartMethod,
                ["workingDirectory"] = config.WorkingDirectory ?? config.Domain.DocumentRoot
            }
        });

        return Ok(new { success = result.IsSuccess, error = result.Error?.Message });
    }

    /// <summary>Manage .env file for the app.</summary>
    [HttpGet("env")]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> GetEnv(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();
        var config = await _db.Set<AppConfig>().FirstOrDefaultAsync(a => a.DomainId == domainId);
        return Ok(new { environmentVariables = config?.EnvironmentVariables ?? "{}" });
    }

    [HttpPut("env")]
    [Authorize(Roles = "Admin,Reseller")]
    public async Task<IActionResult> SetEnv(int domainId, [FromBody] SetEnvRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var config = await _db.Set<AppConfig>().Include(a => a.Domain)
            .FirstOrDefaultAsync(a => a.DomainId == domainId);
        if (config is null) return BadRequest(new { message = "App not configured" });

        config.EnvironmentVariables = request.EnvironmentVariables;
        await _db.SaveChangesAsync();

        // Write .env file to disk via agent
        await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "app.writeEnv",
            Params = new Dictionary<string, object?>
            {
                ["workingDirectory"] = config.WorkingDirectory ?? config.Domain.DocumentRoot,
                ["envVars"] = request.EnvironmentVariables
            }
        });

        return Ok(new { message = "Environment variables updated" });
    }
}

public record ConfigureAppRequest(
    string? Runtime = null, string? EntryPoint = null, int? Port = null,
    string? NodeVersion = null, string? RestartMethod = null,
    string? BuildCommand = null, string? StartCommand = null,
    string? EnvironmentVariables = null, string? WorkingDirectory = null,
    bool? AutoDeploy = null
);

public record SetEnvRequest(string EnvironmentVariables);
