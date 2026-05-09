using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Middleware;

/// <summary>
/// Centralized tenant authorization. Ensures users can only access their own resources.
/// Admin sees everything. Reseller sees own + their customers'. Customer sees only own.
/// </summary>
public class TenantAuthorizationService
{
    private readonly AppDbContext _db;

    public TenantAuthorizationService(AppDbContext db) => _db = db;

    public async Task<bool> CanAccessDomainAsync(ClaimsPrincipal user, int domainId)
    {
        var domain = await _db.Domains
            .Include(d => d.Subscription).ThenInclude(s => s.User)
            .FirstOrDefaultAsync(d => d.Id == domainId);
        return domain is not null && CanAccessSubscriptionOwner(user, domain.Subscription);
    }

    public async Task<bool> CanAccessSubscriptionAsync(ClaimsPrincipal user, int subscriptionId)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);
        return sub is not null && CanAccessSubscriptionOwner(user, sub);
    }

    public async Task<bool> CanAccessMailboxAsync(ClaimsPrincipal user, int mailboxId)
    {
        var mailbox = await _db.Mailboxes
            .Include(m => m.Domain).ThenInclude(d => d.Subscription).ThenInclude(s => s.User)
            .FirstOrDefaultAsync(m => m.Id == mailboxId);
        return mailbox is not null && CanAccessSubscriptionOwner(user, mailbox.Domain.Subscription);
    }

    public async Task<bool> CanAccessDatabaseAsync(ClaimsPrincipal user, int databaseId)
    {
        var db = await _db.HostedDatabases
            .Include(d => d.Subscription).ThenInclude(s => s.User)
            .FirstOrDefaultAsync(d => d.Id == databaseId);
        return db is not null && CanAccessSubscriptionOwner(user, db.Subscription);
    }

    /// <summary>
    /// Filter a subscription query to only include accessible subscriptions for the current user.
    /// </summary>
    public IQueryable<Subscription> FilterSubscriptions(ClaimsPrincipal user, IQueryable<Subscription> query)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = user.FindFirstValue(ClaimTypes.Role);

        if (role == UserRole.Admin.ToString()) return query;
        if (role == UserRole.Reseller.ToString())
            return query.Where(s => s.UserId == userId || s.User.ParentUserId == userId);
        return query.Where(s => s.UserId == userId);
    }

    /// <summary>
    /// Filter a domain query to only include accessible domains for the current user.
    /// </summary>
    public IQueryable<Domain> FilterDomains(ClaimsPrincipal user, IQueryable<Domain> query)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = user.FindFirstValue(ClaimTypes.Role);

        if (role == UserRole.Admin.ToString()) return query;
        if (role == UserRole.Reseller.ToString())
            return query.Where(d => d.Subscription.UserId == userId || d.Subscription.User.ParentUserId == userId);
        return query.Where(d => d.Subscription.UserId == userId);
    }

    private static bool CanAccessSubscriptionOwner(ClaimsPrincipal user, Subscription sub)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = user.FindFirstValue(ClaimTypes.Role);

        if (role == UserRole.Admin.ToString()) return true;
        if (role == UserRole.Reseller.ToString())
            return sub.UserId == userId || sub.User?.ParentUserId == userId;
        return sub.UserId == userId;
    }
}
