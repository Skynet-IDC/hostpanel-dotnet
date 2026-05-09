using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class SystemCheckUpdatesHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemCheckUpdates;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("/usr/bin/apt-get",
            ["update", "-qq"], ct);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["output"] = result.StdOut }
        };
    }
}

public class SystemListUpdatesHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemListUpdates;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("/usr/bin/apt",
            ["list", "--upgradable"], ct);

        var lines = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith("Listing"))
            .Select(l =>
            {
                var parts = l.Split('/');
                var name = parts.Length > 0 ? parts[0] : l;
                return new { package_name = name, full = l.Trim() };
            })
            .ToList();

        return new AgentResult
        {
            Success = true,
            Data = new()
            {
                ["count"] = lines.Count,
                ["packages"] = JsonSerializer.Serialize(lines)
            }
        };
    }
}

public class SystemInstallUpdatesHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemInstallUpdates;
    private readonly ILogger<SystemInstallUpdatesHandler> _logger;

    public SystemInstallUpdatesHandler(ILogger<SystemInstallUpdatesHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var securityOnly = parameters.TryGetValue("securityOnly", out var so) && so is true;
        var packages = parameters.TryGetValue("packages", out var pkgs) ? pkgs : null;

        List<string> args = ["upgrade", "-y", "-qq"];

        if (securityOnly)
        {
            args = ["upgrade", "-y", "-qq", "-o", "Dir::Etc::SourceList=/etc/apt/sources.list.d/security.list"];
        }

        if (packages is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var packageList = je.EnumerateArray().Select(p => p.GetString()!).Where(p => p is not null).ToList();
            if (packageList.Count > 0)
            {
                args = ["install", "-y", "-qq", .. packageList!];
            }
        }

        _logger.LogInformation("Installing updates with args: {Args}", string.Join(" ", args));

        var result = await ProcessRunner.RunAsync("/usr/bin/apt-get", args.ToArray(), ct, timeoutMs: 300_000);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new()
            {
                ["output"] = result.StdOut,
                ["error"] = result.StdErr
            }
        };
    }
}

public class SystemUpdateHistoryHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemUpdateHistory;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        // Read apt history log
        var logPath = "/var/log/apt/history.log";
        if (!File.Exists(logPath))
            return new AgentResult { Success = true, Data = new() { ["entries"] = "[]" } };

        var content = await File.ReadAllTextAsync(logPath, ct);
        var entries = content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(20)
            .Select(e => e.Trim())
            .ToList();

        return new AgentResult
        {
            Success = true,
            Data = new() { ["entries"] = JsonSerializer.Serialize(entries) }
        };
    }
}
