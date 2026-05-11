using System.Net;
using System.Net.Http.Json;

namespace HostPanel.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public class FeatureEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FeatureEndpointTests(TestWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient() => _factory.CreateClient();

    // --- Billing ---
    [Fact]
    public async Task Billing_ListInvoices_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/billing/invoices");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- Customers ---
    [Fact]
    public async Task Customers_List_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- WordPress ---
    [Fact]
    public async Task WordPress_List_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/wordpress");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- Databases ---
    [Fact]
    public async Task Databases_ListTypes_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/databases/types");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Databases_List_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/databases");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- Branding ---
    [Fact]
    public async Task Branding_Get_ReturnsOk_NoAuth()
    {
        var _client = CreateClient();
        var r = await _client.GetAsync("/api/v1/branding");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var json = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(json.TryGetProperty("panelName", out _));
    }

    // --- 2FA ---
    [Fact]
    public async Task TwoFactor_Status_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/auth/2fa/status");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var json = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.False(json.GetProperty("enabled").GetBoolean());
    }

    // --- API Keys ---
    [Fact]
    public async Task ApiKeys_List_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/apikeys");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task ApiKeys_Create_ReturnsKeyOnce()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.PostAsJsonAsync("/api/v1/apikeys", new { name = "test-key" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);

        var json = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(json.TryGetProperty("key", out var key));
        Assert.StartsWith("hp_", key.GetString());
    }

    // --- Subscriptions ---
    [Fact]
    public async Task Subscriptions_List_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/subscriptions");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- SSO Config ---
    [Fact]
    public async Task SsoConfig_ReturnsStatus()
    {
        var _client = CreateClient();
        var r = await _client.GetAsync("/api/v1/sso/config");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- Webhooks (admin) ---
    [Fact]
    public async Task Webhooks_ListEvents_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/webhooks/events");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- Firewall ---
    [Fact]
    public async Task Firewall_ListRules_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/firewall/rules");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- Server Updates ---
    [Fact]
    public async Task ServerUpdates_List_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/server/updates");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- Audit Log ---
    [Fact]
    public async Task AuditLog_List_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/audit");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- IP Management ---
    [Fact]
    public async Task IpAddresses_List_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/ip/addresses");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task IpBlacklist_List_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/ip/blacklist");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- Mail Ports ---
    [Fact]
    public async Task MailPorts_ReturnsAll()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/mail/ports");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var json = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(json.TryGetProperty("submission", out _));
        Assert.True(json.TryGetProperty("smtps", out _));
        Assert.True(json.TryGetProperty("altSubmission", out _));
    }

    // --- Panel Version ---
    [Fact]
    public async Task PanelVersion_ReturnsOk()
    {
        var _client = CreateClient();
        await TestHelpers.LoginAsync(_client);
        var r = await _client.GetAsync("/api/v1/panel/version");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // --- Webhook rejection (unauthenticated) ---
    [Fact]
    public async Task StripeWebhook_WithoutSignature_RejectsOrOk()
    {
        var _client = CreateClient();
        var r = await _client.PostAsJsonAsync("/api/v1/billing/webhooks/stripe", new { });
        // Should reject (401) or return OK if provider not configured
        Assert.True(r.StatusCode == HttpStatusCode.Unauthorized || r.StatusCode == HttpStatusCode.OK);
    }
}
