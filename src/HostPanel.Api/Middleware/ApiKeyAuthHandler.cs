using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Middleware;

public class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AppDbContext _db;

    public ApiKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.NoResult();

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith("Bearer hp_"))
            return AuthenticateResult.NoResult(); // Not an API key, let JWT handler try

        var apiKeyValue = headerValue["Bearer ".Length..];

        // API keys have format "hp_prefix_secret"
        var prefix = apiKeyValue.Length >= 6 ? apiKeyValue[..6] : apiKeyValue;

        // Hash the full key to compare
        var keyHash = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(apiKeyValue)));

        var apiKey = await _db.ApiKeys
            .Include(k => k.User)
            .FirstOrDefaultAsync(k => k.KeyPrefix == prefix && k.KeyHash == keyHash && k.IsActive);

        if (apiKey is null)
            return AuthenticateResult.Fail("Invalid API key");

        if (apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt < DateTime.UtcNow)
            return AuthenticateResult.Fail("API key expired");

        // Update last used
        apiKey.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKey.UserId),
            new(ClaimTypes.Email, apiKey.User.Email ?? ""),
            new(ClaimTypes.Role, apiKey.User.Role.ToString()),
            new("apiKeyId", apiKey.Id.ToString()),
        };

        // Add scope claims if scoped
        if (!string.IsNullOrEmpty(apiKey.Scopes))
            claims.Add(new Claim("scopes", apiKey.Scopes));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
