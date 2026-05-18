using System.Diagnostics;
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
    private readonly ILogger<InvoicePdfController> _logger;

    public InvoicePdfController(AppDbContext db, ILogger<InvoicePdfController> logger)
    {
        _db = db;
        _logger = logger;
    }

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

        // Bug #52 fix: produce a real PDF (was returning HTML with .html extension).
        // Shell out to wkhtmltopdf (installed by installer) — stdin html, stdout pdf bytes.
        // Falls back to HTML if wkhtmltopdf is missing so the endpoint never 500s on a clean dev box.
        var pdfBytes = await TryRenderPdfAsync(html);
        if (pdfBytes is null)
        {
            _logger.LogWarning("wkhtmltopdf unavailable — falling back to HTML for invoice {Id}", invoice.Id);
            return File(Encoding.UTF8.GetBytes(html), "text/html", $"invoice-{invoice.Id:D6}.html");
        }
        return File(pdfBytes, "application/pdf", $"invoice-{invoice.Id:D6}.pdf");
    }

    /// <summary>
    /// Render an HTML document to PDF by piping it through `wkhtmltopdf - -` (stdin/stdout mode).
    /// Returns null if wkhtmltopdf is missing or fails; caller falls back to HTML.
    /// </summary>
    private static async Task<byte[]?> TryRenderPdfAsync(string html)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wkhtmltopdf",
                Arguments = "--quiet --enable-local-file-access --encoding utf-8 - -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;

            // Write HTML to stdin
            var stdinBytes = Encoding.UTF8.GetBytes(html);
            await proc.StandardInput.BaseStream.WriteAsync(stdinBytes);
            proc.StandardInput.Close();

            // Read stdout (PDF) and stderr concurrently to avoid pipe deadlock
            using var stdoutBuf = new MemoryStream();
            var stdoutTask = proc.StandardOutput.BaseStream.CopyToAsync(stdoutBuf);
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { proc.Kill(true); } catch { } return null; }

            await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0 || stdoutBuf.Length == 0)
                return null;

            // sanity: PDF must start with %PDF
            var bytes = stdoutBuf.ToArray();
            if (bytes.Length < 4 || bytes[0] != 0x25 || bytes[1] != 0x50 || bytes[2] != 0x44 || bytes[3] != 0x46)
                return null;

            return bytes;
        }
        catch (System.ComponentModel.Win32Exception) { return null; /* binary missing */ }
        catch { return null; }
    }
}
