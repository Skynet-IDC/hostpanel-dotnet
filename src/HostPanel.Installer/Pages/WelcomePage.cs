namespace HostPanel.Installer.Pages;

/// <summary>
/// Stage 1 / wizard page 1 — Welcome.
/// Currently a placeholder that confirms the binary booted. Stage 3 turns
/// this into the actual first form page (primary/standby radio + Sentry
/// telemetry opt-in).
/// </summary>
public static class WelcomePage
{
    public static string Render() =>
        HtmlLayout.RenderPage(
            title: "Welcome",
            currentStep: 1,
            totalSteps: 9,
            bodyContent: """
<h1>HostPanel installer</h1>
<p class='sub'>Binary booted. Run the preflight to see what's on this box, then
the wizard will collect your install choices.</p>

<div class='card'>
  <dl class='meta'>
    <dt>Stage:</dt>      <dd id='stage'>…</dd>
    <dt>Version:</dt>    <dd id='version'>…</dd>
    <dt>Listening:</dt>  <dd id='boundAt'>…</dd>
  </dl>
</div>

<div class='actions'>
  <button class='btn primary' onclick='location.href="/preflight"'>Run preflight →</button>
</div>
""",
            extraScript: """
fetch('/healthz').then(r => r.json()).then(j => {
  document.getElementById('stage').textContent = j.stage;
  document.getElementById('version').textContent = j.version;
  document.getElementById('boundAt').textContent = j.boundAt;
}).catch(e => {
  document.querySelector('.sub').textContent = 'Healthz failed: ' + e.message;
});
""");
}
