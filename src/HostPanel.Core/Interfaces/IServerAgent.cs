using HostPanel.Core.DTOs;

namespace HostPanel.Core.Interfaces;

public interface IServerAgent
{
    Task<AgentResponse> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
