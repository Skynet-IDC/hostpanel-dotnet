using HostPanel.Core.Models;

namespace HostPanel.Core.Interfaces;

public interface IPaymentProvider
{
    string ProviderName { get; }
    Task<string> CreateInvoiceAsync(Invoice invoice, AppUser customer);
    Task<bool> CheckPaymentStatusAsync(string providerInvoiceId);
    Task SendReminderAsync(string providerInvoiceId);
    Task CancelInvoiceAsync(string providerInvoiceId);
    Task<WebhookResult> HandleWebhookAsync(string payload, string signature, IDictionary<string, string>? headers = null);
}

public record WebhookResult(bool Success, string? InvoiceId, string? Status, string? Message);
