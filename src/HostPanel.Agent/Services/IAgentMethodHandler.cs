using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public interface IAgentMethodHandler
{
    string Method { get; }
    Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken cancellationToken);
}
