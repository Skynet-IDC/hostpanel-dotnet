using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using HostPanel.Api.Middleware;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Tests;

public class TenantAuthorizationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TenantAuthorizationService _auth;

    public TenantAuthorizationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _auth = new TenantAuthorizationService(_db);

        SeedData();
    }

    private void SeedData()
    {
        var admin = new AppUser { Id = "admin-1", Email = "admin@test.com", UserName = "admin@test.com", Role = UserRole.Admin };
        var reseller = new AppUser { Id = "reseller-1", Email = "reseller@test.com", UserName = "reseller@test.com", Role = UserRole.Reseller };
        var customer = new AppUser { Id = "customer-1", Email = "customer@test.com", UserName = "customer@test.com", Role = UserRole.Customer, ParentUserId = "reseller-1" };
        var otherCustomer = new AppUser { Id = "customer-2", Email = "other@test.com", UserName = "other@test.com", Role = UserRole.Customer, ParentUserId = "reseller-1" };

        _db.Users.AddRange(admin, reseller, customer, otherCustomer);

        var plan = new ServicePlan { Id = 1, Name = "Basic", DiskQuotaMb = 1024, BandwidthLimitMb = 10240, MaxDomains = 5, MaxEmailAccounts = 10, MaxDatabases = 3 };
        _db.ServicePlans.Add(plan);

        var sub = new Subscription { Id = 1, UserId = "customer-1", ServicePlanId = 1, Status = SubscriptionStatus.Active, SystemUsername = "hp_test" };
        _db.Subscriptions.Add(sub);

        var domain = new Domain { Id = 1, SubscriptionId = 1, Name = "test.com", DocumentRoot = "/var/www/hp_test/httpdocs" };
        _db.Domains.Add(domain);

        _db.SaveChanges();
    }

    private static ClaimsPrincipal CreateUser(string userId, string role)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task Admin_CanAccessAnyDomain()
    {
        var admin = CreateUser("admin-1", "Admin");
        Assert.True(await _auth.CanAccessDomainAsync(admin, 1));
    }

    [Fact]
    public async Task Customer_CanAccessOwnDomain()
    {
        var customer = CreateUser("customer-1", "Customer");
        Assert.True(await _auth.CanAccessDomainAsync(customer, 1));
    }

    [Fact]
    public async Task OtherCustomer_CannotAccessDomain()
    {
        var other = CreateUser("customer-2", "Customer");
        Assert.False(await _auth.CanAccessDomainAsync(other, 1));
    }

    [Fact]
    public async Task Reseller_CanAccessCustomerDomain()
    {
        var reseller = CreateUser("reseller-1", "Reseller");
        Assert.True(await _auth.CanAccessDomainAsync(reseller, 1));
    }

    [Fact]
    public async Task NonexistentDomain_ReturnsFalse()
    {
        var admin = CreateUser("admin-1", "Admin");
        Assert.False(await _auth.CanAccessDomainAsync(admin, 999));
    }

    public void Dispose() => _db.Dispose();
}
