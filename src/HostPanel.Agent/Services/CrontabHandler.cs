using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class CrontabHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemSetCrontab;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var username = parameters.TryGetValue("username", out var u) ? u?.ToString() : null;
        string? content = null;
        if (parameters.TryGetValue("content", out var c))
            content = c is JsonElement je
                ? (je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString())
                : c?.ToString();

        if (string.IsNullOrEmpty(username))
            return new AgentResult { Success = false, Data = new() { ["error"] = "username required" } };

        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z_][a-zA-Z0-9_]{0,31}$"))
            return new AgentResult { Success = false, Data = new() { ["error"] = "Invalid username" } };

        // Write to temp file, then install via crontab binary
        var tmpFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmpFile, content ?? "", ct);
            var result = await ProcessRunner.RunAsync("/usr/bin/crontab",
                ["-u", username, tmpFile], ct);

            return new AgentResult
            {
                Success = result.ExitCode == 0,
                Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
            };
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }
}
