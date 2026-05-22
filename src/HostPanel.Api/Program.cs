using System.Text;
using OpenTelemetry.Trace;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Infrastructure.Services;
using HostPanel.Infrastructure.Services.Migration;
using HostPanel.Modules.Billing.Providers;
using HostPanel.Modules.Billing.Services;
using HostPanel.Modules.Database;
using HostPanel.Modules.Dns;
using HostPanel.Modules.Mail;
using HostPanel.Modules.FileManager;
using HostPanel.Modules.Ssl;
using HostPanel.Modules.Web;

var builder = WebApplication.CreateBuilder(args);

// Systemd readiness (sends READY=1 / STOPPING=1; no-op outside systemd)
builder.Host.UseSystemd();

// Sentry — only initializes when a DSN is configured (operator can disable by leaving blank)
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    builder.WebHost.UseSentry(o =>
    {
        o.Dsn = sentryDsn;
        o.Environment = builder.Environment.EnvironmentName;
        o.TracesSampleRate = builder.Environment.IsDevelopment() ? 1.0 : 0.1;
        o.SendDefaultPii = false; // never ship request bodies / headers / user identifiers
        o.MaxBreadcrumbs = 50;
        o.Debug = builder.Environment.IsDevelopment();
        o.AttachStacktrace = true;
        // Filter noisy auth-redirect 401s — they're not bugs, they're auth flow
        o.SetBeforeSend(e => e.Logger?.Contains("Microsoft.AspNetCore.Authentication") == true ? null : e);
    });
}

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// EF Core + SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

// ASP.NET Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT key not configured");

// Multi-scheme auth: routes to ApiKey or JWT based on header prefix
const string MultiAuthScheme = "MultiAuth";

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = MultiAuthScheme;
    options.DefaultChallengeScheme = MultiAuthScheme;
})
.AddPolicyScheme(MultiAuthScheme, MultiAuthScheme, options =>
{
    options.ForwardDefaultSelector = ctx =>
    {
        string? auth = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (auth is not null && auth.StartsWith("Bearer hp_", StringComparison.Ordinal))
            return "ApiKey";
        return JwtBearerDefaults.AuthenticationScheme;
    };
})
.AddJwtBearer(options =>
{
    options.MapInboundClaims = false; // Prevent remapping claim types
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        NameClaimType = System.Security.Claims.ClaimTypes.Name,
        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
})
.AddScheme<AuthenticationSchemeOptions, HostPanel.Api.Middleware.ApiKeyAuthHandler>("ApiKey", null);

// Authentik OIDC (optional - only if configured)
var authentikAuthority = builder.Configuration["Sso:Authentik:Authority"];
if (!string.IsNullOrEmpty(authentikAuthority))
{
    authBuilder.AddOpenIdConnect(options =>
    {
        options.Authority = authentikAuthority;
        options.ClientId = builder.Configuration["Sso:Authentik:ClientId"] ?? "";
        options.ClientSecret = builder.Configuration["Sso:Authentik:ClientSecret"] ?? "";
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.GetClaimsFromUserInfoEndpoint = true;
    });
}

// Default auth policy uses the PolicyScheme which routes to the right handler
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(MultiAuthScheme)
        .RequireAuthenticatedUser()
        .Build();
});

// Tenant authorization
builder.Services.AddScoped<HostPanel.Api.Middleware.TenantAuthorizationService>();

// Services
builder.Services.AddScoped<JwtService>();
builder.Services.AddSingleton<IServerAgent, LocalServerAgent>();
builder.Services.AddSingleton<ServerAgentFactory>();
builder.Services.AddScoped<IReconciler, SubscriptionReconciler>();
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddHostedService<ReconciliationBackgroundService>();
builder.Services.AddScoped<SslService>();
builder.Services.AddScoped<FileManagerService>();
builder.Services.AddSingleton<PowerDnsProvider>();
builder.Services.AddSingleton<CloudflareDnsProvider>();
builder.Services.AddSingleton<IDnsProvider>(sp => sp.GetRequiredService<PowerDnsProvider>());
builder.Services.AddSingleton<IDnsProvider>(sp => sp.GetRequiredService<CloudflareDnsProvider>());
builder.Services.AddSingleton<DnsProviderFactory>();
builder.Services.AddScoped<MailService>();
builder.Services.AddScoped<WordPressToolkit>();
builder.Services.AddSingleton<IDbProvisioningService, MsSqlProvisioner>();
builder.Services.AddSingleton<IDbProvisioningService, MySqlProvisioner>();
builder.Services.AddSingleton<IDbProvisioningService, MariaDbProvisioner>();
builder.Services.AddSingleton<IDbProvisioningService, PostgreSqlProvisioner>();
builder.Services.AddSingleton<IDbProvisioningService, SqliteProvisioner>();
builder.Services.AddSingleton<DbProvisioningFactory>();
builder.Services.AddSingleton<IPaymentProvider, PayPalInvoicingProvider>();
builder.Services.AddSingleton<IPaymentProvider, StripeProvider>();
builder.Services.AddHostedService<BillingCycleService>();
builder.Services.AddHostedService<SslRenewalBackgroundService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<PleskMigrationService>();
builder.Services.AddHttpClient("plesk-migration").ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });
builder.Services.AddHttpClient("cloudflare");
builder.Services.AddScoped<WebmailService>();
builder.Services.AddScoped<UsageTrackingService>();
builder.Services.AddScoped<IBillingMeter, UsageTrackingService>();
builder.Services.AddHostedService<UsageTrackingBackgroundService>();
builder.Services.AddHttpClient("Webhooks");
builder.Services.AddSingleton<WebhookDispatcher>();
builder.Services.AddScoped<EmailNotificationService>();
builder.Services.AddScoped<IFailoverStrategy, DnsSwapStrategy>();
builder.Services.AddScoped<IFailoverStrategy, FloatingIpStrategy>();
builder.Services.AddScoped<IFailoverStrategy, CloudflareFailoverStrategy>();
builder.Services.AddScoped<IFailoverStrategy, PerDomainFailoverStrategy>();
builder.Services.AddScoped<FailoverStrategyFactory>();
builder.Services.AddScoped<HaService>();
builder.Services.AddHostedService<HaHealthMonitorService>();

// Controllers + SignalR
// BUG-007 fix: PascalCase controllers like ServicePlansController generate routes at
// /api/v1/ServicePlans. Add a convention that ALSO registers a kebab-case alias
// (/api/v1/service-plans) so scripted REST callers using either work.
builder.Services.AddScoped<HostPanel.Api.Middleware.AuditLogFilter>();
builder.Services.AddControllers(o =>
    {
        o.Filters.Add<HostPanel.Api.Middleware.AuditLogFilter>();
        o.Conventions.Add(new HostPanel.Api.Middleware.KebabCaseControllerRouteConvention());
    })
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSignalR();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();
    });

// OpenAPI
builder.Services.AddOpenApi();

// CORS for React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                ?? ["http://localhost:5173"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Apply migrations and seed roles/admin on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Skip MigrateAsync for InMemory provider (used in tests)
    if (db.Database.IsRelational())
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in Enum.GetNames<UserRole>())
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Seed admin user only when explicit Admin:Email + Admin:Password are configured.
    // Fresh installs intentionally leave the system admin-less so the first visitor
    // creates the admin via /api/v1/setup/bootstrap-admin (BootstrapController).
    // Backward compat: existing installs that set those config values still get seeded.
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var configuredEmail = builder.Configuration["Admin:Email"];
    var configuredPassword = builder.Configuration["Admin:Password"];
    if (!string.IsNullOrWhiteSpace(configuredEmail) &&
        !string.IsNullOrWhiteSpace(configuredPassword) &&
        !userManager.Users.Any(u => u.Role == UserRole.Admin))
    {
        var admin = new AppUser
        {
            UserName = configuredEmail,
            Email = configuredEmail,
            FirstName = "System",
            LastName = "Admin",
            Role = UserRole.Admin,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, configuredPassword);
        if (result.Succeeded)
            await userManager.AddToRoleAsync(admin, UserRole.Admin.ToString());
    }

    // BUG-011 fix: seed a self-Server row with IsLocal=true so HA configure-failover
    // can find the primary. Idempotent — only creates if no IsLocal row exists.
    var hasLocalServer = await db.Set<HostPanel.Core.Models.Server>().AnyAsync(s => s.IsLocal);
    if (!hasLocalServer)
    {
        var hostname = System.Net.Dns.GetHostName();
        string ipAddress = "127.0.0.1";
        try
        {
            ipAddress = System.Net.Dns.GetHostAddresses(hostname)
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                            && !System.Net.IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .FirstOrDefault() ?? "127.0.0.1";
        }
        catch { /* fall through to loopback */ }

        db.Set<HostPanel.Core.Models.Server>().Add(new HostPanel.Core.Models.Server
        {
            Hostname = hostname,
            IpAddress = ipAddress,
            AgentPort = 50051,
            AgentToken = builder.Configuration["Agent:Token"] ?? "",
            ServerGroup = "primary",
            IsLocal = true,
            IsActive = true,
            IsHealthy = true,
            LastHealthCheck = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Honor X-Forwarded-* headers from nginx so OIDC redirect_uri uses the public scheme + host
// (otherwise .NET sees `http://localhost` from the local nginx proxy and Authentik 400s).
// ForwardedHeadersOptions lives in Microsoft.AspNetCore.Builder; constants in HttpOverrides.
{
    var fwd = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                           | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                           | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
    };
    fwd.KnownProxies.Add(System.Net.IPAddress.Loopback);
    fwd.KnownNetworks.Clear(); // trust any upstream — nginx is the only proxy on this box
    fwd.KnownProxies.Clear();
    app.UseForwardedHeaders(fwd);
}

app.UseCors("AllowFrontend");
if (!app.Environment.IsEnvironment("Testing"))
    app.UseMiddleware<HostPanel.Api.Middleware.RateLimitingMiddleware>();
// WebSockets — needed for ClaudeController's /api/v1/claude/terminal/{subId} PTY bridge.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<HostPanel.Api.Hubs.LogStreamHub>("/hubs/logs");

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
