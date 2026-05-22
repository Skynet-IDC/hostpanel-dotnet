// F3 web installer — Stage 3 (wizard form pages).
// Kestrel on 0.0.0.0:8080. Renders wizard form pages, accepts POSTs to
// persist operator choices into WizardState (in-memory singleton — Stage 4
// adds disk persistence), exposes /api/preflight + /api/wizard/* JSON.

using HostPanel.Installer.Pages;
using HostPanel.Installer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddSingleton<PreflightService>();
builder.Services.AddSingleton<WizardState>();

const string bindUrl = "http://0.0.0.0:8080";
builder.WebHost.UseUrls(bindUrl);

var app = builder.Build();

// === Wizard pages (GET = render, POST = persist + redirect) ===

app.MapGet("/", () => Results.Redirect("/welcome"));

app.MapGet("/welcome", (WizardState s) => Results.Content(WelcomePage.Render(s), "text/html"));
app.MapPost("/wizard/welcome", async (HttpRequest req, WizardState s) =>
{
    var form = await req.ReadFormAsync();
    WelcomePage.ApplyForm(s, form);
    return Results.Redirect("/preflight");
});

app.MapGet("/preflight", (WizardState s) => Results.Content(PreflightPage.Render(), "text/html"));

app.MapGet("/wizard/database", (WizardState s) =>
{
    if (s.Role != InstallRole.Primary) return Results.Redirect("/wizard/network");
    return Results.Content(DatabasePage.Render(s), "text/html");
});
app.MapPost("/wizard/database", async (HttpRequest req, WizardState s) =>
{
    var form = await req.ReadFormAsync();
    DatabasePage.ApplyForm(s, form);
    return Results.Redirect(s.NextAfter("database"));
});

app.MapGet("/wizard/network", (WizardState s) => Results.Content(NetworkPage.Render(s), "text/html"));
app.MapPost("/wizard/network", async (HttpRequest req, WizardState s) =>
{
    var form = await req.ReadFormAsync();
    NetworkPage.ApplyForm(s, form);
    return Results.Redirect(s.NextAfter("network"));
});

app.MapGet("/wizard/security", (WizardState s) => Results.Content(SecurityPage.Render(s), "text/html"));
app.MapPost("/wizard/security", async (HttpRequest req, WizardState s) =>
{
    var form = await req.ReadFormAsync();
    SecurityPage.ApplyForm(s, form);
    return Results.Redirect(s.NextAfter("security"));
});

app.MapGet("/wizard/integrations", (WizardState s) => Results.Content(IntegrationsPage.Render(s), "text/html"));
app.MapPost("/wizard/integrations", async (HttpRequest req, WizardState s) =>
{
    var form = await req.ReadFormAsync();
    IntegrationsPage.ApplyForm(s, form);
    return Results.Redirect(s.NextAfter("integrations"));
});

app.MapGet("/wizard/admin", (WizardState s) =>
{
    if (s.Role != InstallRole.Primary) return Results.Redirect("/wizard/standby");
    return Results.Content(AdminPage.Render(s), "text/html");
});
app.MapPost("/wizard/admin", async (HttpRequest req, WizardState s) =>
{
    var form = await req.ReadFormAsync();
    AdminPage.ApplyForm(s, form);
    return Results.Redirect(s.NextAfter("admin"));
});

app.MapGet("/wizard/standby", (WizardState s) =>
{
    if (s.Role != InstallRole.Standby) return Results.Redirect("/wizard/admin");
    return Results.Content(StandbyLinkPage.Render(s), "text/html");
});
app.MapPost("/wizard/standby", async (HttpRequest req, WizardState s) =>
{
    var form = await req.ReadFormAsync();
    StandbyLinkPage.ApplyForm(s, form);
    return Results.Redirect(s.NextAfter("standby"));
});

app.MapGet("/wizard/confirm", (WizardState s) => Results.Content(ConfirmPage.Render(s), "text/html"));
app.MapPost("/wizard/confirm", async (HttpRequest req, WizardState s) =>
{
    var form = await req.ReadFormAsync();
    var action = form["action"].ToString();
    // Stage 4 will pick this up and start the orchestrator. For now just acknowledge.
    return Results.Ok(new { acknowledged = true, action, state = "Stage 4 orchestrator not yet wired" });
});

// Dry-run preview — Stage 4 fills this in. For now redirects back with a note.
app.MapGet("/wizard/dryrun", (WizardState s) => Results.Content(
    HtmlLayout.RenderPage(
        title: "Dry-run preview",
        bodyContent: """
<h1>Dry-run preview</h1>
<p class='sub'>Stage 4 will list every command that would run, in order, without
mutating the box. Not yet implemented.</p>
<div class='actions'><a class='btn' href='/wizard/confirm'>← Back to Confirm</a></div>
""",
        currentStep: 8, totalSteps: 9),
    "text/html"));

// === JSON APIs ===

app.MapGet("/api/preflight", async (PreflightService svc, WizardState s, CancellationToken ct) =>
{
    var report = await svc.GatherAsync(ct);
    // Cache in wizard state so later pages can read RAM/swap/interfaces for prompts.
    s.CachedPreflight = report;
    return Results.Json(report, new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    });
});

app.MapGet("/api/wizard/state", (WizardState s) =>
    Results.Json(s, new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    }));

app.MapGet("/api/wizard/next", (WizardState s, string from) =>
    Results.Json(new { next = s.NextAfter(from), role = s.Role.ToString() }));

app.MapGet("/healthz", () => Results.Ok(new
{
    ok = true,
    stage = "wizard-forms",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev",
    boundAt = bindUrl
}));

Console.WriteLine();
Console.WriteLine("================================================================");
Console.WriteLine("  HostPanel Installer — Stage 3 (wizard form pages)");
Console.WriteLine("================================================================");
Console.WriteLine($"  Listening on {bindUrl}");
Console.WriteLine("  Open this URL in a browser to begin install.");
Console.WriteLine("  Press Ctrl-C to abort.");
Console.WriteLine("================================================================");
Console.WriteLine();

app.Run();

public partial class Program { }
