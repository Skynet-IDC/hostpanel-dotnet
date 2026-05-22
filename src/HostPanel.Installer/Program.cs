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
builder.Services.AddSingleton<SseProgressBroker>();
builder.Services.AddSingleton<InstallStateStore>();
builder.Services.AddSingleton<InstallOrchestrator>();

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
app.MapPost("/wizard/confirm", async (HttpRequest req, InstallOrchestrator orch) =>
{
    var form = await req.ReadFormAsync();
    var action = form["action"].ToString();
    if (action != "install") return Results.Redirect("/wizard/confirm");
    if (!orch.TryStart()) return Results.Redirect("/wizard/progress"); // already running
    return Results.Redirect("/wizard/progress");
});

app.MapGet("/wizard/progress", () => Results.Content(ProgressPage.Render(), "text/html"));

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

// SSE stream of install events. Each subscriber gets the in-progress history
// replayed first (so reloads don't lose context), then live updates.
app.MapGet("/api/install/events", async (HttpResponse resp, SseProgressBroker broker, CancellationToken ct) =>
{
    resp.Headers["Cache-Control"] = "no-cache";
    resp.Headers["X-Accel-Buffering"] = "no";
    resp.ContentType = "text/event-stream";

    var queue = new System.Threading.Channels.Channel<InstallProgressEvent>[1];
    queue[0] = System.Threading.Channels.Channel.CreateUnbounded<InstallProgressEvent>();

    using var sub = broker.Subscribe(async ev =>
    {
        await queue[0].Writer.WriteAsync(ev, ct);
    }, out var backlog);

    foreach (var ev in backlog)
    {
        await resp.WriteAsync(SseProgressBroker.SerializeForSse(ev), ct);
    }
    await resp.Body.FlushAsync(ct);

    try
    {
        await foreach (var ev in queue[0].Reader.ReadAllAsync(ct))
        {
            await resp.WriteAsync(SseProgressBroker.SerializeForSse(ev), ct);
            await resp.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { /* client disconnect */ }
});

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
