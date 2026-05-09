using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class HaAddFloatingIpHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaAddFloatingIp;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var ip = GetStr(parameters, "ip");
        var iface = GetStr(parameters, "interface") ?? "eth0";

        if (string.IsNullOrEmpty(ip))
            return new AgentResult { Success = false, Data = new() { ["error"] = "ip required" } };

        // Add virtual IP to interface
        var result = await ProcessRunner.RunAsync("/usr/sbin/ip",
            ["addr", "add", $"{ip}/32", "dev", iface], ct);

        return new AgentResult
        {
            Success = result.ExitCode == 0 || result.StdErr.Contains("RTNETLINK answers: File exists"),
            Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class HaRemoveFloatingIpHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaRemoveFloatingIp;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var ip = GetStr(parameters, "ip");
        var iface = GetStr(parameters, "interface") ?? "eth0";

        if (string.IsNullOrEmpty(ip))
            return new AgentResult { Success = false, Data = new() { ["error"] = "ip required" } };

        var result = await ProcessRunner.RunAsync("/usr/sbin/ip",
            ["addr", "del", $"{ip}/32", "dev", iface], ct);

        return new AgentResult
        {
            Success = result.ExitCode == 0 || result.StdErr.Contains("Cannot assign"),
            Data = new() { ["output"] = result.StdOut }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class HaSendGratuitousArpHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaSendGratuitousArp;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var ip = GetStr(parameters, "ip");
        var iface = GetStr(parameters, "interface") ?? "eth0";

        if (string.IsNullOrEmpty(ip))
            return new AgentResult { Success = false, Data = new() { ["error"] = "ip required" } };

        // Send gratuitous ARP to update switch/router MAC tables
        var result = await ProcessRunner.RunAsync("/usr/bin/arping",
            ["-U", "-c", "3", "-I", iface, ip], ct);

        return new AgentResult
        {
            Success = true, // arping may return non-zero even on success
            Data = new() { ["output"] = result.StdOut }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}
