using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/panel")]
[Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
public class PanelUpdateController : ControllerBase
{
    private readonly IServerAgent _agent;
    private readonly IConfiguration _config;

    public PanelUpdateController(IServerAgent agent, IConfiguration config)
    {
        _agent = agent;
        _config = config;
    }

    /// <summary>Get current installed version.</summary>
    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        // Try to read version.json from install dir
        var versionFile = Path.Combine(_config["Panel:InstallDir"] ?? "/opt/hostpanel", "version.json");
        string version = "1.0.0", buildDate = "", channel = "stable", repo = "";

        if (System.IO.File.Exists(versionFile))
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(System.IO.File.ReadAllText(versionFile));
                version = json.TryGetProperty("version", out var v) ? v.GetString() ?? version : version;
                buildDate = json.TryGetProperty("buildDate", out var bd) ? bd.GetString() ?? "" : "";
                channel = json.TryGetProperty("channel", out var ch) ? ch.GetString() ?? "stable" : "stable";
                repo = json.TryGetProperty("repo", out var r) ? r.GetString() ?? "" : "";
            }
            catch { }
        }

        return Ok(new
        {
            version,
            buildDate,
            channel,
            repo,
            dotnetVersion = Environment.Version.ToString(),
            assemblyDate = System.IO.File.GetLastWriteTimeUtc(typeof(PanelUpdateController).Assembly.Location)
        });
    }

    /// <summary>Check if updates are available by comparing local version to latest git tag.</summary>
    [HttpGet("check-update")]
    public async Task<IActionResult> CheckForUpdate()
    {
        var sourceDir = _config["Panel:SourceDir"] ?? "/opt/hostpanel-src";

        // Fetch latest from remote
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.PanelUpdate,
            Params = new Dictionary<string, object?>
            {
                ["sourceDir"] = sourceDir,
                ["checkOnly"] = true
            }
        });

        // Read current and remote version
        var localVersionFile = Path.Combine(_config["Panel:InstallDir"] ?? "/opt/hostpanel", "version.json");
        var remoteVersionFile = Path.Combine(sourceDir, "version.json");

        var localVersion = "0.0.0";
        var remoteVersion = "0.0.0";

        if (System.IO.File.Exists(localVersionFile))
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(System.IO.File.ReadAllText(localVersionFile));
                localVersion = json.GetProperty("version").GetString() ?? localVersion;
            }
            catch { }
        }

        if (System.IO.File.Exists(remoteVersionFile))
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(System.IO.File.ReadAllText(remoteVersionFile));
                remoteVersion = json.GetProperty("version").GetString() ?? remoteVersion;
            }
            catch { }
        }

        // Compare versions properly — update only if remote > local
        var updateAvailable = false;
        var sourceExists = System.IO.Directory.Exists(sourceDir);
        try
        {
            if (sourceExists && remoteVersion != "0.0.0")
                updateAvailable = new Version(remoteVersion) > new Version(localVersion);
        }
        catch { updateAvailable = remoteVersion != localVersion && remoteVersion != "0.0.0"; }

        return Ok(new
        {
            currentVersion = localVersion,
            latestVersion = remoteVersion,
            updateAvailable,
            sourceExists,
            sourceDir
        });
    }

    /// <summary>
    /// Full update pipeline: backup → git pull → npm build → dotnet publish → migrate → restart → health check.
    /// Auto-rollback on failure.
    /// </summary>
    [HttpPost("update")]
    public async Task<IActionResult> Update([FromBody] PanelUpdateRequest? request = null)
    {
        var sourceDir = request?.SourceDir ?? _config["Panel:SourceDir"] ?? "/opt/hostpanel-src";
        var installDir = _config["Panel:InstallDir"] ?? "/opt/hostpanel";

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.PanelUpdate,
            Params = new Dictionary<string, object?>
            {
                ["sourceDir"] = sourceDir,
                ["installDir"] = installDir,
                ["branch"] = request?.Branch ?? "main",
                ["backupDb"] = request?.BackupDatabase ?? true
            }
        });

        return Ok(new
        {
            success = result.IsSuccess,
            version = result.Result?.Data?.TryGetValue("version", out var v) == true ? v : null,
            log = result.Result?.Data?.TryGetValue("log", out var l) == true ? l : null,
            error = result.Error?.Message
        });
    }

    /// <summary>Rollback to previous version. Restores backup and restarts.</summary>
    [HttpPost("rollback")]
    public async Task<IActionResult> Rollback()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.PanelRollback,
            Params = []
        });

        return Ok(new { success = result.IsSuccess, data = result.Result?.Data, error = result.Error?.Message });
    }
}

public record PanelUpdateRequest(string? SourceDir = null, string? Branch = null, bool BackupDatabase = true);
