using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class FirewallStatusHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.FirewallStatus;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("/usr/sbin/ufw", ["status", "numbered"], ct);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new()
            {
                ["rules"] = result.StdOut,
                ["error"] = result.StdErr
            }
        };
    }
}
