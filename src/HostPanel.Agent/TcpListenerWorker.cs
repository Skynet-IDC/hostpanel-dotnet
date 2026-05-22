using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using HostPanel.Agent.Services;
using HostPanel.Shared.Tls;

namespace HostPanel.Agent;

// Cross-host TCP listener for the HostPanel agent. Used by the primary panel to
// call its standby's agent during HA configure-failover, replication-lag checks,
// failover, and failback. Disabled by default — enable via Agent:Tcp:Enabled=true.
//
// Security layers (each independent — defense in depth):
//   1. Bound to a specific IP (operator-supplied via Agent:Tcp:BindAddress) so the
//      socket is not exposed on a WAN-facing NIC. Refuses to bind 0.0.0.0; if no
//      bind address is configured, falls back to loopback (fail closed).
//   2. UFW rule installed by the installer restricts source IPs to the LAN subnet.
//   3. Optional mTLS via AgentTlsContext — when Agent:Tcp:Tls:Enabled=true the
//      socket is wrapped in SslStream and the peer must present a cert from the
//      drop-file trust store under /etc/hostpanel/tls/trusted-cas.d/.
//   4. Bearer token in Agent:Token is validated with constant-time compare by the
//      shared AgentSessionProcessor after the TLS handshake completes.
public class TcpListenerWorker : BackgroundService
{
    private readonly ILogger<TcpListenerWorker> _logger;
    private readonly AgentSessionProcessor _sessionProcessor;
    private readonly AgentTlsContext _tls;
    private readonly bool _enabled;
    private readonly IPAddress _bindAddress;
    private readonly int _port;

    public TcpListenerWorker(
        ILogger<TcpListenerWorker> logger,
        AgentSessionProcessor sessionProcessor,
        AgentTlsContext tls,
        IConfiguration config)
    {
        _logger = logger;
        _sessionProcessor = sessionProcessor;
        _tls = tls;
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

        _logger.LogInformation("Agent TCP listener accepting connections on {Endpoint} (mTLS {Tls})",
            endpoint, _tls.Enabled ? "enabled" : "disabled");

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
        using var rawStream = new NetworkStream(clientSocket, ownsSocket: true);
        Stream sessionStream = rawStream;
        SslStream? ssl = null;

        try
        {
            if (_tls.Enabled && _tls.Identity is not null)
            {
                ssl = new SslStream(rawStream, leaveInnerStreamOpen: false, _tls.BuildValidationCallback());
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _tls.Identity,
                    ClientCertificateRequired = true,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                    CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
                }, cancellationToken);
                sessionStream = ssl;
            }

            await _sessionProcessor.HandleAsync(sessionStream, cancellationToken);
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning("Rejected TCP peer at mTLS handshake: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling TCP client");
        }
        finally
        {
            ssl?.Dispose();
        }
    }
}
