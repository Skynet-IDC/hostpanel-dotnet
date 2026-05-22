using Microsoft.Extensions.Logging;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Shared.Tls;

namespace HostPanel.Infrastructure.Services;

// Creates the appropriate IServerAgent for a given server.
// Local server uses Unix socket (LocalServerAgent), remote servers use TCP
// (RemoteServerAgent) optionally wrapped in mTLS via AgentTlsContext.
public class ServerAgentFactory
{
    private readonly IServerAgent _localAgent;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AgentTlsContext _tls;

    public ServerAgentFactory(IServerAgent localAgent, ILoggerFactory loggerFactory, AgentTlsContext tls)
    {
        _localAgent = localAgent;
        _loggerFactory = loggerFactory;
        _tls = tls;
    }

    public IServerAgent GetAgent(Server server)
    {
        if (server.IsLocal)
            return _localAgent;

        return new RemoteServerAgent(server,
            _loggerFactory.CreateLogger<RemoteServerAgent>(),
            _tls);
    }
}
