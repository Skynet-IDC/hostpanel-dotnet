// F3 web installer — Stage 1 skeleton.
// Boots Kestrel on 0.0.0.0:8080 and serves a hardcoded Welcome page so the
// operator can confirm the binary deployed correctly on a fresh box before
// later stages bolt on preflight, wizard forms, the install orchestrator,
// and live SSE progress.

var builder = WebApplication.CreateBuilder(args);

// Strip framework noise from Console output so the operator sees clean status
// lines (the binary runs in the foreground during install — no journal yet).
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

// Bind explicitly so the user knows the port without inspecting config.
// 8080 matches the operator-facing port chosen in the F3 design doc.
const string bindUrl = "http://0.0.0.0:8080";
builder.WebHost.UseUrls(bindUrl);

var app = builder.Build();

app.MapGet("/", () => Results.Content(WelcomePage(), "text/html"));
app.MapGet("/welcome", () => Results.Content(WelcomePage(), "text/html"));

// Liveness check used by curl-pipe-bash wrapper to confirm the binary booted
// before pointing the operator at the wizard URL.
app.MapGet("/healthz", () => Results.Ok(new
{
    ok = true,
    stage = "skeleton",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev",
    boundAt = bindUrl
}));

Console.WriteLine();
Console.WriteLine("================================================================");
Console.WriteLine("  HostPanel Installer — Stage 1 (skeleton)");
Console.WriteLine("================================================================");
Console.WriteLine($"  Listening on {bindUrl}");
Console.WriteLine("  Open this URL in a browser to begin install.");
Console.WriteLine("  Press Ctrl-C to abort.");
Console.WriteLine("================================================================");
Console.WriteLine();

app.Run();

static string WelcomePage() => """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>HostPanel installer</title>
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <style>
    :root {
      color-scheme: light dark;
      --fg: #1a1a1a;
      --bg: #fafafa;
      --accent: #2563eb;
      --muted: #6b7280;
      --card: #ffffff;
      --border: #e5e7eb;
    }
    @media (prefers-color-scheme: dark) {
      :root { --fg: #f5f5f5; --bg: #0a0a0a; --card: #1a1a1a; --border: #2a2a2a; --muted: #9ca3af; }
    }
    * { box-sizing: border-box; }
    body {
      font: 15px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif;
      color: var(--fg); background: var(--bg);
      margin: 0; padding: 2rem;
      display: flex; align-items: center; justify-content: center;
      min-height: 100vh;
    }
    .card {
      background: var(--card);
      border: 1px solid var(--border);
      border-radius: 12px;
      padding: 2.5rem;
      max-width: 640px;
      width: 100%;
      box-shadow: 0 1px 3px rgba(0,0,0,0.05);
    }
    h1 { margin: 0 0 .5rem; font-size: 1.5rem; }
    .sub { color: var(--muted); margin-bottom: 2rem; }
    .meta {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: .5rem 1rem;
      margin: 1.5rem 0;
      font-family: ui-monospace, "SF Mono", Consolas, monospace;
      font-size: 13px;
    }
    .meta dt { color: var(--muted); }
    .meta dd { margin: 0; }
    .next {
      margin-top: 2rem;
      padding-top: 1.5rem;
      border-top: 1px solid var(--border);
      color: var(--muted);
      font-size: 14px;
    }
    .tag {
      display: inline-block;
      background: var(--accent); color: white;
      padding: 2px 8px; border-radius: 4px;
      font-size: 12px; font-weight: 500;
      vertical-align: middle;
    }
  </style>
</head>
<body>
  <main class="card">
    <h1>HostPanel installer <span class="tag">Stage 1</span></h1>
    <p class="sub">Binary booted. Wizard is not built yet — this is the skeleton.</p>

    <dl class="meta">
      <dt>Stage:</dt>      <dd id="stage">…</dd>
      <dt>Version:</dt>    <dd id="version">…</dd>
      <dt>Listening:</dt>  <dd id="boundAt">…</dd>
    </dl>

    <div class="next">
      Next stages add: preflight checks, the wizard form pages,
      the install orchestrator, and live SSE progress.
    </div>
  </main>

  <script>
    fetch('/healthz').then(r => r.json()).then(j => {
      document.getElementById('stage').textContent = j.stage;
      document.getElementById('version').textContent = j.version;
      document.getElementById('boundAt').textContent = j.boundAt;
    }).catch(e => {
      document.querySelector('.sub').textContent = 'Healthz failed: ' + e.message;
    });
  </script>
</body>
</html>
""";

// Suppress the "Type Program must be public" Razor pages convention — we don't use
// it here, and we want the type internal so the assembly is clean.
public partial class Program { }
