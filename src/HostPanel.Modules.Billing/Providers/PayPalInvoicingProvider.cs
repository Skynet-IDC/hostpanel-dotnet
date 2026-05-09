using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;

namespace HostPanel.Modules.Billing.Providers;

public class PayPalInvoicingProvider : IPaymentProvider
{
    public string ProviderName => "PayPal";

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly bool _useSandbox;
    private readonly string _webhookId;
    private readonly ILogger<PayPalInvoicingProvider> _logger;
    private string? _accessToken;
    private DateTime _tokenExpiry;

    public PayPalInvoicingProvider(IConfiguration config, ILogger<PayPalInvoicingProvider> logger)
    {
        _logger = logger;
        _clientId = config["Billing:PayPal:ClientId"] ?? "";
        _clientSecret = config["Billing:PayPal:ClientSecret"] ?? "";
        _useSandbox = config.GetValue("Billing:PayPal:UseSandbox", true);
        _webhookId = config["Billing:PayPal:WebhookId"] ?? "";

        var baseUrl = _useSandbox
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com";

        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<string> CreateInvoiceAsync(Invoice invoice, AppUser customer)
    {
        await EnsureAuthenticatedAsync();

        var payload = new
        {
            detail = new
            {
                invoice_number = $"HP-{invoice.Id:D6}",
                currency_code = invoice.Currency,
                payment_term = new { term_type = "NET_30" }
            },
            invoicer = new { name = new { full_name = "HostPanel" } },
            primary_recipients = new[]
            {
                new
                {
                    billing_info = new
                    {
                        name = new { given_name = customer.FirstName, surname = customer.LastName },
                        email_address = customer.Email
                    }
                }
            },
            items = new[]
            {
                new
                {
                    name = "Hosting Service",
                    quantity = "1",
                    unit_amount = new { currency_code = invoice.Currency, value = invoice.Amount.ToString("F2") }
                }
            }
        };

        var response = await _http.PostAsJsonAsync("/v2/invoicing/invoices", payload);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = result.GetProperty("id").GetString()!;

        // Send the invoice
        await _http.PostAsync($"/v2/invoicing/invoices/{invoiceId}/send", new StringContent("{\"send_to_invoicer\":true}", Encoding.UTF8, "application/json"));

        _logger.LogInformation("Created PayPal invoice {InvoiceId} for {Email}, amount {Amount}",
            invoiceId, customer.Email, invoice.Amount);

        return invoiceId;
    }

    public async Task<bool> CheckPaymentStatusAsync(string providerInvoiceId)
    {
        await EnsureAuthenticatedAsync();
        var response = await _http.GetAsync($"/v2/invoicing/invoices/{providerInvoiceId}");
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var status = result.GetProperty("status").GetString();
        return status == "PAID" || status == "MARKED_AS_PAID";
    }

    public async Task SendReminderAsync(string providerInvoiceId)
    {
        await EnsureAuthenticatedAsync();
        await _http.PostAsync($"/v2/invoicing/invoices/{providerInvoiceId}/remind",
            new StringContent("{\"send_to_invoicer\":true}", Encoding.UTF8, "application/json"));
    }

    public async Task CancelInvoiceAsync(string providerInvoiceId)
    {
        await EnsureAuthenticatedAsync();
        await _http.PostAsync($"/v2/invoicing/invoices/{providerInvoiceId}/cancel",
            new StringContent("{\"send_to_invoicer\":true,\"send_to_recipient\":true}", Encoding.UTF8, "application/json"));
    }

    public async Task<WebhookResult> HandleWebhookAsync(string payload, string signature, IDictionary<string, string>? headers = null)
    {
        if (string.IsNullOrEmpty(_webhookId))
            return new WebhookResult(false, null, null, "PayPal webhook ID not configured");

        await EnsureAuthenticatedAsync();

        var verified = await VerifyPayPalWebhookAsync(payload, signature, headers ?? new Dictionary<string, string>());
        if (!verified)
            return new WebhookResult(false, null, null, "PayPal webhook signature verification failed");

        var json = JsonSerializer.Deserialize<JsonElement>(payload);
        var eventType = json.GetProperty("event_type").GetString();

        if (eventType == "INVOICING.INVOICE.PAID")
        {
            var invoiceId = json.GetProperty("resource").GetProperty("id").GetString();
            return new WebhookResult(true, invoiceId, "paid", null);
        }

        return new WebhookResult(true, null, null, $"Unhandled event: {eventType}");
    }

    private async Task<bool> VerifyPayPalWebhookAsync(string payload, string transmissionSig, IDictionary<string, string> headers)
    {
        headers.TryGetValue("PAYPAL-AUTH-ALGO", out var authAlgo);
        headers.TryGetValue("PAYPAL-CERT-URL", out var certUrl);
        headers.TryGetValue("PAYPAL-TRANSMISSION-ID", out var transmissionId);
        headers.TryGetValue("PAYPAL-TRANSMISSION-TIME", out var transmissionTime);

        var verifyPayload = new
        {
            auth_algo = authAlgo ?? "SHA256withRSA",
            cert_url = certUrl ?? "",
            transmission_id = transmissionId ?? "",
            transmission_sig = transmissionSig,
            transmission_time = transmissionTime ?? "",
            webhook_id = _webhookId,
            webhook_event = JsonSerializer.Deserialize<JsonElement>(payload)
        };

        try
        {
            var response = await _http.PostAsJsonAsync("/v1/notifications/verify-webhook-signature", verifyPayload);
            if (!response.IsSuccessStatusCode) return false;

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var status = result.GetProperty("verification_status").GetString();
            return status == "SUCCESS";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal webhook verification failed");
            return false;
        }
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_accessToken is not null && DateTime.UtcNow < _tokenExpiry) return;

        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
        {
            Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        _accessToken = result.GetProperty("access_token").GetString();
        var expiresIn = result.GetProperty("expires_in").GetInt32();
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }
}
