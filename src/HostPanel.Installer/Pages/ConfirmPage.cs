using System.Text;
using HostPanel.Installer.Services;
using static HostPanel.Installer.Pages.FormHelpers;

namespace HostPanel.Installer.Pages;

/// <summary>
/// Wizard page 8 — summary of every choice + Save-as-yaml + Dry-run + Install.
/// Last chance to go back and tweak.
/// </summary>
public static class ConfirmPage
{
    public static string Render(WizardState s)
    {
        var summary = BuildSummary(s);

        return HtmlLayout.RenderPage(
            title: "Confirm",
            currentStep: 8,
            totalSteps: 9,
            bodyContent: $"""
<h1>Confirm install plan</h1>
<p class='sub'>Review every choice. Use Back to edit. Save your answers as
YAML to replay on a sibling box, or run a dry-run to preview steps without
mutating anything.</p>

<div class='card'>
  <h2 style='margin-top:0'>Summary</h2>
  {summary}
</div>

<form method='post' action='/wizard/confirm' style='margin-top:1.5rem'>
  {Check("saveYaml", "Save these answers to install.yaml for later (downloadable after install)", false,
    "Use --config install.yaml --headless on the next box to replay this exact install.")}

  <div class='actions'>
    <a class='btn' href='{(s.Role == InstallRole.Primary ? "/wizard/admin" : "/wizard/standby")}'>← Back</a>
    <a class='btn' href='/wizard/dryrun'>Dry-run preview</a>
    <button type='submit' name='action' value='install' class='btn primary' style='background:var(--block); border-color:var(--block)'>Install now ▶</button>
  </div>
</form>
""");
    }

    private static string BuildSummary(WizardState s)
    {
        var rows = new StringBuilder();

        rows.Append("<dl class='meta' style='margin:0'>");
        rows.Append(Row("Role", s.Role.ToString()));
        rows.Append(Row("Sentry telemetry", Yn(s.SentryOptIn)));

        if (s.Role == InstallRole.Primary)
        {
            rows.Append(Section("Database"));
            rows.Append(Row("Install MSSQL", Yn(s.InstallMssql)));
            rows.Append(Row("SA password", string.IsNullOrEmpty(s.SaPassword) ? "(not set)" : "(set)"));
            rows.Append(Row("Memory cap", s.CapMssqlMemory ? $"{s.MssqlMemoryMb?.ToString() ?? "auto"} MB" : "no cap"));
            if (!string.IsNullOrEmpty(s.ExternalDbConnectionString))
                rows.Append(Row("External DB", "(set — overrides local install)"));
        }

        rows.Append(Section("Network"));
        rows.Append(Row("Hostname", s.Hostname ?? "(unset)"));
        rows.Append(Row("Bind IP", s.BindIp ?? "(auto)"));
        rows.Append(Row("Public IP", s.PublicIp ?? "(auto-detect)"));
        rows.Append(Row("Auto Cloudflare DNS", Yn(s.AutoCreateCfDns)));
        rows.Append(Row("Pre-validate DNS", Yn(s.PreValidateDns)));
        rows.Append(Row("TLS", s.Tls.ToString()));
        rows.Append(Row("Set system hostname", Yn(s.SetSystemHostname)));
        rows.Append(Row("Timezone", string.IsNullOrEmpty(s.Timezone) ? "(keep current)" : s.Timezone));
        rows.Append(Row("Install chrony", Yn(s.InstallChrony)));
        rows.Append(Row("Create swapfile", Yn(s.CreateSwapfile)));

        rows.Append(Section("Security"));
        rows.Append(Row("Configure UFW", Yn(s.ConfigureUfw)));
        rows.Append(Row("Install fail2ban", Yn(s.InstallFail2ban)));
        rows.Append(Row("Import SSH key", string.IsNullOrEmpty(s.AdminSshPublicKey) ? "no" : "yes"));

        rows.Append(Section("Integrations"));
        rows.Append(Row("Tailscale", Yn(s.JoinTailscale)));
        rows.Append(Row("External SMTP", Yn(s.ConfigureSmtp)));
        rows.Append(Row("Off-box backups", Yn(s.ConfigureBackups) + (s.ConfigureBackups ? $" → {s.BackupDest}" : "")));
        rows.Append(Row("Loki forwarding", Yn(s.ForwardToLoki)));
        rows.Append(Row("Update channel", s.UpdateChannel.ToString()));
        rows.Append(Row("Apply updates", s.UpdateApplyMode.ToString()));
        rows.Append(Row("n8n webhook", Yn(s.PostToN8n)));

        if (s.Role == InstallRole.Primary)
        {
            rows.Append(Section("Admin"));
            rows.Append(Row("Admin email", s.AdminEmail ?? "(unset)"));
            rows.Append(Row("Admin password", string.IsNullOrEmpty(s.AdminPassword) ? "(not set)" : "(set)"));
            rows.Append(Row("Recovery email", s.RecoveryContactEmail ?? "(none)"));
            rows.Append(Row("Custom branding", Yn(s.UploadBrandLogo)));
            rows.Append(Row("Demo customer", Yn(s.CreateDemoCustomer)));
            rows.Append(Row("Pre-fetch app catalog", Yn(s.PrefetchAppCatalog)));
        }
        else
        {
            rows.Append(Section("Standby link"));
            rows.Append(Row("Primary URL", s.PrimaryUrl ?? "(unset)"));
            rows.Append(Row("Pairing token", string.IsNullOrEmpty(s.PairingToken) ? "(unset)" : "(set)"));
            rows.Append(Row("Reachable address", s.ThisBoxReachableAddress ?? "(unset)"));
            rows.Append(Row("Prefer Tailscale callback", Yn(s.PreferTailscaleForCallback)));
        }

        rows.Append("</dl>");
        return rows.ToString();
    }

    private static string Row(string k, string v) => $"<dt>{E(k)}</dt><dd>{E(v)}</dd>";
    private static string Section(string s) => $"<dt style='grid-column:1/-1; padding-top:.6rem; font-weight:600; color:var(--fg); border-top:1px dashed var(--border)'>{E(s)}</dt><dd style='display:none'></dd>";
    private static string Yn(bool b) => b ? "yes" : "no";
}
