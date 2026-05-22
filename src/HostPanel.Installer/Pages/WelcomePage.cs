using HostPanel.Installer.Services;
using static HostPanel.Installer.Pages.FormHelpers;

namespace HostPanel.Installer.Pages;

/// <summary>Wizard page 1 — pick role (primary vs standby) + Sentry opt-in.</summary>
public static class WelcomePage
{
    public static string Render(WizardState s) =>
        HtmlLayout.RenderPage(
            title: "Welcome",
            currentStep: 1,
            totalSteps: 9,
            bodyContent: $"""
<h1>HostPanel installer</h1>
<p class='sub'>Tell us what you're installing. Every other prompt in this wizard
defaults to <em>off</em> — nothing changes on this box unless you tick a box.</p>

{Form("/wizard/welcome",
    $"""
<div class='card'>
  {Radio("role", "What are you installing?", s.Role.ToString(),
    ("Primary", "Primary — a fresh HostPanel control plane", "Standalone or the first half of an HA pair."),
    ("Standby", "Standby — joining an existing primary", "The primary must already be running and you need a pairing token from its Settings → HA page."))}
</div>

<div class='card'>
  <p style="margin-bottom:.75rem; font-weight:500">Telemetry</p>
  {Check("sentryOptIn", "Send anonymous install-failure envelopes to Sentry", s.SentryOptIn,
    "Off by default. If enabled, the installer reports only crash/failure events (no credentials, no config). Helps us catch installer bugs on boxes we don't have access to.")}
</div>
""", continueText: "Continue →")}
""");

    public static void ApplyForm(WizardState s, IFormCollection f)
    {
        s.Role = Str(f, "role") == "Standby" ? InstallRole.Standby : InstallRole.Primary;
        s.SentryOptIn = Bool(f, "sentryOptIn");
        s.LastUpdatedAt = DateTime.UtcNow;
    }
}
