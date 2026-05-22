using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Modules.Billing.Services;

namespace HostPanel.Modules.Billing.Providers;

/// <summary>
/// PayPal invoicing + webhook verification. Credentials live in the DB via
/// PaymentProviderConfigService — the operator updates them in Settings → Billing
/// without restarting the panel.
///
/// OAuth tokens are cached by (clientId, sandbox) so credential rotation cleanly
/// invalidates the old token (different cache key → fresh /v1/oauth2/token call).
/// </summary>
public class PayPalInvoicingProvider : IPaymentProvider
{
    public string ProviderName => "PayPal";

    private readonly PaymentProviderConfigService _configService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PayPalInvoicingProvider> _logger;
    private readonly object _tokenLock = new();
    private (string clientId, bool sandbox, string token, DateTime expiry)? _cachedToken;

    public PayPalInvoicingProvider(
        PaymentProviderConfigService configService,
        IHttpClientFactory httpFactory,
        ILogger<PayPalInvoicingProvider> logger)
    {
        _configService = configService;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>Sandbox/live base URL for the resolved config.</summary>
    private static string BaseUrl(bool sandbox) =>
        sandbox ? "https://api-m.sandbox.paypal.com" : "https://api-m.paypal.com";

    /// <summary>
    /// Resolve current credentials. Throws InvalidOperationException with a clear
    /// message if no enabled config exists — bubbles up as a 400/500 the operator
    /// can act on instead of an opaque "client_id missing".
    /// </summary>
    private async Task<PaymentProviderConfigService.Resolved> RequireConfigAsync(CancellationToken ct = default)
    {
        var cfg = await _configService.GetActiveAsync("PayPal", ct);
        if (cfg is null)
            throw new InvalidOperationException("PayPal is not configured. Set credentials in Settings → Billing → PayPal.");
        if (string.IsNullOrWhiteSpace(cfg.ClientId) || string.IsNullOrWhiteSpace(cfg.ClientSecret))
            throw new InvalidOperationException("PayPal credentials are incomplete. Re-enter Client ID + Client Secret in Settings.");
        return cfg;
    }

    private async Task<HttpClient> AuthClientAsync(CancellationToken ct = default)
    {
        var cfg = await RequireConfigAsync(ct);
        var token = await GetAccessTokenAsync(cfg, ct);
        var http = _httpFactory.CreateClient("paypal");
        http.BaseAddress = new Uri(BaseUrl(cfg.Sandbox));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<string> GetAccessTokenAsync(PaymentProviderConfigService.Resolved cfg, CancellationToken ct)
    {
        lock (_tokenLock)
        {
            if (_cachedToken is { } cached
                && cached.clientId == cfg.ClientId
                && cached.sandbox == cfg.Sandbox
                && DateTime.UtcNow < cached.expiry)
            {
                return cached.token;
            }
        }

        var http = _httpFactory.CreateClient("paypal-auth");
        http.BaseAddress = new Uri(BaseUrl(cfg.Sandbox));

        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cfg.ClientId}:{cfg.ClientSecret}"));
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
        {
            Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

        var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var token = result.GetProperty("access_token").GetString()!;
        var expiresIn = result.GetProperty("expires_in").GetInt32();

        lock (_tokenLock)
        {
            _cachedToken = (cfg.ClientId, cfg.Sandbox, token, DateTime.UtcNow.AddSeconds(expiresIn - 60));
        }
        return token;
    }

    /// <summary>
    /// Validate credentials by attempting an OAuth token call. Used by the Settings
    /// UI BEFORE persisting — operator gets immediate "Connected" feedback instead
    /// of finding out at first invoice creation that the secret was wrong.
    /// </summary>
    public async Task<(bool ok, string message)> ValidateCredentialsAsync(string clientId, string clientSecret, bool sandbox, CancellationToken ct = default)
    {
        try
        {
            var http = _httpFactory.CreateClient("paypal-validate");
            http.BaseAddress = new Uri(BaseUrl(sandbox));
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            var req = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
            {
                Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
            using var resp = await http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
                return (true, $"Connected — using {(sandbox ? "sandbox" : "live")}");
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, $"PayPal rejected credentials: HTTP {(int)resp.StatusCode} — {body}");
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach PayPal: {ex.Message}");
        }
    }

    public async Task<string> CreateInvoiceAsync(Invoice invoice, AppUser customer)
    {
        var http = await AuthClientAsync();

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

        var response = await http.PostAsJsonAsync("/v2/invoicing/invoices", payload);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = result.GetProperty("id").GetString()!;

        // Send the invoice so the customer gets the hosted payment link
        await http.PostAsync($"/v2/invoicing/invoices/{invoiceId}/send",
            new StringContent("{\"send_to_invoicer\":true}", Encoding.UTF8, "application/json"));

        _logger.LogInformation("Created PayPal invoice {InvoiceId} for {Email}, amount {Amount}",
            invoiceId, customer.Email, invoice.Amount);

        return invoiceId;
    }

    public async Task<bool> CheckPaymentStatusAsync(string providerInvoiceId)
    {
        var http = await AuthClientAsync();
        var response = await http.GetAsync($"/v2/invoicing/invoices/{providerInvoiceId}");
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var status = result.GetProperty("status").GetString();
        return status == "PAID" || status == "MARKED_AS_PAID";
    }

    public async Task SendReminderAsync(string providerInvoiceId)
    {
        var http = await AuthClientAsync();
        await http.PostAsync($"/v2/invoicing/invoices/{providerInvoiceId}/remind",
            new StringContent("{\"send_to_invoicer\":true}", Encoding.UTF8, "application/json"));
    }

    public async Task CancelInvoiceAsync(string providerInvoiceId)
    {
        var http = await AuthClientAsync();
        await http.PostAsync($"/v2/invoicing/invoices/{providerInvoiceId}/cancel",
            new StringContent("{\"send_to_invoicer\":true,\"send_to_recipient\":true}", Encoding.UTF8, "application/json"));
    }

    public async Task<WebhookResult> HandleWebhookAsync(string payload, string signature, IDictionary<string, string>? headers = null)
    {
        var cfg = await _configService.GetActiveAsync("PayPal");
        if (cfg is null || string.IsNullOrEmpty(cfg.WebhookId))
            return new WebhookResult(false, null, null, "PayPal webhook ID not configured (Settings → Billing → PayPal)");

        var verified = await VerifyPayPalWebhookAsync(payload, signature, headers ?? new Dictionary<string, string>(), cfg);
        if (!verified)
            return new WebhookResult(false, null, null, "PayPal webhook signature verification failed");

        var json = JsonSerializer.Deserialize<JsonElement>(payload);
        var eventType = json.GetProperty("event_type").GetString();

        if (eventType == "INVOICING.INVOICE.PAID")
        {
            var invoiceId = json.GetProperty("resource").GetProperty("id").GetString();
            return new WebhookResult(true, invoiceId, "paid", null);
        }
        if (eventType == "PAYMENT.CAPTURE.COMPLETED")
        {
            // Treat as a payment-completed signal even though it doesn't carry an invoice id directly.
            return new WebhookResult(true, null, "paid", null);
        }

        return new WebhookResult(true, null, null, $"Unhandled event: {eventType}");
    }

    private async Task<bool> VerifyPayPalWebhookAsync(string payload, string transmissionSig, IDictionary<string, string> headers, PaymentProviderConfigService.Resolved cfg)
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
            webhook_id = cfg.WebhookId,
            webhook_event = JsonSerializer.Deserialize<JsonElement>(payload)
        };

        try
        {
            var http = await AuthClientAsync();
            var response = await http.PostAsJsonAsync("/v1/notifications/verify-webhook-signature", verifyPayload);
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
}
