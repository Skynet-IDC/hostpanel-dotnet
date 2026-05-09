using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Modules.Billing.Services;

public class BillingCycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingCycleService> _logger;

    public BillingCycleService(IServiceScopeFactory scopeFactory, ILogger<BillingCycleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBillingCycleAsync(stoppingToken);
                await ProcessOverdueInvoicesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in billing cycle");
            }

            // Run daily at midnight
            var now = DateTime.UtcNow;
            var next = now.Date.AddDays(1);
            await Task.Delay(next - now, stoppingToken);
        }
    }

    private async Task ProcessBillingCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dueSubscriptions = await db.Subscriptions
            .Include(s => s.User)
            .Include(s => s.ServicePlan)
            .Where(s => s.Status == SubscriptionStatus.Active)
            .Where(s => s.NextInvoiceDate != null && s.NextInvoiceDate <= DateTime.UtcNow)
            .ToListAsync(ct);

        _logger.LogInformation("Found {Count} subscriptions due for billing", dueSubscriptions.Count);

        foreach (var sub in dueSubscriptions)
        {
            try
            {
                var invoice = new Invoice
                {
                    SubscriptionId = sub.Id,
                    Provider = sub.PreferredPaymentProvider,
                    Amount = sub.ServicePlan.PricePerPeriod,
                    Currency = "USD",
                    Status = InvoiceStatus.Sent,
                    DueDate = DateTime.UtcNow.AddDays(30)
                };

                db.Invoices.Add(invoice);

                // Advance next invoice date
                sub.NextInvoiceDate = sub.BillingPeriod switch
                {
                    BillingPeriod.Monthly => sub.NextInvoiceDate?.AddMonths(1),
                    BillingPeriod.Quarterly => sub.NextInvoiceDate?.AddMonths(3),
                    BillingPeriod.SemiAnnual => sub.NextInvoiceDate?.AddMonths(6),
                    BillingPeriod.Annual => sub.NextInvoiceDate?.AddYears(1),
                    _ => sub.NextInvoiceDate?.AddMonths(1)
                };

                await db.SaveChangesAsync(ct);

                // Send invoice via payment provider
                var providers = scope.ServiceProvider.GetServices<IPaymentProvider>();
                var provider = providers.FirstOrDefault(p =>
                    p.ProviderName == invoice.Provider.ToString());

                if (provider is not null)
                {
                    var providerInvoiceId = await provider.CreateInvoiceAsync(invoice, sub.User);
                    invoice.ProviderInvoiceId = providerInvoiceId;
                    await db.SaveChangesAsync(ct);
                }

                _logger.LogInformation("Billed subscription {SubId} for {Amount} {Currency}",
                    sub.Id, invoice.Amount, invoice.Currency);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to bill subscription {SubId}", sub.Id);
            }
        }
    }

    private async Task ProcessOverdueInvoicesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var overdueInvoices = await db.Invoices
            .Include(i => i.Subscription).ThenInclude(s => s.ServicePlan)
            .Include(i => i.Subscription).ThenInclude(s => s.User)
            .Where(i => i.Status == InvoiceStatus.Sent && i.DueDate < DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var invoice in overdueInvoices)
        {
            var plan = invoice.Subscription.ServicePlan;
            var daysPastDue = (DateTime.UtcNow - invoice.DueDate).TotalDays;

            invoice.Status = InvoiceStatus.Overdue;

            // Check payment with provider
            if (!string.IsNullOrEmpty(invoice.ProviderInvoiceId))
            {
                var providers = scope.ServiceProvider.GetServices<IPaymentProvider>();
                var provider = providers.FirstOrDefault(p => p.ProviderName == invoice.Provider.ToString());
                if (provider is not null)
                {
                    var paid = await provider.CheckPaymentStatusAsync(invoice.ProviderInvoiceId);
                    if (paid)
                    {
                        invoice.Status = InvoiceStatus.Paid;
                        invoice.PaidDate = DateTime.UtcNow;

                        // Reactivate if suspended
                        if (invoice.Subscription.Status == SubscriptionStatus.Suspended)
                        {
                            invoice.Subscription.Status = SubscriptionStatus.Active;
                            _logger.LogInformation("Auto-reactivated subscription {SubId} after payment", invoice.SubscriptionId);
                        }

                        await db.SaveChangesAsync(ct);
                        continue;
                    }
                }
            }

            // Auto-suspend after grace period
            if (plan.AutoSuspend && daysPastDue >= plan.GracePeriodDays &&
                invoice.Subscription.Status == SubscriptionStatus.Active)
            {
                invoice.Subscription.Status = SubscriptionStatus.Suspended;
                _logger.LogWarning("Auto-suspended subscription {SubId} due to non-payment ({Days} days overdue)",
                    invoice.SubscriptionId, (int)daysPastDue);
            }

            // Send reminders
            var reminderDays = plan.ReminderSchedule.Split(',').Select(d => int.TryParse(d.Trim(), out var v) ? v : -1).Where(d => d >= 0);
            if (reminderDays.Contains((int)daysPastDue) && !string.IsNullOrEmpty(invoice.ProviderInvoiceId))
            {
                var providers = scope.ServiceProvider.GetServices<IPaymentProvider>();
                var provider = providers.FirstOrDefault(p => p.ProviderName == invoice.Provider.ToString());
                if (provider is not null)
                {
                    await provider.SendReminderAsync(invoice.ProviderInvoiceId);
                    _logger.LogInformation("Sent payment reminder for invoice {InvoiceId}", invoice.Id);
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
