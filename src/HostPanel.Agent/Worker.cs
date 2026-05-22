using System.Net.Sockets;
using HostPanel.Agent.Services;

namespace HostPanel.Agent;

// Unix-socket listener for the local panel-to-agent control plane. Most calls go
// through this socket (API process talks to its co-located agent as root over a
// www-data-group-readable socket). Per-connection processing is delegated to the
// shared AgentSessionProcessor so the cross-host TcpListenerWorker uses the same
// auth + dispatch path.
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TokenAuthService _tokenAuth;
    private readonly AgentSessionProcessor _sessionProcessor;
    private readonly string _socketPath;

    public Worker(
        ILogger<Worker> logger,
        TokenAuthService tokenAuth,
        AgentSessionProcessor sessionProcessor,
        IConfiguration config)
    {
        _logger = logger;
        _tokenAuth = tokenAuth;
        _sessionProcessor = sessionProcessor;
        _socketPath = config["Agent:SocketPath"] ?? "/var/run/hostpanel/agent.sock";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var socketDir = Path.GetDirectoryName(_socketPath);
        if (socketDir is not null && !Directory.Exists(socketDir))
            Directory.CreateDirectory(socketDir);

        if (File.Exists(_socketPath))
            File.Delete(_socketPath);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        listener.Listen(10);

        // Set socket permissions so www-data (API process) can connect
        try
        {
            File.SetUnixFileMode(_socketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite);

            using var chgrp = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("/bin/chgrp", $"www-data {_socketPath}")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            chgrp?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not set socket permissions (non-Linux?)");
        }

        _logger.LogInformation("Agent listening on {SocketPath}", _socketPath);
        _logger.LogInformation("Agent token loaded ({Length} chars)", _tokenAuth.GetToken().Length);

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
                _logger.LogError(ex, "Error accepting connection");
            }
        }

        if (File.Exists(_socketPath))
            File.Delete(_socketPath);
    }

    private async Task HandleClientAsync(Socket clientSocket, CancellationToken cancellationToken)
    {
        using var stream = new NetworkStream(clientSocket, ownsSocket: true);
        await _sessionProcessor.HandleAsync(stream, cancellationToken);
    }
}
