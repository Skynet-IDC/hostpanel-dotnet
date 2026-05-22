using HostPanel.Installer.Services;
using static HostPanel.Installer.Pages.FormHelpers;

namespace HostPanel.Installer.Pages;

/// <summary>Wizard page 7-alt — link this standby to an existing primary.</summary>
public static class StandbyLinkPage
{
    public static string Render(WizardState s) =>
        HtmlLayout.RenderPage(
            title: "Standby link",
            currentStep: 7,
            totalSteps: 9,
            bodyContent: $"""
<h1>Link to primary</h1>
<p class='sub'>This standby will register with the primary via the F3 pairing
flow. You'll need a short-lived token from the primary's panel UI.</p>

<div class='card' style='background: rgba(37, 99, 235, 0.05); border-color: var(--accent)'>
  <p style='margin:0 0 .5rem; font-weight:500'>Before you continue, on the primary:</p>
  <ol style='margin:0; padding-left:1.5rem'>
    <li>Open <code>Settings → HA → Generate pairing token</code></li>
    <li>Copy the displayed token (15-min TTL, single-use)</li>
    <li>Paste it below along with the primary's URL</li>
  </ol>
</div>

{Form("/wizard/standby",
    $"""
<div class='card'>
  {Text("primaryUrl", "Primary URL", s.PrimaryUrl, "https://panel.example.com:8443", required: true)}
  {Text("pairingToken", "Pairing token", s.PairingToken,
    "64-char base64url from primary's Generate pairing token", required: true)}
  {Text("thisBoxReachableAddress", "How the primary should reach back to this box", s.ThisBoxReachableAddress,
    "LAN IP, hostname, or Tailscale name", required: true)}
  {Check("preferTailscaleForCallback", "Prefer the Tailscale magic-DNS name over LAN IP", s.PreferTailscaleForCallback,
    "If both primary and this box are on the same tailnet, more robust than LAN IP across NAT.")}
</div>
""", backHref: "/wizard/integrations")}
""");

    public static void ApplyForm(WizardState s, IFormCollection f)
    {
        s.PrimaryUrl = Str(f, "primaryUrl");
        s.PairingToken = Str(f, "pairingToken");
        s.ThisBoxReachableAddress = Str(f, "thisBoxReachableAddress");
        s.PreferTailscaleForCallback = Bool(f, "preferTailscaleForCallback");
        s.LastUpdatedAt = DateTime.UtcNow;
    }
}
