using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;

namespace HostPanel.Infrastructure.Services;

/// <summary>
/// Communicates with a HostPanel Agent on a remote server via TCP + TLS.
/// For production, replace with gRPC + mTLS.
/// </summary>
public class RemoteServerAgent : IServerAgent
{
    private readonly Server _server;
    private readonly ILogger<RemoteServerAgent> _logger;

    public RemoteServerAgent(Server server, ILogger<RemoteServerAgent> logger)
    {
        _server = server;
        _logger = logger;
    }

    public async Task<AgentResponse> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        request.Token = _server.AgentToken ?? "";

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_server.IpAddress, _server.AgentPort, cancellationToken);

            Stream connStream = client.GetStream();

            // Use mTLS if certificates are configured
            if (!string.IsNullOrEmpty(_server.TlsCertificatePath) && File.Exists(_server.TlsCertificatePath))
            {
                var sslStream = new SslStream(connStream, false, (sender, cert, chain, errors) =>
                {
                    // In production: validate server cert against known CA
                    return errors == SslPolicyErrors.None || errors == SslPolicyErrors.RemoteCertificateChainErrors;
                });

                var clientCert = new X509Certificate2(_server.TlsCertificatePath, _server.TlsKeyPath);
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = _server.Hostname,
                    ClientCertificates = new X509CertificateCollection { clientCert },
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                }, cancellationToken);

                connStream = sslStream;
                _logger.LogDebug("mTLS connection established to {Host}", _server.Hostname);
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
