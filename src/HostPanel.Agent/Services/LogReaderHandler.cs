using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class LogReaderHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemReadLog;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var path = parameters.TryGetValue("path", out var p) ? p?.ToString() ?? (p is JsonElement je ? je.GetString() : null) : null;
        var linesParam = parameters.TryGetValue("lines", out var l) ? l : null;
        var lines = 100;
        if (linesParam is JsonElement jl && jl.TryGetInt32(out var li)) lines = li;
        else if (linesParam is int i) lines = i;

        if (string.IsNullOrEmpty(path))
            return new AgentResult { Success = false, Data = new() { ["error"] = "path required" } };

        // Only allow reading from known log directories
        var allowedPrefixes = new[] { "/var/log/nginx/", "/var/log/mail", "/var/log/hostpanel/" };
        if (!allowedPrefixes.Any(prefix => path.StartsWith(prefix)))
            return new AgentResult { Success = false, Data = new() { ["error"] = "Path not in allowed log directories" } };

        if (!File.Exists(path))
            return new AgentResult { Success = true, Data = new() { ["lines"] = "[]", ["exists"] = false } };

        var allLines = File.ReadLines(path).TakeLast(Math.Min(lines, 1000)).ToList();

        return new AgentResult
        {
            Success = true,
            Data = new()
            {
                ["lines"] = JsonSerializer.Serialize(allLines),
                ["exists"] = true,
                ["totalLines"] = allLines.Count
            }
        };
    }
}
