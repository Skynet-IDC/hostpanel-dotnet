using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;

namespace HostPanel.Infrastructure.Services;

public class LocalServerAgent : IServerAgent
{
    private readonly string _socketPath;
    private readonly string _token;
    private readonly ILogger<LocalServerAgent> _logger;

    public LocalServerAgent(IConfiguration config, ILogger<LocalServerAgent> logger)
    {
        _logger = logger;
        _socketPath = config["Agent:SocketPath"] ?? "/var/run/hostpanel/agent.sock";
        _token = config["Agent:Token"] ?? "";
    }

    public async Task<AgentResponse> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        request.Token = _token;

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), cancellationToken);

            using var stream = new NetworkStream(socket, ownsSocket: false);
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var json = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);

            var responseLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return new AgentResponse
                {
                    Id = request.Id,
                    Error = new AgentError { Code = -32000, Message = "Empty response from agent" }
                };
            }

            return JsonSerializer.Deserialize<AgentResponse>(responseLine)
                ?? new AgentResponse
                {
                    Id = request.Id,
                    Error = new AgentError { Code = -32000, Message = "Failed to parse agent response" }
                };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to communicate with agent at {SocketPath}", _socketPath);
            return new AgentResponse
            {
                Id = request.Id,
                TraceId = request.TraceId,
                Error = new AgentError { Code = -32000, Message = $"Agent communication error: {ex.Message}" }
            };
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
        catch
        {
            return false;
        }
    }
}
