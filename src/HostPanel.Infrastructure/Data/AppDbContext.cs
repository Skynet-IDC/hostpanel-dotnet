using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Models;

namespace HostPanel.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ServicePlan> ServicePlans => Set<ServicePlan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Domain> Domains => Set<Domain>();
    public DbSet<DnsZone> DnsZones => Set<DnsZone>();
    public DbSet<DnsRecord> DnsRecords => Set<DnsRecord>();
    public DbSet<Mailbox> Mailboxes => Set<Mailbox>();
    public DbSet<MailAlias> MailAliases => Set<MailAlias>();
    public DbSet<HostedDatabase> HostedDatabases => Set<HostedDatabase>();
    public DbSet<SslCertificate> SslCertificates => Set<SslCertificate>();
    public DbSet<Backup> Backups => Set<Backup>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DesiredState> DesiredStates => Set<DesiredState>();
    public DbSet<SmtpRelayConfig> SmtpRelayConfigs => Set<SmtpRelayConfig>();
    public DbSet<CronJob> CronJobs => Set<CronJob>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WordPressSite> WordPressSites => Set<WordPressSite>();
    public DbSet<FtpAccount> FtpAccounts => Set<FtpAccount>();
    public DbSet<GitDeployment> GitDeployments => Set<GitDeployment>();
    public DbSet<DockerContainer> DockerContainers => Set<DockerContainer>();
    public DbSet<ServerIpAddress> ServerIpAddresses => Set<ServerIpAddress>();
    public DbSet<IpBlacklistEntry> IpBlacklistEntries => Set<IpBlacklistEntry>();
    public DbSet<TotpToken> TotpTokens => Set<TotpToken>();
    public DbSet<BrandingConfig> BrandingConfigs => Set<BrandingConfig>();
    public DbSet<AppConfig> AppConfigs => Set<AppConfig>();
    public DbSet<HaSyncState> HaSyncStates => Set<HaSyncState>();
    public DbSet<HaPairingToken> HaPairingTokens => Set<HaPairingToken>();
    public DbSet<PaymentProviderConfig> PaymentProviderConfigs => Set<PaymentProviderConfig>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<CustomerBranding> CustomerBrandings => Set<CustomerBranding>();
    public DbSet<MigrationSource> MigrationSources => Set<MigrationSource>();
    public DbSet<MigrationJob> MigrationJobs => Set<MigrationJob>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // CustomerBranding
        builder.Entity<CustomerBranding>(e =>
        {
            e.HasIndex(c => c.UserId).IsUnique();
            e.HasOne(c => c.User)
                .WithOne()
                .HasForeignKey<CustomerBranding>(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SystemSetting — unique index on Key (already created in migration 20260517010000)
        builder.Entity<SystemSetting>(e =>
        {
            e.HasIndex(s => s.Key).IsUnique();
        });

        // MigrationJob — FK to MigrationSource, indexes for common lookups
        builder.Entity<MigrationJob>(e =>
        {
            e.HasOne(j => j.MigrationSource)
                .WithMany()
                .HasForeignKey(j => j.MigrationSourceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => j.State);
            e.HasIndex(j => new { j.MigrationSourceId, j.SourceDomain });
        });

        // AppUser
        builder.Entity<AppUser>(e =>
        {
            e.HasMany(u => u.Subscriptions)
                .WithOne(s => s.User)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(u => u.ParentUser)
                .WithMany()
                .HasForeignKey(u => u.ParentUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(u => u.ApiKeys)
                .WithOne(k => k.User)
                .HasForeignKey(k => k.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Subscription
        builder.Entity<Subscription>(e =>
        {
            e.HasOne(s => s.ServicePlan)
                .WithMany(p => p.Subscriptions)
                .HasForeignKey(s => s.ServicePlanId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(s => s.Domains)
                .WithOne(d => d.Subscription)
                .HasForeignKey(d => d.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(s => s.Databases)
                .WithOne(d => d.Subscription)
                .HasForeignKey(d => d.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(s => s.Backups)
                .WithOne(b => b.Subscription)
                .HasForeignKey(b => b.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(s => s.Invoices)
                .WithOne(i => i.Subscription)
                .HasForeignKey(i => i.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Domain
        builder.Entity<Domain>(e =>
        {
            e.HasIndex(d => d.Name).IsUnique();

            e.HasMany(d => d.DnsZones)
                .WithOne(z => z.Domain)
                .HasForeignKey(z => z.DomainId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(d => d.Mailboxes)
                .WithOne(m => m.Domain)
                .HasForeignKey(m => m.DomainId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(d => d.MailAliases)
                .WithOne(a => a.Domain)
                .HasForeignKey(a => a.DomainId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(d => d.SslCertificate)
                .WithOne(c => c.Domain)
                .HasForeignKey<SslCertificate>(c => c.DomainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // DnsZone
        builder.Entity<DnsZone>(e =>
        {
            e.HasMany(z => z.Records)
                .WithOne(r => r.DnsZone)
                .HasForeignKey(r => r.DnsZoneId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Invoice
        builder.Entity<Invoice>(e =>
        {
            e.Property(i => i.Amount).HasPrecision(18, 2);
        });

        // ServicePlan
        builder.Entity<ServicePlan>(e =>
        {
            e.Property(p => p.PricePerPeriod).HasPrecision(18, 2);
        });

        // ApiKey
        builder.Entity<ApiKey>(e =>
        {
            e.HasIndex(k => k.KeyPrefix);
        });

        // DesiredState
        builder.Entity<DesiredState>(e =>
        {
            e.HasIndex(d => new { d.ResourceType, d.ResourceId }).IsUnique();
            e.HasIndex(d => d.State);
            e.HasIndex(d => d.NextRetryAt);
        });

        // HaPairingToken — unique on Token (lookup by raw value), and index on ExpiresAt so
        // a cleanup job can find stale rows cheaply.
        builder.Entity<HaPairingToken>(e =>
        {
            e.HasIndex(t => t.Token).IsUnique();
            e.HasIndex(t => t.ExpiresAt);
        });

        // PaymentProviderConfig — unique on (Provider, Sandbox) so each combination of
        // provider+environment has exactly one credential row.
        builder.Entity<PaymentProviderConfig>(e =>
        {
            e.HasIndex(p => new { p.Provider, p.Sandbox }).IsUnique();
        });
    }

    public override int SaveChanges()
    {
        SetTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void SetTimestamps()
    {
        var entries = ChangeTracker.Entries<BaseEntity>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
    }
}
