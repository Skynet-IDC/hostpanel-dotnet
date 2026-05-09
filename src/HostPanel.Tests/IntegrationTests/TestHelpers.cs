using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPanel.Tests.IntegrationTests;

public static class TestHelpers
{
    public static async Task<string> LoginAsync(HttpClient client, string email = "admin@hostpanel.local", string password = "REDACTED-PASSWORD")
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = json.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return token;
    }

    public static async Task<T?> GetAsync<T>(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }
}
