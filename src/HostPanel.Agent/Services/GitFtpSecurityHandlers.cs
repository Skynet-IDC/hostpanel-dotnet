using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

// --- Git Deploy ---
public class GitDeployHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.GitDeploy;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var repoUrl = GetStr(parameters, "repoUrl");
        var branch = GetStr(parameters, "branch") ?? "main";
        var deployPath = GetStr(parameters, "deployPath");
        var username = GetStr(parameters, "username");

        if (string.IsNullOrEmpty(repoUrl) || string.IsNullOrEmpty(deployPath))
            return Fail("repoUrl and deployPath required");

        // Validate URL scheme — only allow https:// and git:// (block file://, ssh://, etc.)
        if (!repoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !repoUrl.StartsWith("git://", StringComparison.OrdinalIgnoreCase) &&
            !repoUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            return Fail("Only https://, git://, and git@ repository URLs are allowed");

        // Validate deploy path is under /var/www/
        if (!deployPath.StartsWith("/var/www/"))
            return Fail("deployPath must be under /var/www/");

        ProcessResult result;
        if (Directory.Exists(Path.Combine(deployPath, ".git")))
        {
            // Pull existing repo
            result = await ProcessRunner.RunAsync("/usr/bin/git",
                ["-C", deployPath, "pull", "origin", branch], ct, timeoutMs: 120_000);
        }
        else
        {
            // Fresh clone
            result = await ProcessRunner.RunAsync("/usr/bin/git",
                ["clone", "--branch", branch, "--single-branch", repoUrl, deployPath], ct, timeoutMs: 120_000);
        }

        // Get latest commit hash
        var hashResult = await ProcessRunner.RunAsync("/usr/bin/git",
            ["-C", deployPath, "rev-parse", "HEAD"], ct);

        if (!string.IsNullOrEmpty(username))
            await ProcessRunner.RunAsync("/bin/chown", ["-R", $"{username}:{username}", deployPath], ct);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new()
            {
                ["commitHash"] = hashResult.StdOut.Trim(),
                ["output"] = result.StdOut,
                ["error"] = result.StdErr
            }
        };
    }

    private static AgentResult Fail(string msg) => new() { Success = false, Data = new() { ["error"] = msg } };
    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

// --- FTP ---
public class FtpCreateAccountHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.FtpCreateAccount;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var username = GetStr(parameters, "username");
        var password = GetStr(parameters, "password");
        var homeDir = GetStr(parameters, "homeDir");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return Fail("username and password required");

        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z_][a-zA-Z0-9_]{0,31}$"))
            return Fail("Invalid username format");

        // Create system user with nologin shell for FTP-only access
        await ProcessRunner.RunAsync("/usr/sbin/useradd",
            ["-m", "-d", homeDir ?? $"/var/www/{username}", "-s", "/usr/sbin/nologin", username], ct);

        // Set password via temp file piped to chpasswd (no shell interpolation)
        var tmpPassFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmpPassFile, $"{username}:{password}\n", ct);
            await ProcessRunner.RunAsync("/usr/sbin/chpasswd", [], ct);
            // chpasswd reads from stdin; use input redirection via a wrapper
            var result = await ProcessRunner.RunAsync("/bin/sh",
                ["-c", $"cat {tmpPassFile} | /usr/sbin/chpasswd"], ct);
            if (result.ExitCode != 0)
                return Fail($"Failed to set password: {result.StdErr}");
        }
        finally { try { File.Delete(tmpPassFile); } catch { } }

        return new AgentResult { Success = true, Data = new() { ["username"] = username } };
    }

    private static AgentResult Fail(string msg) => new() { Success = false, Data = new() { ["error"] = msg } };
    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class FtpDeleteAccountHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.FtpDeleteAccount;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var username = parameters.TryGetValue("username", out var v) ? v?.ToString() : null;
        if (string.IsNullOrEmpty(username)) return new AgentResult { Success = false };
        var r = await ProcessRunner.RunAsync("/usr/sbin/userdel", [username], ct);
        return new AgentResult { Success = r.ExitCode == 0 || r.StdErr.Contains("does not exist") };
    }
}

// --- Security / ClamAV / Fail2Ban ---
public class ClamScanHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SecurityClamScan;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var scanPath = GetStr(parameters, "docRoot") ?? "/var/www";

        // Validate scan path is under /var/www/
        if (!scanPath.StartsWith("/var/www"))
            return new AgentResult { Success = false, Data = new() { ["error"] = "Scan path must be under /var/www/" } };

        var result = await ProcessRunner.RunAsync("/usr/bin/clamscan",
            ["-r", "--infected", "--no-summary", scanPath], ct, timeoutMs: 300_000);

        var infected = result.StdOut.Split('\n')
            .Where(l => l.Contains("FOUND"))
            .ToList();

        return new AgentResult
        {
            Success = true,
            Data = new()
            {
                ["infectedCount"] = infected.Count,
                ["infected"] = JsonSerializer.Serialize(infected),
                ["scanPath"] = scanPath
            }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class ClamScanAllHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SecurityClamScanAll;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("/usr/bin/clamscan",
            ["-r", "--infected", "/var/www", "/tmp"], ct, timeoutMs: 600_000);

        return new AgentResult
        {
            Success = result.ExitCode <= 1, // 0=clean, 1=found, 2=error
            Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
        };
    }
}

public class Fail2BanStatusHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SecurityFail2BanStatus;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("/usr/bin/fail2ban-client", ["status"], ct);
        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
        };
    }
}

public class Fail2BanUnbanHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SecurityFail2BanUnban;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var jail = GetStr(parameters, "jail") ?? "sshd";
        var ip = GetStr(parameters, "ip");
        if (string.IsNullOrEmpty(ip)) return new AgentResult { Success = false, Data = new() { ["error"] = "ip required" } };

        var result = await ProcessRunner.RunAsync("/usr/bin/fail2ban-client",
            ["set", jail, "unbanip", ip], ct);
        return new AgentResult { Success = result.ExitCode == 0, Data = new() { ["output"] = result.StdOut } };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}
