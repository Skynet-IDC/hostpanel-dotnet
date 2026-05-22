using HostPanel.Installer.Services;
using static HostPanel.Installer.Pages.FormHelpers;

namespace HostPanel.Installer.Pages;

/// <summary>Wizard page 6 — integrations (Tailscale, SMTP, backups, Loki, update channel, n8n).</summary>
public static class IntegrationsPage
{
    public static string Render(WizardState s) =>
        HtmlLayout.RenderPage(
            title: "Integrations",
            currentStep: 6,
            totalSteps: 9,
            bodyContent: $"""
<h1>Integrations</h1>
<p class='sub'>External services. Each line is independently skippable —
leave a box unticked to skip it entirely.</p>

{Form("/wizard/integrations",
    $"""
<div class='card'>
  {Check("joinTailscale", "Join Tailscale tailnet during install", s.JoinTailscale,
    "Useful for HA so primary↔standby happens over the tailnet, not public IPs.")}
  {Text("tailscaleAuthKey", "Tailscale auth key (only if joining)", s.TailscaleAuthKey,
    "tskey-auth-…", inputType: "password")}
</div>

<div class='card'>
  {Check("configureSmtp", "Configure outbound SMTP relay (otherwise uses bundled Postfix)", s.ConfigureSmtp,
    "External relay (SES, Mailgun, etc) is more reliable for password-reset emails.")}
  {Text("smtpHost", "SMTP host", s.SmtpHost, "smtp.example.com")}
  {Text("smtpPort", "SMTP port", s.SmtpPort.ToString(), "587", inputType: "number")}
  {Text("smtpUser", "SMTP username", s.SmtpUser, "apikey or user@domain")}
  {Text("smtpPassword", "SMTP password", s.SmtpPassword, "", inputType: "password")}
</div>

<div class='card'>
  {Check("configureBackups", "Configure off-box backups (S3/B2/SFTP)", s.ConfigureBackups,
    "Periodic full backups of /opt/hostpanel + MSSQL dumps to remote storage.")}
  {Radio("backupDest", "Destination", s.BackupDest.ToString(),
    ("None", "None / decide later", null),
    ("S3", "AWS S3 (or S3-compatible like MinIO/Wasabi)", null),
    ("B2", "Backblaze B2", null),
    ("Sftp", "SFTP server", null))}
  {Text("backupBucketOrPath", "Bucket / remote path", s.BackupBucketOrPath, "my-hostpanel-backups")}
  {Text("backupAccessKey", "Access key / username", s.BackupAccessKey, "")}
  {Text("backupSecretKey", "Secret key / password", s.BackupSecretKey, "", inputType: "password")}
</div>

<div class='card'>
  {Check("forwardToLoki", "Forward systemd journal to Loki", s.ForwardToLoki,
    "Wires up promtail to ship logs to your existing Loki endpoint.")}
  {Text("lokiEndpoint", "Loki push endpoint", s.LokiEndpoint, "http://loki.internal:3100/loki/api/v1/push")}
</div>

<div class='card'>
  {Radio("updateChannel", "Self-update channel", s.UpdateChannel.ToString(),
    ("Stable", "Stable — tagged releases only", null),
    ("Beta", "Beta — pre-release tags", null),
    ("Edge", "Edge — every commit on main", null),
    ("Off", "Off — never check for updates", null))}
  {Radio("updateApplyMode", "When new updates exist…", s.UpdateApplyMode.ToString(),
    ("Manual", "Manual — show banner in UI, operator clicks Apply", null),
    ("Auto", "Auto — apply immediately when detected", "Risky: an upstream bug can take down the panel without warning."),
    ("Off", "Off — never auto-apply (panel restart required for new binary)", null))}
</div>

<div class='card'>
  {Check("postToN8n", "Post panel events to an n8n webhook", s.PostToN8n,
    "Each subscription created/changed/billed → POST to your n8n flow.")}
  {Text("n8nWebhookUrl", "n8n webhook URL", s.N8nWebhookUrl, "https://n8n.example.com/webhook/hostpanel")}
</div>
""", backHref: "/wizard/security")}
""");

    public static void ApplyForm(WizardState s, IFormCollection f)
    {
        s.JoinTailscale = Bool(f, "joinTailscale");
        s.TailscaleAuthKey = Str(f, "tailscaleAuthKey");
        s.ConfigureSmtp = Bool(f, "configureSmtp");
        s.SmtpHost = Str(f, "smtpHost");
        s.SmtpPort = Int(f, "smtpPort") ?? 587;
        s.SmtpUser = Str(f, "smtpUser");
        s.SmtpPassword = Str(f, "smtpPassword");
        s.ConfigureBackups = Bool(f, "configureBackups");
        s.BackupDest = Str(f, "backupDest") switch
        {
            "S3" => BackupDestination.S3,
            "B2" => BackupDestination.B2,
            "Sftp" => BackupDestination.Sftp,
            _ => BackupDestination.None
        };
        s.BackupBucketOrPath = Str(f, "backupBucketOrPath");
        s.BackupAccessKey = Str(f, "backupAccessKey");
        s.BackupSecretKey = Str(f, "backupSecretKey");
        s.ForwardToLoki = Bool(f, "forwardToLoki");
        s.LokiEndpoint = Str(f, "lokiEndpoint");
        s.UpdateChannel = Str(f, "updateChannel") switch
        {
            "Beta" => UpdateChannel.Beta,
            "Edge" => UpdateChannel.Edge,
            "Off" => UpdateChannel.Off,
            _ => UpdateChannel.Stable
        };
        s.UpdateApplyMode = Str(f, "updateApplyMode") switch
        {
            "Auto" => UpdateApplyMode.Auto,
            "Off" => UpdateApplyMode.Off,
            _ => UpdateApplyMode.Manual
        };
        s.PostToN8n = Bool(f, "postToN8n");
        s.N8nWebhookUrl = Str(f, "n8nWebhookUrl");
        s.LastUpdatedAt = DateTime.UtcNow;
    }
}
