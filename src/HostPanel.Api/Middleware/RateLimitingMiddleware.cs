using System.Collections.Concurrent;
using System.Net;

namespace HostPanel.Api.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private static readonly ConcurrentDictionary<string, ClientRateInfo> _clients = new();

    // Configurable limits
    private const int GeneralRequestsPerMinute = 120;
    private const int AuthRequestsPerMinute = 10;
    private const int ApiKeyRequestsPerMinute = 300;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = context.Request.Path.Value ?? "";
        var isAuth = path.Contains("/auth/login") || path.Contains("/auth/register");
        var isApiKey = context.Request.Headers.Authorization.ToString().StartsWith("Bearer hp_");

        var limit = isAuth ? AuthRequestsPerMinute : isApiKey ? ApiKeyRequestsPerMinute : GeneralRequestsPerMinute;
        var key = isAuth ? $"auth:{ip}" : $"general:{ip}";

        var now = DateTime.UtcNow;
        var client = _clients.GetOrAdd(key, _ => new ClientRateInfo());

        lock (client)
        {
            if (now - client.WindowStart > TimeSpan.FromMinutes(1))
            {
                client.WindowStart = now;
                client.RequestCount = 0;
            }

            client.RequestCount++;

            if (client.RequestCount > limit)
            {
                _logger.LogWarning("Rate limit exceeded for {Key} ({Count}/{Limit})", key, client.RequestCount, limit);
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                context.Response.Headers.RetryAfter = "60";
                return;
            }
        }

        await _next(context);
    }

    // Cleanup old entries periodically
    public static void CleanupStaleEntries()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        foreach (var kv in _clients)
        {
            if (kv.Value.WindowStart < cutoff)
                _clients.TryRemove(kv.Key, out _);
        }
    }

    private class ClientRateInfo
    {
        public DateTime WindowStart = DateTime.UtcNow;
        public int RequestCount;
    }
}
