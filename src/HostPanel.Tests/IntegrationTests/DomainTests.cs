using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPanel.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public class DomainTests : IClassFixture<TestWebApplicationFactory> 
{
    private readonly HttpClient _client;

    public DomainTests(TestWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task ListDomains_Authenticated_ReturnsOk()
    {
        await TestHelpers.LoginAsync(_client);
        var response = await _client.GetAsync("/api/v1/domains");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListDomains_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/domains");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ServerStats_ReturnsHostname()
    {
        await TestHelpers.LoginAsync(_client);
        var response = await _client.GetAsync("/api/v1/server/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("hostname", out _));
    }

    [Fact]
    public async Task ServicePlans_ListReturnsOk()
    {
        await TestHelpers.LoginAsync(_client);
        var response = await _client.GetAsync("/api/v1/serviceplans");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ServicePlans_CreateRequiresAdmin()
    {
        await TestHelpers.LoginAsync(_client);
        var response = await _client.PostAsJsonAsync("/api/v1/serviceplans", new
        {
            name = "Test Plan",
            diskQuotaMb = 1024,
            bandwidthLimitMb = 10240,
            maxDomains = 5,
            maxEmailAccounts = 10,
            maxDatabases = 3,
            pricePerPeriod = 9.99
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
