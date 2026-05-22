// F3 web installer — Stage 2 (preflight & ports).
// Boots Kestrel on 0.0.0.0:8080, serves the wizard pages, and exposes
// /api/preflight for the read-only system-info JSON the preflight page consumes.

using HostPanel.Installer.Pages;
using HostPanel.Installer.Services;

var builder = WebApplication.CreateBuilder(args);

// Strip framework noise from Console output so the operator sees clean status
// lines (the binary runs in the foreground during install — no journal yet).
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddSingleton<PreflightService>();

// Bind explicitly so the user knows the port without inspecting config.
// 8080 matches the operator-facing port chosen in the F3 design doc.
const string bindUrl = "http://0.0.0.0:8080";
builder.WebHost.UseUrls(bindUrl);

var app = builder.Build();

// Wizard pages — each one is a static .Render() returning a complete HTML page.
app.MapGet("/", () => Results.Redirect("/welcome"));
app.MapGet("/welcome", () => Results.Content(WelcomePage.Render(), "text/html"));
app.MapGet("/preflight", () => Results.Content(PreflightPage.Render(), "text/html"));

// JSON APIs consumed by the pages' vanilla JS.
app.MapGet("/api/preflight", async (PreflightService svc, CancellationToken ct) =>
{
    var report = await svc.GatherAsync(ct);
    return Results.Json(report, new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    });
});

// Liveness check used by the curl-pipe-bash wrapper to confirm the binary booted
// before pointing the operator at the wizard URL.
app.MapGet("/healthz", () => Results.Ok(new
{
    ok = true,
    stage = "preflight",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev",
    boundAt = bindUrl
}));

Console.WriteLine();
Console.WriteLine("================================================================");
Console.WriteLine("  HostPanel Installer — Stage 2 (preflight & ports)");
Console.WriteLine("================================================================");
Console.WriteLine($"  Listening on {bindUrl}");
Console.WriteLine("  Open this URL in a browser to begin install.");
Console.WriteLine("  Press Ctrl-C to abort.");
Console.WriteLine("================================================================");
Console.WriteLine();

app.Run();

// Marker partial so Stage 3+ can hang test infrastructure off Program if needed.
public partial class Program { }
