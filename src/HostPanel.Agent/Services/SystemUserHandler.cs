using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class SystemCreateUserHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemCreateUser;
    private readonly ILogger<SystemCreateUserHandler> _logger;

    public SystemCreateUserHandler(ILogger<SystemCreateUserHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var username = GetString(parameters, "username");
        var homeDir = GetString(parameters, "homeDir") ?? $"/var/www/{username}";

        if (string.IsNullOrEmpty(username))
            return new AgentResult { Success = false, Data = new() { ["error"] = "username is required" } };

        // Validate username (alphanumeric + underscore only)
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z_][a-zA-Z0-9_]{0,31}$"))
            return new AgentResult { Success = false, Data = new() { ["error"] = "Invalid username format" } };

        _logger.LogInformation("Creating system user {Username} with home {Home}", username, homeDir);

        // Create user with home directory
        var result = await ProcessRunner.RunAsync("/usr/sbin/useradd",
            ["-m", "-d", homeDir, "-s", "/bin/bash", username], cancellationToken);

        if (result.ExitCode != 0 && !result.StdErr.Contains("already exists"))
            return new AgentResult { Success = false, Data = new() { ["error"] = result.StdErr } };

        // Create standard directory structure
        var dirs = new[] { "httpdocs", "logs", "tmp", "domains" };
        foreach (var dir in dirs)
        {
            var path = Path.Combine(homeDir, dir);
            Directory.CreateDirectory(path);
        }

        // Set ownership
        await ProcessRunner.RunAsync("/bin/chown", ["-R", $"{username}:{username}", homeDir], cancellationToken);

        // Add www-data to this tenant's group so nginx can traverse the home dir (drwxr-x---)
        // and serve files from httpdocs/. Without this, requests 13 (Permission denied) on realpath().
        await ProcessRunner.RunAsync("/usr/sbin/usermod", ["-aG", username, "www-data"], cancellationToken);

        // chmod 2775 on httpdocs so www-data (now in the tenant's group) can write the ACME
        // HTTP-01 challenge file. setgid (2xxx) makes new files inherit the tenant's group,
        // keeping ownership consistent if the user later uploads files via FTP/SFTP.
        var httpdocsDir = Path.Combine(homeDir, "httpdocs");
        await ProcessRunner.RunAsync("/bin/chmod", ["2775", httpdocsDir], cancellationToken);

        // Restart php-fpm so running workers pick up the new group membership (usermod -aG
        // doesn't refresh running processes). Without this, the first request to the new
        // tenant's docroot returns "Primary script unknown" until FPM is manually restarted.
        // We restart every installed FPM version (idempotent if a version isn't installed).
        foreach (var v in new[] { "8.1", "8.2", "8.3" })
        {
            await ProcessRunner.RunAsync("/usr/bin/systemctl",
                ["restart", $"php{v}-fpm"], cancellationToken);
        }

        // Create default index.html (owned by the tenant so the user can replace it)
        var indexPath = Path.Combine(homeDir, "httpdocs", "index.html");
        if (!File.Exists(indexPath))
        {
            await File.WriteAllTextAsync(indexPath,
                $"<html><body><h1>Welcome</h1><p>Site for {username} is being set up.</p></body></html>",
                cancellationToken);
            await ProcessRunner.RunAsync("/bin/chown", [$"{username}:{username}", indexPath], cancellationToken);
        }

        return new AgentResult
        {
            Success = true,
            Data = new() { ["username"] = username, ["homeDir"] = homeDir }
        };
    }

    private static string? GetString(Dictionary<string, object?> p, string key) =>
        p.TryGetValue(key, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class SystemDeleteUserHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemDeleteUser;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var username = parameters.TryGetValue("username", out var v) ? v?.ToString() : null;
        if (string.IsNullOrEmpty(username))
            return new AgentResult { Success = false, Data = new() { ["error"] = "username is required" } };

        // Remove user and home directory
        var result = await ProcessRunner.RunAsync("/usr/sbin/userdel", ["-r", username], cancellationToken);

        if (result.ExitCode != 0 && !result.StdErr.Contains("does not exist"))
            return new AgentResult { Success = false, Data = new() { ["error"] = result.StdErr } };

        return new AgentResult { Success = true };
    }
}

public class SystemSetQuotaHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemSetQuota;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var username = parameters.TryGetValue("username", out var v) ? v?.ToString() : null;
        if (string.IsNullOrEmpty(username))
            return new AgentResult { Success = false, Data = new() { ["error"] = "username is required" } };

        // Set disk quota using setquota (requires quota support on filesystem)
        var diskQuotaMb = parameters.TryGetValue("diskQuotaMb", out var qv) ? qv?.ToString() : "1024";
        var blocks = long.Parse(diskQuotaMb ?? "1024") * 1024; // Convert MB to KB blocks

        var result = await ProcessRunner.RunAsync("/usr/sbin/setquota",
            ["-u", username, "0", blocks.ToString(), "0", "0", "/"], cancellationToken);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
        };
    }
}
