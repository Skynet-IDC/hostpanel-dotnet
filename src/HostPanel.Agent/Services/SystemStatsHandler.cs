using System.Diagnostics;
using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class SystemStatsHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemGetStats;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, object?>
        {
            ["hostname"] = Environment.MachineName,
            ["osVersion"] = Environment.OSVersion.ToString(),
            ["processorCount"] = Environment.ProcessorCount,
            ["totalMemoryMb"] = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024),
            ["uptimeSeconds"] = Environment.TickCount64 / 1000
        };

        // Per-user disk usage if username provided
        var username = parameters.TryGetValue("username", out var u) ? u?.ToString() ?? (u is JsonElement je ? je.GetString() : null) : null;
        if (!string.IsNullOrEmpty(username))
        {
            var homeDir = $"/var/www/{username}";
            if (Directory.Exists(homeDir))
            {
                try
                {
                    var result = await ProcessRunner.RunAsync("/usr/bin/du",
                        ["-sb", homeDir], cancellationToken, timeoutMs: 15_000);

                    if (result.ExitCode == 0)
                    {
                        var parts = result.StdOut.Trim().Split('\t');
                        if (parts.Length > 0 && long.TryParse(parts[0], out var bytes))
                            data["diskUsageBytes"] = bytes;
                    }
                }
                catch { /* Best effort */ }
            }
        }

        return new AgentResult { Success = true, Data = data };
    }
}
