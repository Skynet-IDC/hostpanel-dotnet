using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Modules.Billing.Services;

/// <summary>
/// Read/write PaymentProviderConfig rows with IDataProtector-encrypted secrets
/// and a 60-second in-memory cache so hot paths (every invoice create) don't
/// hit the DB. Singleton — uses IServiceScopeFactory to open scoped DbContexts
/// per call, IDataProtector to wrap/unwrap secrets.
/// </summary>
public class PaymentProviderConfigService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtector _protector;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PaymentProviderConfigService> _logger;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public PaymentProviderConfigService(
        IServiceScopeFactory scopeFactory,
        IDataProtectionProvider protectionProvider,
        IMemoryCache cache,
        ILogger<PaymentProviderConfigService> logger)
    {
        _scopeFactory = scopeFactory;
        _protector = protectionProvider.CreateProtector("HostPanel.PaymentProviderConfig.v1");
        _cache = cache;
        _logger = logger;
    }

    public record Resolved(string ClientId, string ClientSecret, bool Sandbox, string? WebhookId);

    /// <summary>Get the active config for a provider (the Enabled=true row), null if none.</summary>
    public async Task<Resolved?> GetActiveAsync(string provider, CancellationToken ct = default)
    {
        var key = $"ppconfig:{provider}:active";
        if (_cache.TryGetValue(key, out Resolved? cached)) return cached;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Set<PaymentProviderConfig>()
            .Where(p => p.Provider == provider && p.Enabled)
            .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            _cache.Set(key, (Resolved?)null, CacheTtl);
            return null;
        }
        try
        {
            var secret = _protector.Unprotect(row.ProtectedSecret);
            var resolved = new Resolved(row.ClientId, secret, row.Sandbox, row.WebhookId);
            _cache.Set(key, resolved, CacheTtl);
            return resolved;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect secret for {Provider} (id={Id}). Operator must re-save credentials.", provider, row.Id);
            return null;
        }
    }

    /// <summary>List all rows for the Settings UI (secret returned MASKED, never plaintext).</summary>
    public async Task<List<(int Id, string Provider, bool Sandbox, string ClientId, string MaskedSecret, string? WebhookId, bool Enabled, DateTime? LastValidatedAt, string? LastValidationMessage)>> ListMaskedAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Set<PaymentProviderConfig>()
            .OrderBy(p => p.Provider).ThenBy(p => p.Sandbox)
            .ToListAsync(ct);

        return rows.Select(r => (
            r.Id, r.Provider, r.Sandbox, r.ClientId,
            MaskedSecret: MaskSecret(r.ProtectedSecret),
            r.WebhookId, r.Enabled, r.LastValidatedAt, r.LastValidationMessage
        )).ToList();
    }

    /// <summary>Upsert config for (Provider, Sandbox). Secret is IDataProtector-wrapped before write.</summary>
    public async Task<int> UpsertAsync(string provider, bool sandbox, string clientId, string clientSecret, string? webhookId, bool enabled, DateTime? validatedAt, string? validationMessage, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.Set<PaymentProviderConfig>()
            .FirstOrDefaultAsync(p => p.Provider == provider && p.Sandbox == sandbox, ct);
        var protectedSecret = _protector.Protect(clientSecret);

        if (existing is null)
        {
            existing = new PaymentProviderConfig
            {
                Provider = provider,
                Sandbox = sandbox,
                ClientId = clientId,
                ProtectedSecret = protectedSecret,
                WebhookId = webhookId,
                Enabled = enabled,
                LastValidatedAt = validatedAt,
                LastValidationMessage = validationMessage
            };
            db.Set<PaymentProviderConfig>().Add(existing);
        }
        else
        {
            existing.ClientId = clientId;
            existing.ProtectedSecret = protectedSecret;
            existing.WebhookId = webhookId;
            existing.Enabled = enabled;
            existing.LastValidatedAt = validatedAt;
            existing.LastValidationMessage = validationMessage;
        }

        // If this row is Enabled, disable all sibling rows for the same provider (different
        // Sandbox value) so there's exactly one active credential set per provider.
        if (enabled)
        {
            var siblings = await db.Set<PaymentProviderConfig>()
                .Where(p => p.Provider == provider && p.Sandbox != sandbox && p.Enabled)
                .ToListAsync(ct);
            foreach (var s in siblings) s.Enabled = false;
        }

        await db.SaveChangesAsync(ct);
        InvalidateCache(provider);
        return existing.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Set<PaymentProviderConfig>().FindAsync(new object?[] { id }, ct);
        if (row is null) return;
        db.Set<PaymentProviderConfig>().Remove(row);
        await db.SaveChangesAsync(ct);
        InvalidateCache(row.Provider);
    }

    public void InvalidateCache(string provider)
    {
        _cache.Remove($"ppconfig:{provider}:active");
    }

    private static string MaskSecret(string protectedSecret)
    {
        // We never reveal the real secret over the wire. The protected string IS opaque
        // ciphertext, but to make the UI even less ambiguous we return a fixed mask.
        return string.IsNullOrEmpty(protectedSecret) ? "" : "••••••••••••••";
    }
}
