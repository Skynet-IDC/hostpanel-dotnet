using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;

namespace HostPanel.Infrastructure.Services;

/// <summary>
/// Creates the appropriate IServerAgent for a given server.
/// Local server uses Unix socket, remote servers use TCP.
/// </summary>
public class ServerAgentFactory
{
    private readonly IServerAgent _localAgent;
    private readonly ILoggerFactory _loggerFactory;

    public ServerAgentFactory(IServerAgent localAgent, ILoggerFactory loggerFactory)
    {
        _localAgent = localAgent;
        _loggerFactory = loggerFactory;
    }

    public IServerAgent GetAgent(Server server)
    {
        if (server.IsLocal)
            return _localAgent;

        return new RemoteServerAgent(server,
            _loggerFactory.CreateLogger<RemoteServerAgent>());
    }
}
