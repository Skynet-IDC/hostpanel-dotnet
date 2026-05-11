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

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// EF Core + SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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
builder.Services.AddControllers();
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

    // Seed admin user if none exists
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    if (!userManager.Users.Any(u => u.Role == UserRole.Admin))
    {
        var adminEmail = builder.Configuration["Admin:Email"] ?? "admin@hostpanel.local";
        var adminPassword = builder.Configuration["Admin:Password"] ?? "REDACTED-PASSWORD";

        var admin = new AppUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "System",
            LastName = "Admin",
            Role = UserRole.Admin,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (result.Succeeded)
            await userManager.AddToRoleAsync(admin, UserRole.Admin.ToString());
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");
if (!app.Environment.IsEnvironment("Testing"))
    app.UseMiddleware<HostPanel.Api.Middleware.RateLimitingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<HostPanel.Api.Hubs.LogStreamHub>("/hubs/logs");

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
