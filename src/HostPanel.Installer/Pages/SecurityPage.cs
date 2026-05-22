using HostPanel.Installer.Services;
using static HostPanel.Installer.Pages.FormHelpers;

namespace HostPanel.Installer.Pages;

/// <summary>Wizard page 5 — security & hygiene (UFW, fail2ban, SSH key import).</summary>
public static class SecurityPage
{
    public static string Render(WizardState s) =>
        HtmlLayout.RenderPage(
            title: "Security",
            currentStep: 5,
            totalSteps: 9,
            bodyContent: $"""
<h1>Security & hygiene</h1>
<p class='sub'>Default-off network hardening. Tick what you want — nothing
is mandatory.</p>

{Form("/wizard/security",
    $"""
<div class='card'>
  {Check("configureUfw", "Configure UFW with recommended rules", s.ConfigureUfw,
    "Allow 22 (SSH), 80 + 443 (web), 8443 (panel) anywhere; 50051 (agent) restricted to primary's IP on standby.")}
  {Check("installFail2ban", "Install fail2ban with HostPanel admin-login jail", s.InstallFail2ban,
    "Bans IPs after repeated failed /auth/login attempts. Off by default — turn on if your panel is public.")}
</div>

<div class='card'>
  <p style="margin-bottom:.5rem; font-weight:500">Admin SSH key import</p>
  <p class='check-detail' style='margin-bottom:.75rem'>
    Pastes one or more public keys into <code>/root/.ssh/authorized_keys</code> so you
    keep remote access after this installer terminal closes. Leave blank to skip.
  </p>
  {Area("adminSshPublicKey", "SSH public keys (one per line)", s.AdminSshPublicKey,
    "ssh-ed25519 AAAA… you@laptop", rows: 4)}
</div>
""", backHref: "/wizard/network")}
""");

    public static void ApplyForm(WizardState s, IFormCollection f)
    {
        s.ConfigureUfw = Bool(f, "configureUfw");
        s.InstallFail2ban = Bool(f, "installFail2ban");
        s.AdminSshPublicKey = Str(f, "adminSshPublicKey");
        s.LastUpdatedAt = DateTime.UtcNow;
    }
}
