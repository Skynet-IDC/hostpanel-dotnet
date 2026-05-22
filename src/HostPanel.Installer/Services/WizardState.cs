namespace HostPanel.Installer.Services;

public enum InstallRole { Primary, Standby }
public enum TlsMode { SelfSigned, LetsEncrypt, LetsEncryptWithFallback }
public enum UpdateChannel { Stable, Beta, Edge, Off }
public enum UpdateApplyMode { Auto, Manual, Off }
public enum BackupDestination { None, S3, B2, Sftp }

/// <summary>
/// In-memory store of every choice the operator makes across the wizard.
/// Singleton — there's one wizard run per installer process. Stage 4 swaps
/// in-memory for an .install-state.json persisted to disk (resume on crash).
/// </summary>
public class WizardState
{
    // === Page 1 — Welcome ===
    public InstallRole Role { get; set; } = InstallRole.Primary;
    public bool SentryOptIn { get; set; }

    // === Page 2 — Preflight (cached so later pages can read auto-calc'd defaults) ===
    public PreflightReport? CachedPreflight { get; set; }

    // === Page 3 — Database (primary only) ===
    public bool InstallMssql { get; set; } = true;
    public string? SaPassword { get; set; }
    public bool CapMssqlMemory { get; set; } = true;
    public int? MssqlMemoryMb { get; set; }
    public string? ExternalDbConnectionString { get; set; }

    // === Page 4 — Network ===
    public string? Hostname { get; set; }
    public string? BindIp { get; set; }
    public string? PublicIp { get; set; }
    public bool AutoCreateCfDns { get; set; }
    public string? CfApiToken { get; set; }
    public bool PreValidateDns { get; set; } = true;
    public TlsMode Tls { get; set; } = TlsMode.LetsEncryptWithFallback;
    public bool SetSystemHostname { get; set; } = true;
    public string? Timezone { get; set; }
    public bool InstallChrony { get; set; } = true;
    public bool CreateSwapfile { get; set; }

    // === Page 5 — Security & Hygiene ===
    public bool ConfigureUfw { get; set; } = true;
    public bool InstallFail2ban { get; set; }
    public string? AdminSshPublicKey { get; set; }

    // === Page 6 — Integrations ===
    public bool JoinTailscale { get; set; }
    public string? TailscaleAuthKey { get; set; }
    public bool ConfigureSmtp { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    public bool ConfigureBackups { get; set; }
    public BackupDestination BackupDest { get; set; } = BackupDestination.None;
    public string? BackupBucketOrPath { get; set; }
    public string? BackupAccessKey { get; set; }
    public string? BackupSecretKey { get; set; }
    public bool ForwardToLoki { get; set; }
    public string? LokiEndpoint { get; set; }
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;
    public UpdateApplyMode UpdateApplyMode { get; set; } = UpdateApplyMode.Manual;
    public bool PostToN8n { get; set; }
    public string? N8nWebhookUrl { get; set; }

    // === Page 7 — Admin & Branding (primary only) ===
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string? RecoveryContactEmail { get; set; }
    public bool UploadBrandLogo { get; set; }
    public string? BrandLogoUrl { get; set; }
    public string? BrandColor { get; set; }
    public bool CreateDemoCustomer { get; set; }
    public bool PrefetchAppCatalog { get; set; }

    // === Page 7-alt — Standby link (standby only) ===
    public string? PrimaryUrl { get; set; }
    public string? PairingToken { get; set; }
    public string? ThisBoxReachableAddress { get; set; }
    public bool PreferTailscaleForCallback { get; set; }

    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Returns the next page in the wizard flow given the current page.
    /// Primary path: welcome → preflight → database → network → security → integrations → admin → confirm
    /// Standby path: welcome → preflight → network → security → integrations → standby → confirm
    /// </summary>
    public string NextAfter(string currentPage) => Role switch
    {
        InstallRole.Primary => currentPage switch
        {
            "welcome" => "/preflight",
            "preflight" => "/wizard/database",
            "database" => "/wizard/network",
            "network" => "/wizard/security",
            "security" => "/wizard/integrations",
            "integrations" => "/wizard/admin",
            "admin" => "/wizard/confirm",
            _ => "/welcome"
        },
        InstallRole.Standby => currentPage switch
        {
            "welcome" => "/preflight",
            "preflight" => "/wizard/network",
            "network" => "/wizard/security",
            "security" => "/wizard/integrations",
            "integrations" => "/wizard/standby",
            "standby" => "/wizard/confirm",
            _ => "/welcome"
        },
        _ => "/welcome"
    };
}
