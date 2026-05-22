using System.Net;
using System.Net.Sockets;
using HostPanel.Agent.Services;

namespace HostPanel.Agent;

// Cross-host TCP listener for the HostPanel agent. Used by the primary panel to
// call its standby's agent during HA configure-failover, replication-lag checks,
// failover, and failback. Disabled by default — enable via Agent:Tcp:Enabled=true.
//
// Security model:
//   1. Bound to a specific IP (operator-supplied via Agent:Tcp:BindAddress) so the
//      socket is not exposed on a WAN-facing NIC. Refuses to bind 0.0.0.0; if no
//      bind address is configured, falls back to loopback (which makes the listener
//      useless for cross-host calls — that's the intent, fail closed).
//   2. UFW rule installed by the installer restricts source IPs to the LAN subnet.
//   3. Bearer token in Agent:Token is validated with constant-time compare by the
//      shared AgentSessionProcessor.
//
// Future hardening: mTLS via Agent:Tcp:CertificatePath, plus a per-server allowlist
// of caller certs. The Server.IpAddress field on the panel side already supports
// switching to a port matching a TLS-fronted listener.
public class TcpListenerWorker : BackgroundService
{
    private readonly ILogger<TcpListenerWorker> _logger;
    private readonly AgentSessionProcessor _sessionProcessor;
    private readonly bool _enabled;
    private readonly IPAddress _bindAddress;
    private readonly int _port;

    public TcpListenerWorker(
        ILogger<TcpListenerWorker> logger,
        AgentSessionProcessor sessionProcessor,
        IConfiguration config)
    {
        _logger = logger;
        _sessionProcessor = sessionProcessor;
        _enabled = config.GetValue("Agent:Tcp:Enabled", false);
        _port = config.GetValue("Agent:Tcp:Port", 50051);

        var bindStr = config["Agent:Tcp:BindAddress"];
        if (string.IsNullOrWhiteSpace(bindStr))
        {
            _bindAddress = IPAddress.Loopback;
            if (_enabled)
                _logger.LogError("Agent:Tcp:Enabled=true but Agent:Tcp:BindAddress not set — binding loopback (cross-host calls will fail)");
        }
        else if (bindStr == "0.0.0.0")
        {
            _bindAddress = IPAddress.Loopback;
            _logger.LogError("Refusing to bind 0.0.0.0 (would expose agent on every NIC including WAN). Configure Agent:Tcp:BindAddress to a specific LAN IP. Falling back to loopback.");
        }
        else if (!IPAddress.TryParse(bindStr, out var parsed))
        {
            _bindAddress = IPAddress.Loopback;
            _logger.LogError("Agent:Tcp:BindAddress invalid ({Bind}) — falling back to loopback", bindStr);
        }
        else
        {
            _bindAddress = parsed;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Agent TCP listener disabled (Agent:Tcp:Enabled=false)");
            return;
        }

        var endpoint = new IPEndPoint(_bindAddress, _port);
        using var listener = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            listener.Bind(endpoint);
            listener.Listen(10);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bind agent TCP listener on {Endpoint}", endpoint);
            return;
        }

        _logger.LogInformation("Agent TCP listener accepting connections on {Endpoint}", endpoint);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await listener.AcceptAsync(stoppingToken);
                _ = HandleClientAsync(clientSocket, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting TCP connection");
            }
        }
    }

    private async Task HandleClientAsync(Socket clientSocket, CancellationToken cancellationToken)
    {
        using var stream = new NetworkStream(clientSocket, ownsSocket: true);
        await _sessionProcessor.HandleAsync(stream, cancellationToken);
    }
}
