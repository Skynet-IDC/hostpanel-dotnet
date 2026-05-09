using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;

namespace HostPanel.Modules.Billing.Providers;

public class StripeProvider : IPaymentProvider
{
    public string ProviderName => "Stripe";

    private readonly HttpClient _http;
    private readonly string _webhookSecret;
    private readonly ILogger<StripeProvider> _logger;

    public StripeProvider(IConfiguration config, ILogger<StripeProvider> logger)
    {
        _logger = logger;
        _webhookSecret = config["Billing:Stripe:WebhookSecret"] ?? "";
        var apiKey = config["Billing:Stripe:SecretKey"] ?? "";

        _http = new HttpClient { BaseAddress = new Uri("https://api.stripe.com/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> CreateInvoiceAsync(Invoice invoice, AppUser customer)
    {
        // Get or create Stripe customer
        var customerId = await GetOrCreateCustomerAsync(customer);

        // Create invoice item
        var itemContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["customer"] = customerId,
            ["amount"] = ((int)(invoice.Amount * 100)).ToString(), // Stripe uses cents
            ["currency"] = invoice.Currency.ToLower(),
            ["description"] = "Hosting Service"
        });

        await _http.PostAsync("v1/invoiceitems", itemContent);

        // Create and finalize invoice
        var invoiceContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["customer"] = customerId,
            ["auto_advance"] = "true",
            ["collection_method"] = "send_invoice",
            ["days_until_due"] = "30"
        });

        var response = await _http.PostAsync("v1/invoices", invoiceContent);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stripeInvoiceId = result.GetProperty("id").GetString()!;

        // Finalize
        await _http.PostAsync($"v1/invoices/{stripeInvoiceId}/finalize", null);

        // Send
        await _http.PostAsync($"v1/invoices/{stripeInvoiceId}/send", null);

        _logger.LogInformation("Created Stripe invoice {InvoiceId} for {Email}", stripeInvoiceId, customer.Email);
        return stripeInvoiceId;
    }

    public async Task<bool> CheckPaymentStatusAsync(string providerInvoiceId)
    {
        var response = await _http.GetAsync($"v1/invoices/{providerInvoiceId}");
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("status").GetString() == "paid";
    }

    public async Task SendReminderAsync(string providerInvoiceId)
    {
        await _http.PostAsync($"v1/invoices/{providerInvoiceId}/send", null);
    }

    public async Task CancelInvoiceAsync(string providerInvoiceId)
    {
        await _http.PostAsync($"v1/invoices/{providerInvoiceId}/void", null);
    }

    public Task<WebhookResult> HandleWebhookAsync(string payload, string signature, IDictionary<string, string>? headers = null)
    {
        // Verify Stripe signature (Stripe-Signature header format: t=timestamp,v1=hash)
        if (string.IsNullOrEmpty(_webhookSecret))
            return Task.FromResult(new WebhookResult(false, null, null, "Webhook secret not configured"));

        if (!VerifyStripeSignature(payload, signature, _webhookSecret))
            return Task.FromResult(new WebhookResult(false, null, null, "Invalid signature"));

        var json = JsonSerializer.Deserialize<JsonElement>(payload);
        var eventType = json.GetProperty("type").GetString();

        if (eventType == "invoice.paid")
        {
            var invoiceId = json.GetProperty("data").GetProperty("object").GetProperty("id").GetString();
            return Task.FromResult(new WebhookResult(true, invoiceId, "paid", null));
        }

        return Task.FromResult(new WebhookResult(true, null, null, $"Unhandled: {eventType}"));
    }

    private static bool VerifyStripeSignature(string payload, string signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(signatureHeader)) return false;

        // Parse "t=timestamp,v1=signature" format
        string? timestamp = null, expectedSig = null;
        foreach (var part in signatureHeader.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t") timestamp = kv[1];
            if (kv[0] == "v1") expectedSig = kv[1];
        }

        if (timestamp is null || expectedSig is null) return false;

        // Reject timestamps older than 5 minutes (replay protection)
        if (long.TryParse(timestamp, out var ts))
        {
            var eventTime = DateTimeOffset.FromUnixTimeSeconds(ts);
            if (Math.Abs((DateTimeOffset.UtcNow - eventTime).TotalMinutes) > 5) return false;
        }

        // Compute expected signature: HMAC-SHA256(secret, "timestamp.payload")
        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(signedPayload));
        var computed = Convert.ToHexString(hash).ToLower();

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(computed),
            System.Text.Encoding.UTF8.GetBytes(expectedSig));
    }

    private async Task<string> GetOrCreateCustomerAsync(AppUser user)
    {
        // Search for existing customer by email
        var searchResponse = await _http.GetAsync($"v1/customers?email={Uri.EscapeDataString(user.Email ?? "")}&limit=1");
        var searchResult = await searchResponse.Content.ReadFromJsonAsync<JsonElement>();
        var data = searchResult.GetProperty("data");

        if (data.GetArrayLength() > 0)
            return data[0].GetProperty("id").GetString()!;

        // Create new customer
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = user.Email ?? "",
            ["name"] = $"{user.FirstName} {user.LastName}",
            ["metadata[hostpanel_user_id]"] = user.Id
        });

        var response = await _http.PostAsync("v1/customers", content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("id").GetString()!;
    }
}
