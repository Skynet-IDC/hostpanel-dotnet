using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class MethodRegistry
{
    private readonly Dictionary<string, IAgentMethodHandler> _handlers = [];
    private readonly ILogger<MethodRegistry> _logger;

    public MethodRegistry(IEnumerable<IAgentMethodHandler> handlers, ILogger<MethodRegistry> logger)
    {
        _logger = logger;
        foreach (var handler in handlers)
        {
            if (AgentMethods.AllMethods.Contains(handler.Method))
            {
                _handlers[handler.Method] = handler;
                _logger.LogInformation("Registered agent method: {Method}", handler.Method);
            }
            else
            {
                _logger.LogWarning("Rejected unknown method registration: {Method}", handler.Method);
            }
        }
    }

    public IAgentMethodHandler? GetHandler(string method)
    {
        _handlers.TryGetValue(method, out var handler);
        return handler;
    }

    public bool IsMethodAllowed(string method) => AgentMethods.AllMethods.Contains(method);
}
