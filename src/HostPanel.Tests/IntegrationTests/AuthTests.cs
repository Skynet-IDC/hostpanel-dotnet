using System.Net;
using System.Net.Http.Json;

namespace HostPanel.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public class AuthTests : IClassFixture<TestWebApplicationFactory> 
{
    private readonly HttpClient _client;

    public AuthTests(TestWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "admin@hostpanel.local", password = "REDACTED-PASSWORD" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(json.TryGetProperty("token", out var token));
        Assert.False(string.IsNullOrEmpty(token.GetString()));
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "admin@hostpanel.local", password = "wrongpassword" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithToken_ReturnsUser()
    {
        await TestHelpers.LoginAsync(_client);
        var response = await _client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("admin@hostpanel.local", json.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/health");
        // May return 503 if agent not running in test, but shouldn't throw
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable);
    }
}
