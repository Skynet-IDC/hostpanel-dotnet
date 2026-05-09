using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class FirewallAddRuleHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.FirewallAddRule;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("args", out var argsObj))
            return new AgentResult { Success = false, Data = new() { ["error"] = "args required" } };

        var args = new List<string>();
        if (argsObj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in je.EnumerateArray())
                if (item.GetString() is string s) args.Add(s);
        }

        if (args.Count == 0)
            return new AgentResult { Success = false, Data = new() { ["error"] = "Empty args" } };

        var result = await ProcessRunner.RunAsync("/usr/sbin/ufw", args.ToArray(), ct);
        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
        };
    }
}

public class FirewallDeleteRuleHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.FirewallDeleteRule;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var ruleNumber = parameters.TryGetValue("ruleNumber", out var r) ? r?.ToString() : null;
        if (string.IsNullOrEmpty(ruleNumber))
            return new AgentResult { Success = false, Data = new() { ["error"] = "ruleNumber required" } };

        var result = await ProcessRunner.RunAsync("/usr/sbin/ufw",
            ["--force", "delete", ruleNumber], ct);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
        };
    }
}

public class FirewallEnableHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.FirewallEnable;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("/usr/sbin/ufw", ["--force", "enable"], ct);
        return new AgentResult { Success = result.ExitCode == 0, Data = new() { ["output"] = result.StdOut } };
    }
}

public class FirewallDisableHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.FirewallDisable;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("/usr/sbin/ufw", ["disable"], ct);
        return new AgentResult { Success = result.ExitCode == 0, Data = new() { ["output"] = result.StdOut } };
    }
}
