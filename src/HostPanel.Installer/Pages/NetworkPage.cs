using HostPanel.Installer.Services;
using static HostPanel.Installer.Pages.FormHelpers;

namespace HostPanel.Installer.Pages;

/// <summary>Wizard page 4 — network (hostname, bind/public IP, TLS, hygiene).</summary>
public static class NetworkPage
{
    public static string Render(WizardState s)
    {
        var ifs = s.CachedPreflight?.Interfaces
            .Where(i => !i.IsLoopback && i.Ipv4.Count > 0)
            .SelectMany(i => i.Ipv4.Select(ip => (ip, label: $"{ip} ({i.Name})")))
            .ToList()
            ?? [];
        var bindOpts = ifs.Select(x => (x.ip, x.label)).ToArray();
        var ramGb = s.CachedPreflight?.Memory.TotalKb / 1024 / 1024 ?? 0;
        var swapMb = s.CachedPreflight?.Memory.SwapTotalKb / 1024 ?? 0;
        var swapPrompt = ramGb < 8 && swapMb == 0;

        return HtmlLayout.RenderPage(
            title: "Network",
            currentStep: 4,
            totalSteps: 9,
            bodyContent: $"""
<h1>Network</h1>
<p class='sub'>Hostname, which IP the panel binds to, and the TLS strategy.
Every system-mutating option is opt-in.</p>

{Form("/wizard/network",
    $"""
<div class='card'>
  {Text("hostname", "Panel hostname (FQDN)", s.Hostname, "panel.example.com", required: true)}
  {(bindOpts.Length == 0
    ? Text("bindIp", "Bind IP (no interfaces detected — type manually)", s.BindIp, "192.168.10.235")
    : Select("bindIp", "Bind IP (which local interface to listen on)", s.BindIp ?? bindOpts[0].ip, bindOpts))}
  {Text("publicIp", "Public-facing IP (used for DNS A record + LE challenges)", s.PublicIp, "auto-detect at install if blank")}
</div>

<div class='card'>
  <p style="margin-bottom:.75rem; font-weight:500">Cloudflare DNS automation</p>
  {Check("autoCreateCfDns", "Auto-create a Cloudflare A record pointing the hostname at the public IP", s.AutoCreateCfDns,
    "Solves the LE-needs-DNS chicken-and-egg. Requires a CF API token with Zone:Edit + DNS:Edit permissions.")}
  {Text("cfApiToken", "Cloudflare API token (only if auto-DNS is ticked)", s.CfApiToken, "starts with v1.0-… or a raw token", inputType: "password")}
  {Check("preValidateDns", "Pre-validate DNS resolves to this box before requesting LE cert", s.PreValidateDns,
    "Recommended. Cheap defense against requesting LE cert with the wrong DNS pointing.")}
</div>

<div class='card'>
  <p style="margin-bottom:.75rem; font-weight:500">TLS certificate</p>
  {Radio("tls", "How should the panel get its cert?", s.Tls.ToString(),
    ("LetsEncryptWithFallback", "Let's Encrypt, fall back to self-signed", "If LE fails (DNS not propagated yet, port 80 busy), install with self-signed and retry LE on next panel start. Most forgiving."),
    ("LetsEncrypt", "Let's Encrypt only", "Hard requirement. Install fails if LE fails."),
    ("SelfSigned", "Self-signed only", "Operator runs certbot manually after. Browsers warn until then."))}
</div>

<div class='card'>
  <p style="margin-bottom:.75rem; font-weight:500">Box hygiene</p>
  {Check("setSystemHostname", "Set this box's system hostname to match the panel FQDN", s.SetSystemHostname,
    "Run `hostnamectl set-hostname …`. Recommended — TLS + mail want a consistent FQDN.")}
  {Select("timezone", "Set system timezone (leave blank to keep current)",
    s.Timezone,
    ("", "(keep current)"),
    ("UTC", "UTC"),
    ("America/Los_Angeles", "America/Los_Angeles (PT)"),
    ("America/Denver", "America/Denver (MT)"),
    ("America/Chicago", "America/Chicago (CT)"),
    ("America/New_York", "America/New_York (ET)"),
    ("Europe/London", "Europe/London"),
    ("Europe/Berlin", "Europe/Berlin"),
    ("Asia/Tokyo", "Asia/Tokyo"))}
  {Check("installChrony", "Install + enable chrony for clock sync", s.InstallChrony,
    "Recommended. mssql replication + signed PayPal webhooks break on bad clocks.")}
  {(swapPrompt
    ? Check("createSwapfile", $"Create 4 GB swapfile (RAM detected: {ramGb} GB; no swap configured)", s.CreateSwapfile,
        "Recommended for boxes under 8 GB RAM. MSSQL + dotnet under load benefit.")
    : Check("createSwapfile", "Create 4 GB swapfile", s.CreateSwapfile,
        $"RAM detected: {ramGb} GB; existing swap: {swapMb} MB. Not strictly needed."))}
</div>
""", backHref: GetBackHref(s))}
""");
    }

    private static string GetBackHref(WizardState s) =>
        s.Role == InstallRole.Primary ? "/wizard/database" : "/preflight";

    public static void ApplyForm(WizardState s, IFormCollection f)
    {
        s.Hostname = Str(f, "hostname");
        s.BindIp = Str(f, "bindIp");
        s.PublicIp = Str(f, "publicIp");
        s.AutoCreateCfDns = Bool(f, "autoCreateCfDns");
        s.CfApiToken = Str(f, "cfApiToken");
        s.PreValidateDns = Bool(f, "preValidateDns");
        s.Tls = Str(f, "tls") switch
        {
            "SelfSigned" => TlsMode.SelfSigned,
            "LetsEncrypt" => TlsMode.LetsEncrypt,
            _ => TlsMode.LetsEncryptWithFallback
        };
        s.SetSystemHostname = Bool(f, "setSystemHostname");
        s.Timezone = Str(f, "timezone");
        s.InstallChrony = Bool(f, "installChrony");
        s.CreateSwapfile = Bool(f, "createSwapfile");
        s.LastUpdatedAt = DateTime.UtcNow;
    }
}
