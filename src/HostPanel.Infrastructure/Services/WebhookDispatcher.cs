using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Infrastructure.Services;

/// <summary>
/// Dispatches webhook events to registered endpoints (n8n, Zapier, custom).
/// Signs payloads with HMAC-SHA256 for verification.
/// </summary>
public class WebhookDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _http;
    private readonly ILogger<WebhookDispatcher> _logger;

    public WebhookDispatcher(IServiceScopeFactory scopeFactory, IHttpClientFactory httpFactory, ILogger<WebhookDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _http = httpFactory.CreateClient("Webhooks");
        _http.Timeout = TimeSpan.FromSeconds(10);
        _logger = logger;
    }

    /// <summary>
    /// Fire a webhook event. Delivers to all matching registered endpoints.
    /// Call this from anywhere in the application when events occur.
    /// </summary>
    public async Task FireAsync(string eventType, object payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var endpoints = await db.Set<WebhookEndpoint>()
            .Where(w => w.IsActive && w.FailureCount < 10)
            .ToListAsync();

        var matchingEndpoints = endpoints
            .Where(w => w.Events == "*" || w.Events.Split(',').Any(e => e.Trim() == eventType))
            .ToList();

        if (matchingEndpoints.Count == 0) return;

        var webhookPayload = new
        {
            @event = eventType,
            timestamp = DateTime.UtcNow,
            data = payload
        };

        var json = JsonSerializer.Serialize(webhookPayload);

        var tasks = matchingEndpoints.Select(ep => DeliverAsync(ep, json, db));
        await Task.WhenAll(tasks);
        await db.SaveChangesAsync();
    }

    private async Task DeliverAsync(WebhookEndpoint endpoint, string json, AppDbContext db)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            // Sign payload with HMAC-SHA256 if secret is configured
            if (!string.IsNullOrEmpty(endpoint.Secret))
            {
                var signature = ComputeHmac(json, endpoint.Secret);
                request.Headers.Add("X-HostPanel-Signature", $"sha256={signature}");
            }

            request.Headers.Add("X-HostPanel-Event", "webhook");
            request.Headers.Add("User-Agent", "HostPanel-Webhook/1.0");

            var response = await _http.SendAsync(request);

            endpoint.LastTriggeredAt = DateTime.UtcNow;

            if (response.IsSuccessStatusCode)
            {
                endpoint.FailureCount = 0;
                _logger.LogDebug("Webhook delivered to {Url}", endpoint.Url);
            }
            else
            {
                endpoint.FailureCount++;
                endpoint.LastFailureAt = DateTime.UtcNow;
                _logger.LogWarning("Webhook to {Url} returned {Status}", endpoint.Url, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            endpoint.FailureCount++;
            endpoint.LastFailureAt = DateTime.UtcNow;
            _logger.LogWarning(ex, "Webhook delivery failed to {Url}", endpoint.Url);
        }
    }

    private static string ComputeHmac(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLower();
    }
}
