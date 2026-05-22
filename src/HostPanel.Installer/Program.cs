// F3 web installer — Stage 6 (CLI flags + headless + dry-run + uninstall + reconfigure).
// Default boots Kestrel on :8080 (wizard). Flags select alternate modes.

using HostPanel.Installer.Pages;
using HostPanel.Installer.Services;

// === Dispatch on CLI flags BEFORE building the web host ===

CliOptions opts;
try
{
    opts = CliOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.HelpText());
    return 1;
}

if (opts.Help)
{
    Console.WriteLine(CliOptions.HelpText());
    return 0;
}

if (opts.Mode == CliMode.Uninstall)
{
    return Uninstaller.Run(opts.SkipConfirm);
}

if (opts.Mode == CliMode.Headless)
{
    return await HeadlessRunner.RunAsync(opts.ConfigPath!, opts.DryRun, CancellationToken.None);
}

// Wizard / DryRun / Reconfigure modes all boot the web host.

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
builder.Services.AddSingleton(opts);

const string bindUrl = "http://0.0.0.0:8080";
builder.WebHost.UseUrls(bindUrl);

var app = builder.Build();

// === Wizard pages ===

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
app.MapPost("/wizard/confirm", async (HttpRequest req, InstallOrchestrator orch, CliOptions cli) =>
{
    var form = await req.ReadFormAsync();
    var action = form["action"].ToString();
    if (action != "install") return Results.Redirect("/wizard/confirm");
    // Honor the global --dry-run flag (Wizard+DryRun mode) AND a per-run dry-run choice.
    var dryRun = cli.DryRun;
    if (!orch.TryStart(dryRun)) return Results.Redirect("/wizard/progress");
    return Results.Redirect("/wizard/progress");
});

app.MapGet("/wizard/progress", () => Results.Content(ProgressPage.Render(), "text/html"));

// Dry-run preview page — also honored in wizard/install POST when --dry-run was passed.
app.MapGet("/wizard/dryrun", (WizardState s) => Results.Content(
    HtmlLayout.RenderPage(
        title: "Dry-run preview",
        bodyContent: """
<h1>Dry-run preview</h1>
<p class='sub'>The Install button will run in dry-run mode — every step logs what
it would do but mutates nothing. Use this to preview a config in CI before the
real run. Pass <code>--dry-run</code> on the command line OR click Install from
the Confirm page after running this installer with <code>--dry-run</code>.</p>
<div class='actions'><a class='btn' href='/wizard/confirm'>← Back to Confirm</a></div>
""",
        currentStep: 8, totalSteps: 9),
    "text/html"));

// === JSON APIs ===

app.MapGet("/api/preflight", async (PreflightService svc, WizardState s, CancellationToken ct) =>
{
    var report = await svc.GatherAsync(ct);
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

// "Save my answers" — downloads the current WizardState as install.yaml.
app.MapGet("/api/wizard/export.yaml", (WizardState s) =>
    Results.File(
        System.Text.Encoding.UTF8.GetBytes(ConfigYaml.Save(s)),
        "application/x-yaml",
        "install.yaml"));

// SSE stream of install events.
app.MapGet("/api/install/events", async (HttpResponse resp, SseProgressBroker broker, CancellationToken ct) =>
{
    resp.Headers["Cache-Control"] = "no-cache";
    resp.Headers["X-Accel-Buffering"] = "no";
    resp.ContentType = "text/event-stream";

    var queue = System.Threading.Channels.Channel.CreateUnbounded<InstallProgressEvent>();

    using var sub = broker.Subscribe(async ev => { await queue.Writer.WriteAsync(ev, ct); }, out var backlog);

    foreach (var ev in backlog)
        await resp.WriteAsync(SseProgressBroker.SerializeForSse(ev), ct);
    await resp.Body.FlushAsync(ct);

    try
    {
        await foreach (var ev in queue.Reader.ReadAllAsync(ct))
        {
            await resp.WriteAsync(SseProgressBroker.SerializeForSse(ev), ct);
            await resp.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { /* client disconnect */ }
});

app.MapGet("/healthz", (CliOptions cli) => Results.Ok(new
{
    ok = true,
    stage = "cli-flags",
    mode = cli.Mode.ToString(),
    dryRun = cli.DryRun,
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev",
    boundAt = bindUrl
}));

Console.WriteLine();
Console.WriteLine("================================================================");
var modeBanner = opts.Mode switch
{
    CliMode.DryRun => "Stage 6 (DRY-RUN — no mutations will occur)",
    CliMode.Reconfigure => "Stage 6 (reconfigure — wizard for an existing install)",
    _ => "Stage 6 (wizard)"
};
Console.WriteLine($"  HostPanel Installer — {modeBanner}");
Console.WriteLine("================================================================");
Console.WriteLine($"  Listening on {bindUrl}");
Console.WriteLine("  Open this URL in a browser to begin install.");
Console.WriteLine("  Press Ctrl-C to abort.");
Console.WriteLine("================================================================");
Console.WriteLine();

app.Run();
return 0;

public partial class Program { }
