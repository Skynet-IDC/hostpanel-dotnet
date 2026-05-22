namespace HostPanel.Installer.Pages;

/// <summary>
/// Shared HTML chrome for every wizard page. One CSS block, one head, one body
/// wrapper — so each page file just contributes its own &lt;main&gt; content +
/// optional inline script. Per the F3 design contract: HTML+vanilla-JS, no npm
/// toolchain, no React.
/// </summary>
public static class HtmlLayout
{
    public static string RenderPage(string title, string bodyContent, string? extraScript = null, int currentStep = 0, int totalSteps = 0)
    {
        var stepIndicator = totalSteps > 0
            ? $"<div class='steps'>Step {currentStep} of {totalSteps}</div>"
            : "";

        var script = extraScript is null ? "" : $"<script>{extraScript}</script>";

        return $"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>{title} — HostPanel installer</title>
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <style>{SharedCss}</style>
</head>
<body>
  <div class="shell">
    <header>
      <div class="brand">HostPanel <span class="tag">installer</span></div>
      {stepIndicator}
    </header>
    <main>
      {bodyContent}
    </main>
    <footer>
      F3 web installer · single binary · runs as root · no external dependencies
    </footer>
  </div>
  {script}
</body>
</html>
""";
    }

    private const string SharedCss = """
:root {
  color-scheme: light dark;
  --fg: #1a1a1a;
  --bg: #fafafa;
  --accent: #2563eb;
  --accent-hover: #1d4ed8;
  --muted: #6b7280;
  --card: #ffffff;
  --border: #e5e7eb;
  --pass: #16a34a;
  --warn: #d97706;
  --block: #dc2626;
  --code-bg: #f3f4f6;
}
@media (prefers-color-scheme: dark) {
  :root {
    --fg: #f5f5f5; --bg: #0a0a0a;
    --card: #1a1a1a; --border: #2a2a2a;
    --muted: #9ca3af; --code-bg: #1f2937;
  }
}
* { box-sizing: border-box; }
body {
  font: 15px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif;
  color: var(--fg); background: var(--bg);
  margin: 0; padding: 2rem 1rem;
  min-height: 100vh;
}
.shell {
  max-width: 820px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  min-height: calc(100vh - 4rem);
}
header {
  display: flex; justify-content: space-between; align-items: center;
  padding: 1rem 0 1.5rem; margin-bottom: 1.5rem;
  border-bottom: 1px solid var(--border);
}
.brand { font-weight: 600; font-size: 1.1rem; }
.tag {
  display: inline-block; background: var(--accent); color: white;
  padding: 2px 8px; border-radius: 4px;
  font-size: 11px; font-weight: 500; vertical-align: middle;
  text-transform: uppercase; letter-spacing: 0.05em;
}
.steps { color: var(--muted); font-size: 13px; font-family: ui-monospace, monospace; }
main { flex: 1; }
footer {
  margin-top: 2rem; padding-top: 1.5rem;
  border-top: 1px solid var(--border);
  color: var(--muted); font-size: 12px; text-align: center;
}
h1 { margin: 0 0 .5rem; font-size: 1.5rem; }
h2 { margin: 1.5rem 0 .5rem; font-size: 1.1rem; font-weight: 600; }
p { margin: 0 0 1rem; }
.sub { color: var(--muted); margin-bottom: 1.5rem; }
.card {
  background: var(--card); border: 1px solid var(--border);
  border-radius: 10px; padding: 1.5rem; margin-bottom: 1rem;
}
.actions {
  display: flex; gap: .5rem; justify-content: flex-end;
  margin-top: 2rem; padding-top: 1.5rem;
  border-top: 1px solid var(--border);
}
button, .btn {
  font: inherit;
  padding: .55rem 1.25rem;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid var(--border);
  background: var(--card);
  color: var(--fg);
}
button.primary, .btn.primary {
  background: var(--accent); color: white; border-color: var(--accent);
}
button.primary:hover, .btn.primary:hover { background: var(--accent-hover); }
button:disabled, .btn:disabled { opacity: 0.5; cursor: not-allowed; }
.check-row {
  display: grid;
  grid-template-columns: 1.5rem 1fr auto;
  gap: .75rem; align-items: start;
  padding: .55rem 0;
  border-bottom: 1px solid var(--border);
}
.check-row:last-child { border-bottom: none; }
.check-label { font-weight: 500; }
.check-detail { color: var(--muted); font-size: 13px; margin-top: 2px; }
.check-suggestion {
  color: var(--muted); font-size: 13px; margin-top: 4px;
  padding-left: .5rem; border-left: 2px solid var(--border);
}
.status-pass { color: var(--pass); font-weight: 600; }
.status-warn { color: var(--warn); font-weight: 600; }
.status-block { color: var(--block); font-weight: 600; }
.status-badge {
  display: inline-block;
  min-width: 1.25rem; text-align: center;
}
.meta {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: .35rem 1rem;
  font-family: ui-monospace, "SF Mono", Consolas, monospace;
  font-size: 13px;
}
.meta dt { color: var(--muted); }
.meta dd { margin: 0; word-break: break-all; }
code, .code {
  font-family: ui-monospace, monospace; font-size: 13px;
  background: var(--code-bg); padding: 1px 5px; border-radius: 3px;
}
.banner {
  padding: .85rem 1rem; border-radius: 6px;
  margin-bottom: 1rem;
}
.banner-block { background: rgba(220, 38, 38, 0.1); border: 1px solid var(--block); color: var(--block); }
.banner-warn { background: rgba(217, 119, 6, 0.1); border: 1px solid var(--warn); color: var(--warn); }
.banner-pass { background: rgba(22, 163, 74, 0.1); border: 1px solid var(--pass); color: var(--pass); }
table { width: 100%; border-collapse: collapse; font-size: 13px; }
table th, table td {
  text-align: left; padding: .4rem .5rem;
  border-bottom: 1px solid var(--border);
}
table th { font-weight: 600; color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: 0.05em; }
.spinner {
  display: inline-block; width: 1em; height: 1em;
  border: 2px solid var(--muted); border-top-color: transparent;
  border-radius: 50%; animation: spin 0.8s linear infinite;
  vertical-align: middle;
}
@keyframes spin { to { transform: rotate(360deg); } }
""";
}
