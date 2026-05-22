using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Api.Middleware;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

/// <summary>
/// Phase C — per-domain PHP settings (Plesk-parity): version picker, extension
/// toggles, php.ini overrides. State lives on Domain (PhpVersion + PhpExtensionsJson
/// + PhpIniJson). Mutations trigger the agent's php.updatePool method which
/// rewrites the FPM pool config + reloads php{ver}-fpm.
/// </summary>
[ApiController]
[Route("api/v1/domains/{domainId:int}/php")]
[Authorize]
public class PhpController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TenantAuthorizationService _auth;
    private readonly IServerAgent _agent;
    private readonly ILogger<PhpController> _logger;

    public PhpController(AppDbContext db, TenantAuthorizationService auth, IServerAgent agent, ILogger<PhpController> logger)
    {
        _db = db;
        _auth = auth;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>
    /// Get PHP state for a domain — its current version + enabled extensions +
    /// ini overrides PLUS the host's installed PHP versions and the catalog of
    /// available extensions for the domain's currently-set version.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(int domainId)
    {
        var domain = await LoadDomainAsync(domainId);
        if (domain is null) return NotFound(new { message = "Domain not found" });
        if (!await CanAccessAsync(domain)) return Forbid();

        var versions = await CallAgentListAsync<List<string>>(AgentMethods.PhpListVersions, new(), "versions") ?? [];
        var extInfo = await CallAgentAsync(AgentMethods.PhpListExtensions, new Dictionary<string, object?> { ["phpVersion"] = domain.PhpVersion });
        var available = ExtractStringList(extInfo, "available");
        var enabled = ExtractStringList(extInfo, "enabled");

        return Ok(new
        {
            domainId = domain.Id,
            domainName = domain.Name,
            currentVersion = domain.PhpVersion,
            extensions = ParseList(domain.PhpExtensionsJson),
            iniOverrides = ParseDict(domain.PhpIniJson),
            host = new
            {
                installedVersions = versions,
                availableExtensions = available,
                currentlyEnabledExtensions = enabled
            }
        });
    }

    /// <summary>
    /// Update PHP version / extensions / ini overrides for a domain. Persists
    /// the new state and calls the agent to regenerate the FPM pool config.
    /// If the version changes, the old version's pool file is removed.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Update(int domainId, [FromBody] UpdatePhpRequest req)
    {
        var domain = await LoadDomainAsync(domainId);
        if (domain is null) return NotFound(new { message = "Domain not found" });
        if (!await CanAccessAsync(domain)) return Forbid();

        var systemUser = domain.Subscription.SystemUsername;
        if (string.IsNullOrWhiteSpace(systemUser))
            return BadRequest(new { message = "Domain's subscription has no SystemUsername — provision the subscription first." });

        var oldVersion = domain.PhpVersion;
        var newVersion = string.IsNullOrWhiteSpace(req.PhpVersion) ? oldVersion : req.PhpVersion!;
        if (!System.Text.RegularExpressions.Regex.IsMatch(newVersion, @"^\d+\.\d+$"))
            return BadRequest(new { message = $"Invalid PHP version '{newVersion}'. Expected major.minor (e.g. 8.3)." });

        var extensions = req.Extensions ?? ParseList(domain.PhpExtensionsJson);
        var ini = req.IniOverrides ?? ParseDict(domain.PhpIniJson);

        // Refuse to flip to a PHP version that isn't actually installed on the host.
        var versionsResp = await CallAgentAsync(AgentMethods.PhpListVersions, new());
        var installed = ExtractStringList(versionsResp, "versions");
        if (installed.Count > 0 && !installed.Contains(newVersion))
            return BadRequest(new { message = $"PHP {newVersion} is not installed on this server. Installed: {string.Join(", ", installed)}." });

        domain.PhpVersion = newVersion;
        domain.PhpExtensionsJson = JsonSerializer.Serialize(extensions);
        domain.PhpIniJson = JsonSerializer.Serialize(ini);
        await _db.SaveChangesAsync();

        var poolName = $"dom{domain.Id}";
        var agentResult = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.PhpUpdatePool,
            Params = new Dictionary<string, object?>
            {
                ["poolName"] = poolName,
                ["username"] = systemUser,
                ["phpVersion"] = newVersion,
                ["oldPhpVersion"] = oldVersion,
                ["extensionsJson"] = domain.PhpExtensionsJson,
                ["iniJson"] = domain.PhpIniJson
            }
        });

        _logger.LogInformation("Updated PHP for domain {Id} ({Name}): {Old}->{New}, extensions={Exts}, iniKeys={IniCount}, agentOk={Ok}",
            domain.Id, domain.Name, oldVersion, newVersion, extensions.Count, ini.Count, agentResult.IsSuccess);

        return Ok(new
        {
            success = agentResult.IsSuccess,
            domainId = domain.Id,
            currentVersion = newVersion,
            extensions,
            iniOverrides = ini,
            agentMessage = agentResult.Error?.Message ?? "Pool config regenerated.",
            agentData = agentResult.Result?.Data
        });
    }

    // === helpers ===

    private async Task<Domain?> LoadDomainAsync(int domainId) =>
        await _db.Domains.Include(d => d.Subscription).ThenInclude(s => s.User)
            .FirstOrDefaultAsync(d => d.Id == domainId);

    private Task<bool> CanAccessAsync(Domain d) => _auth.CanAccessSubscriptionAsync(User, d.SubscriptionId);

    private async Task<AgentResult?> CallAgentAsync(string method, Dictionary<string, object?> @params)
    {
        var resp = await _agent.ExecuteAsync(new AgentRequest { Method = method, Params = @params });
        return resp.Result;
    }

    private async Task<T?> CallAgentListAsync<T>(string method, Dictionary<string, object?> @params, string key) where T : class
    {
        var result = await CallAgentAsync(method, @params);
        if (result?.Data is null || !result.Data.TryGetValue(key, out var v)) return null;
        if (v is T direct) return direct;
        if (v is JsonElement je) return je.Deserialize<T>();
        return null;
    }

    private static List<string> ExtractStringList(AgentResult? result, string key)
    {
        if (result?.Data is null || !result.Data.TryGetValue(key, out var v)) return [];
        if (v is List<string> direct) return direct;
        if (v is IEnumerable<object> generic) return generic.Select(o => o?.ToString() ?? "").ToList();
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
            return je.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
        return [];
    }

    private static List<string> ParseList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }
    private static Dictionary<string, string> ParseDict(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch { return new(); }
    }
}

public record UpdatePhpRequest(string? PhpVersion = null, List<string>? Extensions = null, Dictionary<string, string>? IniOverrides = null);
