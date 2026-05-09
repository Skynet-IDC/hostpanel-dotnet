using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using HostPanel.Modules.Billing.Providers;

namespace HostPanel.Tests;

public class StripeWebhookTests
{
    [Fact]
    public async Task HandleWebhook_RejectsInvalidSignature()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Stripe:SecretKey"] = "sk_test_fake",
                ["Billing:Stripe:WebhookSecret"] = "whsec_test_secret"
            })
            .Build();

        var provider = new StripeProvider(config, NullLogger<StripeProvider>.Instance);
        var result = await provider.HandleWebhookAsync("{}", "invalid-signature");

        Assert.False(result.Success);
        Assert.Contains("Invalid signature", result.Message);
    }

    [Fact]
    public async Task HandleWebhook_RejectsExpiredTimestamp()
    {
        var secret = "whsec_test_secret";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Stripe:SecretKey"] = "sk_test_fake",
                ["Billing:Stripe:WebhookSecret"] = secret
            })
            .Build();

        var provider = new StripeProvider(config, NullLogger<StripeProvider>.Instance);

        // Create signature with old timestamp (10 min ago)
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var payload = "{}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{oldTimestamp}.{payload}"));
        var sig = $"t={oldTimestamp},v1={Convert.ToHexString(hash).ToLower()}";

        var result = await provider.HandleWebhookAsync(payload, sig);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task HandleWebhook_AcceptsValidSignature()
    {
        var secret = "whsec_test_secret";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Stripe:SecretKey"] = "sk_test_fake",
                ["Billing:Stripe:WebhookSecret"] = secret
            })
            .Build();

        var provider = new StripeProvider(config, NullLogger<StripeProvider>.Instance);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = """{"type":"invoice.paid","data":{"object":{"id":"inv_123"}}}""";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        var sig = $"t={timestamp},v1={Convert.ToHexString(hash).ToLower()}";

        var result = await provider.HandleWebhookAsync(payload, sig);
        Assert.True(result.Success);
        Assert.Equal("inv_123", result.InvoiceId);
        Assert.Equal("paid", result.Status);
    }

    [Fact]
    public async Task HandleWebhook_RejectsEmptySecret()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Stripe:SecretKey"] = "sk_test_fake",
                ["Billing:Stripe:WebhookSecret"] = ""
            })
            .Build();

        var provider = new StripeProvider(config, NullLogger<StripeProvider>.Instance);
        var result = await provider.HandleWebhookAsync("{}", "any");
        Assert.False(result.Success);
    }
}
