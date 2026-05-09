using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HostPanel.Infrastructure.Services;

public class EmailNotificationService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(IConfiguration config, ILogger<EmailNotificationService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendWelcomeEmailAsync(string toEmail, string customerName, string panelUrl, string password)
    {
        var subject = $"Welcome to {_config["Branding:PanelName"] ?? "HostPanel"}";
        var body = $"""
            <html><body style="font-family: sans-serif; line-height: 1.6; color: #333;">
            <h2>Welcome, {WebUtility.HtmlEncode(customerName)}!</h2>
            <p>Your hosting account has been created. Here are your login details:</p>
            <table style="border-collapse: collapse; margin: 20px 0;">
                <tr><td style="padding: 8px; font-weight: bold;">Panel URL:</td><td style="padding: 8px;"><a href="{panelUrl}">{panelUrl}</a></td></tr>
                <tr><td style="padding: 8px; font-weight: bold;">Email:</td><td style="padding: 8px;">{WebUtility.HtmlEncode(toEmail)}</td></tr>
                <tr><td style="padding: 8px; font-weight: bold;">Password:</td><td style="padding: 8px;">{WebUtility.HtmlEncode(password)}</td></tr>
            </table>
            <p>Please change your password after first login.</p>
            <p>If you have questions, contact <a href="mailto:{_config["Branding:SupportEmail"] ?? "support@hostpanel.local"}">{_config["Branding:SupportEmail"] ?? "support"}</a>.</p>
            </body></html>
            """;
        await SendAsync(toEmail, subject, body);
    }

    public async Task SendInvoiceNotificationAsync(string toEmail, string customerName, decimal amount, string currency, DateTime dueDate, string invoiceUrl)
    {
        var subject = $"Invoice Due - {amount:F2} {currency}";
        var body = $"""
            <html><body style="font-family: sans-serif; line-height: 1.6; color: #333;">
            <h2>Invoice for {WebUtility.HtmlEncode(customerName)}</h2>
            <p>A new invoice has been generated for your hosting services.</p>
            <table style="border-collapse: collapse; margin: 20px 0;">
                <tr><td style="padding: 8px; font-weight: bold;">Amount:</td><td style="padding: 8px;">{amount:F2} {currency}</td></tr>
                <tr><td style="padding: 8px; font-weight: bold;">Due Date:</td><td style="padding: 8px;">{dueDate:yyyy-MM-dd}</td></tr>
            </table>
            <p><a href="{invoiceUrl}" style="background: #2563eb; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;">Pay Now</a></p>
            </body></html>
            """;
        await SendAsync(toEmail, subject, body);
    }

    public async Task SendPaymentReminderAsync(string toEmail, string customerName, decimal amount, string currency, int daysPastDue)
    {
        var subject = $"Payment Reminder - {amount:F2} {currency} ({daysPastDue} days overdue)";
        var body = $"""
            <html><body style="font-family: sans-serif; line-height: 1.6; color: #333;">
            <h2>Payment Reminder</h2>
            <p>Dear {WebUtility.HtmlEncode(customerName)},</p>
            <p>Your invoice of <strong>{amount:F2} {currency}</strong> is now <strong>{daysPastDue} days past due</strong>.</p>
            <p>Please make payment to avoid service suspension.</p>
            </body></html>
            """;
        await SendAsync(toEmail, subject, body);
    }

    public async Task SendSuspensionNoticeAsync(string toEmail, string customerName)
    {
        var subject = "Account Suspended - Payment Required";
        var body = $"""
            <html><body style="font-family: sans-serif; line-height: 1.6; color: #333;">
            <h2>Account Suspended</h2>
            <p>Dear {WebUtility.HtmlEncode(customerName)},</p>
            <p>Your hosting account has been suspended due to non-payment. Your websites and email are currently offline.</p>
            <p>Please make payment immediately to restore services. Your data is preserved and will be available once payment is received.</p>
            </body></html>
            """;
        await SendAsync(toEmail, subject, body);
    }

    public async Task SendSslExpiryWarningAsync(string toEmail, string domain, DateTime expiresAt)
    {
        var daysLeft = (int)(expiresAt - DateTime.UtcNow).TotalDays;
        var subject = $"SSL Certificate Expiring - {domain} ({daysLeft} days)";
        var body = $"""
            <html><body style="font-family: sans-serif; line-height: 1.6; color: #333;">
            <h2>SSL Certificate Expiry Warning</h2>
            <p>The SSL certificate for <strong>{WebUtility.HtmlEncode(domain)}</strong> expires on <strong>{expiresAt:yyyy-MM-dd}</strong> ({daysLeft} days from now).</p>
            <p>If auto-renewal is enabled, this will be handled automatically. Otherwise, please renew manually from the panel.</p>
            </body></html>
            """;
        await SendAsync(toEmail, subject, body);
    }

    private async Task SendAsync(string to, string subject, string htmlBody)
    {
        var smtpHost = _config["Smtp:Host"] ?? "localhost";
        var smtpPort = int.Parse(_config["Smtp:Port"] ?? "587");
        var smtpUser = _config["Smtp:Username"];
        var smtpPass = _config["Smtp:Password"];
        var fromEmail = _config["Smtp:From"] ?? "noreply@hostpanel.local";

        try
        {
            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = smtpPort != 25,
                Credentials = !string.IsNullOrEmpty(smtpUser) ? new NetworkCredential(smtpUser, smtpPass) : null
            };

            var msg = new MailMessage(fromEmail, to, subject, htmlBody) { IsBodyHtml = true };
            await client.SendMailAsync(msg);
            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}: {Subject}", to, subject);
        }
    }
}
