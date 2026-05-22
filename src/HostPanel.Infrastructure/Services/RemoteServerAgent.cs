using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Shared.Tls;

namespace HostPanel.Infrastructure.Services;

// Communicates with a HostPanel Agent on a remote server over TCP.
// Optionally upgrades the connection to mTLS via AgentTlsContext — when enabled,
// both sides present a cert from the configured trust store, then the bearer
// token is validated as a second factor on the agent side.
public class RemoteServerAgent : IServerAgent
{
    private readonly Server _server;
    private readonly ILogger<RemoteServerAgent> _logger;
    private readonly AgentTlsContext _tls;

    public RemoteServerAgent(Server server, ILogger<RemoteServerAgent> logger, AgentTlsContext tls)
    {
        _server = server;
        _logger = logger;
        _tls = tls;
    }

    public async Task<AgentResponse> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        request.Token = _server.AgentToken ?? "";

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_server.IpAddress, _server.AgentPort, cancellationToken);

            Stream connStream = client.GetStream();

            if (_tls.Enabled && _tls.Identity is not null)
            {
                var sslStream = new SslStream(connStream, leaveInnerStreamOpen: false, _tls.BuildValidationCallback());
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    // Peers are addressed by IP and certs carry SAN=ip, not DNS, so the
                    // TargetHost field is informational only — the validation callback is what gates.
                    TargetHost = _server.IpAddress,
                    ClientCertificates = new System.Security.Cryptography.X509Certificates.X509CertificateCollection { _tls.Identity },
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                }, cancellationToken);
                connStream = sslStream;
                _logger.LogDebug("mTLS established with {Host}:{Port}", _server.IpAddress, _server.AgentPort);
            }

            using var writer = new StreamWriter(connStream, Encoding.UTF8) { AutoFlush = true };
            using var reader = new StreamReader(connStream, Encoding.UTF8);

            var json = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);

            var responseLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(responseLine))
                return ErrorResponse(request, "Empty response from remote agent");

            return JsonSerializer.Deserialize<AgentResponse>(responseLine)
                ?? ErrorResponse(request, "Failed to parse remote agent response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to communicate with remote agent at {Host}:{Port}",
                _server.IpAddress, _server.AgentPort);
            return ErrorResponse(request, $"Remote agent error: {ex.Message}");
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await ExecuteAsync(new AgentRequest
            {
                Method = AgentMethods.SystemGetStats,
                Params = []
            }, cancellationToken);
            return response.IsSuccess;
        }
        catch { return false; }
    }

    private static AgentResponse ErrorResponse(AgentRequest request, string message) => new()
    {
        Id = request.Id,
        TraceId = request.TraceId,
        Error = new AgentError { Code = -32000, Message = message }
    };
}
