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

    /// <summary>
    /// Manually generate an invoice for a subscription right now (bug #53). Normally invoices
    /// are auto-created by BillingCycleService at midnight when sub.NextInvoiceDate &lt;= now.
    /// This endpoint bypasses that cycle for: signup invoices, ad-hoc charges, plan-change pro-rations.
    /// Admin/Reseller only.
    /// </summary>
    [HttpPost("invoices/generate")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> GenerateInvoice([FromBody] GenerateInvoiceRequest req)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.User)
            .Include(s => s.ServicePlan)
            .FirstOrDefaultAsync(s => s.Id == req.SubscriptionId);
        if (sub is null) return NotFound(new { message = "Subscription not found" });

        // Tenant guard: resellers can only invoice their own customers
        var role = User.FindFirstValue(ClaimTypes.Role);
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (role == UserRole.Reseller.ToString() && sub.User.ParentUserId != callerId && sub.UserId != callerId)
            return Forbid();

        // F1 amount resolution: explicit request value wins; otherwise pull the plan's
        // price for the subscription's actual BillingPeriod (handles per-cycle overrides
        // and linear-fallback in ServicePlan.GetPriceFor).
        var amount = req.Amount ?? sub.ServicePlan.GetPriceFor(sub.BillingPeriod);
        var currency = req.Currency ?? "USD";
        var dueDate = req.DueDate ?? DateTime.UtcNow.AddDays(14);

        // F1 provider routing: per-invoice override > subscription preference > Stripe default.
        // Honors the answers from the AskUserQuestion locking on 2026-05-22.
        var provider = req.Provider ?? sub.PreferredPaymentProvider;

        var invoice = new HostPanel.Core.Models.Invoice
        {
            SubscriptionId = sub.Id,
            Provider = provider,
            Amount = amount,
            Currency = currency,
            Status = InvoiceStatus.Sent,
            DueDate = dueDate,
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        // Create the actual invoice in Stripe/PayPal so the customer gets a hosted
        // payment link instead of just a record in our DB. Failures here don't roll back
        // the local invoice — the operator can retry via a re-send endpoint later.
        string? paymentLink = null;
        try
        {
            // ProviderName is a string ("Stripe"/"PayPal") — match against the enum.
            var billingProvider = _providers.FirstOrDefault(p =>
                string.Equals(p.ProviderName, provider.ToString(), StringComparison.OrdinalIgnoreCase));
            if (billingProvider is not null)
            {
                paymentLink = await billingProvider.CreateInvoiceAsync(invoice, sub.User);
                if (!string.IsNullOrEmpty(paymentLink))
                {
                    invoice.ProviderInvoiceId = paymentLink;
                    await _db.SaveChangesAsync();
                }
            }
            else
            {
                _logger.LogWarning("No registered IPaymentProvider matches enum {Provider}", provider);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider invoice create failed (local invoice persisted): provider={Provider} invoice={Id}",
                provider, invoice.Id);
        }

        _logger.LogInformation("Manual invoice generated: id={Id} sub={SubId} amount={Amount} {Currency} provider={Provider} due={Due}",
            invoice.Id, sub.Id, amount, currency, provider, dueDate);

        return CreatedAtAction(nameof(ListInvoices), new { subscriptionId = sub.Id }, new
        {
            invoice.Id, invoice.SubscriptionId, invoice.Provider, invoice.Amount, invoice.Currency,
            invoice.Status, invoice.DueDate, invoice.CreatedAt,
            providerInvoiceId = invoice.ProviderInvoiceId,
            paymentLink,
            downloadUrl = $"/api/v1/billing/invoices/{invoice.Id}/pdf",
        });
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

public record GenerateInvoiceRequest(int SubscriptionId, decimal? Amount = null, string? Currency = null, DateTime? DueDate = null, PaymentProvider? Provider = null);
