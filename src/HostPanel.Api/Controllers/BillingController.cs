using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class BillingController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IEnumerable<IPaymentProvider> _providers;
    private readonly ILogger<BillingController> _logger;

    public BillingController(AppDbContext db, IEnumerable<IPaymentProvider> providers, ILogger<BillingController> logger)
    {
        _db = db;
        _providers = providers;
        _logger = logger;
    }

    [HttpGet("invoices")]
    [Authorize]
    public async Task<IActionResult> ListInvoices([FromQuery] int? subscriptionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        var query = _db.Invoices
            .Include(i => i.Subscription).ThenInclude(s => s.User)
            .AsQueryable();

        if (subscriptionId.HasValue)
            query = query.Where(i => i.SubscriptionId == subscriptionId.Value);

        // Tenant filtering
        if (role == UserRole.Customer.ToString())
            query = query.Where(i => i.Subscription.UserId == userId);
        else if (role == UserRole.Reseller.ToString())
            query = query.Where(i => i.Subscription.UserId == userId || i.Subscription.User.ParentUserId == userId);

        var invoices = await query
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new
            {
                i.Id, i.SubscriptionId, i.Provider, i.ProviderInvoiceId,
                i.Amount, i.Currency, i.Status, i.DueDate, i.PaidDate, i.CreatedAt
            })
            .ToListAsync();

        return Ok(invoices);
    }

    [HttpPost("webhooks/paypal")]
    [AllowAnonymous]
    public async Task<IActionResult> PayPalWebhook()
    {
        var body = await new StreamReader(Request.Body).ReadToEndAsync();
        var signature = Request.Headers["PAYPAL-TRANSMISSION-SIG"].FirstOrDefault() ?? "";

        // Pass all PayPal verification headers
        var headers = new Dictionary<string, string>
        {
            ["PAYPAL-AUTH-ALGO"] = Request.Headers["PAYPAL-AUTH-ALGO"].FirstOrDefault() ?? "",
            ["PAYPAL-CERT-URL"] = Request.Headers["PAYPAL-CERT-URL"].FirstOrDefault() ?? "",
            ["PAYPAL-TRANSMISSION-ID"] = Request.Headers["PAYPAL-TRANSMISSION-ID"].FirstOrDefault() ?? "",
            ["PAYPAL-TRANSMISSION-TIME"] = Request.Headers["PAYPAL-TRANSMISSION-TIME"].FirstOrDefault() ?? ""
        };

        var provider = _providers.FirstOrDefault(p => p.ProviderName == "PayPal");
        if (provider is null) return Ok();

        var result = await provider.HandleWebhookAsync(body, signature, headers);

        if (!result.Success)
        {
            _logger.LogWarning("PayPal webhook verification failed: {Message}", result.Message);
            return Unauthorized(new { message = "Webhook verification failed" });
        }

        if (result.InvoiceId is not null && result.Status == "paid")
            await MarkInvoicePaidAsync(result.InvoiceId);

        return Ok();
    }

    [HttpPost("webhooks/stripe")]
    [AllowAnonymous]
    public async Task<IActionResult> StripeWebhook()
    {
        var body = await new StreamReader(Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault() ?? "";

        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Stripe");
        if (provider is null) return Ok();

        var result = await provider.HandleWebhookAsync(body, signature);

        if (!result.Success)
        {
            _logger.LogWarning("Stripe webhook verification failed: {Message}", result.Message);
            return Unauthorized(new { message = "Webhook verification failed" });
        }

        if (result.InvoiceId is not null && result.Status == "paid")
            await MarkInvoicePaidAsync(result.InvoiceId);

        return Ok();
    }

    private async Task MarkInvoicePaidAsync(string providerInvoiceId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Subscription)
            .FirstOrDefaultAsync(i => i.ProviderInvoiceId == providerInvoiceId);

        if (invoice is null) return;

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidDate = DateTime.UtcNow;

        if (invoice.Subscription.Status == SubscriptionStatus.Suspended)
            invoice.Subscription.Status = SubscriptionStatus.Active;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Invoice {InvoiceId} marked paid via webhook", invoice.Id);
    }
}
