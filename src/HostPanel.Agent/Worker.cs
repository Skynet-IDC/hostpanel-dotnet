using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HostPanel.Agent.Services;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TokenAuthService _tokenAuth;
    private readonly MethodRegistry _methodRegistry;
    private readonly string _socketPath;

    public Worker(
        ILogger<Worker> logger,
        TokenAuthService tokenAuth,
        MethodRegistry methodRegistry,
        IConfiguration config)
    {
        _logger = logger;
        _tokenAuth = tokenAuth;
        _methodRegistry = methodRegistry;
        _socketPath = config["Agent:SocketPath"] ?? "/var/run/hostpanel/agent.sock";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure socket directory exists
        var socketDir = Path.GetDirectoryName(_socketPath);
        if (socketDir is not null && !Directory.Exists(socketDir))
            Directory.CreateDirectory(socketDir);

        // Remove stale socket file
        if (File.Exists(_socketPath))
            File.Delete(_socketPath);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        listener.Listen(10);

        // Restrict socket to www-data group (API process user)
        try
        {
            File.SetUnixFileMode(_socketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
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

        // Cleanup
        if (File.Exists(_socketPath))
            File.Delete(_socketPath);
    }

    private async Task HandleClientAsync(Socket clientSocket, CancellationToken cancellationToken)
    {
        using var stream = new NetworkStream(clientSocket, ownsSocket: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

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
        // Auth check
        if (!_tokenAuth.ValidateToken(request.Token))
        {
            _logger.LogWarning("Unauthorized request for method {Method}, traceId={TraceId}", request.Method, request.TraceId);
            return CreateError(request.Id, request.TraceId, -32001, "Unauthorized");
        }

        // Method allowlist check
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
