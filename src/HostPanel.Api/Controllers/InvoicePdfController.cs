using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/billing/invoices")]
[Authorize]
public class InvoicePdfController : ControllerBase
{
    private readonly AppDbContext _db;

    public InvoicePdfController(AppDbContext db) => _db = db;

    [HttpGet("{invoiceId}/pdf")]
    public async Task<IActionResult> DownloadPdf(int invoiceId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Subscription).ThenInclude(s => s.User)
            .Include(i => i.Subscription).ThenInclude(s => s.ServicePlan)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice is null) return NotFound();

        var invId = invoice.Id.ToString("D6");
        var createdDate = invoice.CreatedAt.ToString("yyyy-MM-dd");
        var status = invoice.Status.ToString();
        var statusLower = status.ToLower();
        var firstName = invoice.Subscription.User.FirstName;
        var lastName = invoice.Subscription.User.LastName;
        var email = invoice.Subscription.User.Email;
        var company = invoice.Subscription.User.CompanyName ?? "";
        var planName = invoice.Subscription.ServicePlan.Name;
        var amount = invoice.Amount.ToString("F2");
        var currency = invoice.Currency;
        var dueDate = invoice.DueDate.ToString("yyyy-MM-dd");
        var paidLine = invoice.PaidDate.HasValue ? $"<p><strong>Paid:</strong> {invoice.PaidDate:yyyy-MM-dd}</p>" : "";

        var html = $$"""
            <!DOCTYPE html>
            <html>
            <head><style>
                body { font-family: sans-serif; margin: 40px; color: #333; }
                h1 { color: #2563eb; }
                table { width: 100%; border-collapse: collapse; margin: 20px 0; }
                th, td { padding: 12px; text-align: left; border-bottom: 1px solid #eee; }
                th { background: #f8f9fa; font-weight: 600; }
                .total { font-size: 1.5em; font-weight: bold; color: #2563eb; }
            </style></head>
            <body>
                <h1>Invoice #{{invId}}</h1>
                <p><strong>Date:</strong> {{createdDate}}</p>
                <p><strong>Status:</strong> {{status}}</p>

                <h3>Bill To</h3>
                <p>{{firstName}} {{lastName}}<br/>{{email}}<br/>{{company}}</p>

                <table>
                    <thead><tr><th>Description</th><th>Amount</th></tr></thead>
                    <tbody>
                        <tr>
                            <td>Hosting Service - {{planName}}</td>
                            <td>{{amount}} {{currency}}</td>
                        </tr>
                    </tbody>
                </table>

                <p class="total">Total: {{amount}} {{currency}}</p>
                <p><strong>Due Date:</strong> {{dueDate}}</p>
                {{paidLine}}
            </body>
            </html>
            """;

        var bytes = Encoding.UTF8.GetBytes(html);
        return File(bytes, "text/html", $"invoice-{invoice.Id:D6}.html");
    }
}
