using System.Text;
using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

// Per-connection request processor shared between the Unix-socket listener (Worker)
// and the TCP listener (TcpListenerWorker). Owns one request/response exchange:
// reads one JSON line from the stream, dispatches to a method handler, writes one
// JSON line back. Does NOT own the stream — the caller is responsible for disposal.
public class AgentSessionProcessor
{
    private readonly ILogger<AgentSessionProcessor> _logger;
    private readonly TokenAuthService _tokenAuth;
    private readonly MethodRegistry _methodRegistry;

    public AgentSessionProcessor(
        ILogger<AgentSessionProcessor> logger,
        TokenAuthService tokenAuth,
        MethodRegistry methodRegistry)
    {
        _logger = logger;
        _tokenAuth = tokenAuth;
        _methodRegistry = methodRegistry;
    }

    public async Task HandleAsync(Stream stream, CancellationToken cancellationToken)
    {
        // leaveOpen so the caller's `using` on the underlying NetworkStream is the
        // single disposer — disposing the stream twice is harmless but disposing
        // through the reader before the writer finishes is not.
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        try
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                return;

            var request = JsonSerializer.Deserialize<AgentRequest>(line);
            if (request is null)
            {
                await WriteResponse(writer, CreateError("", null, -32700, "Parse error"), cancellationToken);
                return;
            }

            var response = await ProcessRequest(request, cancellationToken);
            await WriteResponse(writer, response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client request");
        }
    }

    private async Task<AgentResponse> ProcessRequest(AgentRequest request, CancellationToken cancellationToken)
    {
        if (!_tokenAuth.ValidateToken(request.Token))
        {
            _logger.LogWarning("Unauthorized request for method {Method}, traceId={TraceId}", request.Method, request.TraceId);
            return CreateError(request.Id, request.TraceId, -32001, "Unauthorized");
        }

        if (!_methodRegistry.IsMethodAllowed(request.Method))
        {
            _logger.LogWarning("Unknown method {Method}, traceId={TraceId}", request.Method, request.TraceId);
            return CreateError(request.Id, request.TraceId, -32601, $"Method not found: {request.Method}");
        }

        var handler = _methodRegistry.GetHandler(request.Method);
        if (handler is null)
        {
            _logger.LogWarning("No handler registered for {Method}, traceId={TraceId}", request.Method, request.TraceId);
            return CreateError(request.Id, request.TraceId, -32601, $"Method not implemented: {request.Method}");
        }

        _logger.LogInformation("Executing {Method}, traceId={TraceId}", request.Method, request.TraceId);

        try
        {
            var result = await handler.HandleAsync(request.Params, cancellationToken);
            return new AgentResponse
            {
                Id = request.Id,
                TraceId = request.TraceId,
                Result = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing {Method}, traceId={TraceId}", request.Method, request.TraceId);
            return CreateError(request.Id, request.TraceId, -32000, ex.Message);
        }
    }

    private static AgentResponse CreateError(string id, string? traceId, int code, string message) => new()
    {
        Id = id,
        TraceId = traceId,
        Error = new AgentError { Code = code, Message = message }
    };

    private static async Task WriteResponse(StreamWriter writer, AgentResponse response, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
    }
}
