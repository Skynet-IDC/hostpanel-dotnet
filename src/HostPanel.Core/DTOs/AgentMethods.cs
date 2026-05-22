namespace HostPanel.Core.DTOs;

public static class AgentMethods
{
    // System
    public const string SystemCreateUser = "system.createUser";
    public const string SystemDeleteUser = "system.deleteUser";
    public const string SystemSetQuota = "system.setQuota";
    public const string SystemGetStats = "system.getStats";
    public const string SystemGetBandwidth = "system.getBandwidth";

    // Nginx
    public const string NginxCreateVhost = "nginx.createVhost";
    public const string NginxDeleteVhost = "nginx.deleteVhost";
    public const string NginxReload = "nginx.reload";

    // PHP
    public const string PhpCreatePool = "php.createPool";
    public const string PhpDeletePool = "php.deletePool";
    public const string PhpUpdatePool = "php.updatePool";
    public const string PhpListVersions = "php.listVersions";
    public const string PhpListExtensions = "php.listExtensions";

    // DNS
    public const string DnsCreateZone = "dns.createZone";
    public const string DnsDeleteZone = "dns.deleteZone";
    public const string DnsUpdateRecord = "dns.updateRecord";
    public const string DnsDeleteRecord = "dns.deleteRecord";

    // Mail
    public const string MailCreateMailbox = "mail.createMailbox";
    public const string MailDeleteMailbox = "mail.deleteMailbox";
    public const string MailCreateAlias = "mail.createAlias";
    public const string MailDeleteAlias = "mail.deleteAlias";
    public const string MailProvision = "mail.provision";
    public const string MailConfigureSsl = "mail.configureSsl";

    // SSL
    public const string SslIssueCertificate = "ssl.issueCertificate";
    public const string SslRenewCertificate = "ssl.renewCertificate";
    public const string SslRevokeCertificate = "ssl.revokeCertificate";

    // Backup
    public const string BackupCreate = "backup.create";
    public const string BackupRestore = "backup.restore";

    // Server Updates
    public const string SystemCheckUpdates = "system.checkUpdates";
    public const string SystemListUpdates = "system.listUpdates";
    public const string SystemInstallUpdates = "system.installUpdates";
    public const string SystemUpdateHistory = "system.updateHistory";

    // Cron
    public const string SystemSetCrontab = "system.setCrontab";

    // Firewall
    public const string FirewallAddRule = "firewall.addRule";
    public const string FirewallDeleteRule = "firewall.deleteRule";
    public const string FirewallEnable = "firewall.enable";
    public const string FirewallDisable = "firewall.disable";
    public const string FirewallStatus = "firewall.status";

    // Logs
    public const string SystemReadLog = "system.readLog";

    // WordPress
    public const string WordPressInstall = "wordpress.install";
    public const string WordPressClone = "wordpress.clone";
    public const string WordPressPushStaging = "wordpress.pushStaging";
    public const string WordPressUpdate = "wordpress.update";
    public const string WordPressSecurityScan = "wordpress.securityScan";

    // Docker
    public const string DockerCreate = "docker.create";
    public const string DockerStart = "docker.start";
    public const string DockerStop = "docker.stop";
    public const string DockerRestart = "docker.restart";
    public const string DockerRemove = "docker.remove";

    // Git
    public const string GitDeploy = "git.deploy";

    // FTP
    public const string FtpCreateAccount = "ftp.createAccount";
    public const string FtpDeleteAccount = "ftp.deleteAccount";

    // Security
    public const string SecurityClamScan = "security.clamScan";
    public const string SecurityClamScanAll = "security.clamScanAll";
    public const string SecurityFail2BanStatus = "security.fail2banStatus";
    public const string SecurityFail2BanUnban = "security.fail2banUnban";
    public const string SecurityQuarantineFile = "security.quarantineFile";

    // Analytics
    public const string AnalyticsVisitors = "analytics.visitors";
    public const string AnalyticsBandwidth = "analytics.bandwidth";
    public const string AnalyticsServerResources = "analytics.serverResources";

    // Apps
    public const string AppsInstall = "apps.install";

    // Domain settings
    public const string DomainSetHotlinkProtection = "domain.setHotlinkProtection";
    public const string DomainSetIpDeny = "domain.setIpDeny";
    public const string DomainPasswordProtect = "domain.passwordProtect";

    // HA
    public const string HaConfigureStandby = "ha.configureStandby";
    public const string HaPromoteToPrimary = "ha.promoteToPrimary";
    public const string HaSyncFrom = "ha.syncFrom";
    public const string HaSyncDatabases = "ha.syncDatabases";
    public const string HaSyncEmail = "ha.syncEmail";
    public const string HaStopSync = "ha.stopSync";
    public const string HaDemoteToStandby = "ha.demoteToStandby";
    public const string HaAddFloatingIp = "ha.addFloatingIp";
    public const string HaRemoveFloatingIp = "ha.removeFloatingIp";
    public const string HaSendGratuitousArp = "ha.sendGratuitousArp";
    public const string HaReplicationLag = "ha.replicationLag";

    // Panel updates
    public const string PanelUpdate = "panel.update";
    public const string PanelRollback = "panel.rollback";

    // App configuration
    public const string AppConfigure = "app.configure";
    public const string AppBuild = "app.build";
    public const string AppRestart = "app.restart";
    public const string AppWriteEnv = "app.writeEnv";

    // Migration (Plesk → HostPanel)
    public const string MigrationRsyncFromPlesk = "migration.rsyncFromPlesk";
    public const string MigrationMysqldumpRemote = "migration.mysqldumpRemote";
    public const string MigrationImapsync = "migration.imapsync";
    public const string MigrationDetectWordPress = "migration.detectWordPress";
    public const string MigrationVerifyCutover = "migration.verifyCutover";
    public const string MigrationPatchWpConfig = "migration.patchWpConfig";

    // NOTE: DNS and Mail methods are handled at the API level via provider APIs
    // (PowerDNS REST API, Cloudflare API, Postfix/Dovecot config files),
    // not through the Agent RPC. Constants kept for documentation only.

    public static readonly HashSet<string> AllMethods =
    [
        SystemCreateUser, SystemDeleteUser, SystemSetQuota, SystemGetStats, SystemGetBandwidth,
        NginxCreateVhost, NginxDeleteVhost, NginxReload,
        PhpCreatePool, PhpDeletePool, PhpUpdatePool, PhpListVersions, PhpListExtensions,
        SslIssueCertificate, SslRenewCertificate, SslRevokeCertificate,
        BackupCreate, BackupRestore,
        SystemCheckUpdates, SystemListUpdates, SystemInstallUpdates, SystemUpdateHistory,
        SystemSetCrontab,
        FirewallAddRule, FirewallDeleteRule, FirewallEnable, FirewallDisable, FirewallStatus,
        SystemReadLog,
        WordPressInstall, WordPressClone, WordPressPushStaging, WordPressUpdate, WordPressSecurityScan,
        DockerCreate, DockerStart, DockerStop, DockerRestart, DockerRemove,
        GitDeploy,
        FtpCreateAccount, FtpDeleteAccount,
        SecurityClamScan, SecurityClamScanAll, SecurityFail2BanStatus, SecurityFail2BanUnban, SecurityQuarantineFile,
        AnalyticsVisitors, AnalyticsBandwidth, AnalyticsServerResources,
        AppsInstall,
        DomainSetHotlinkProtection, DomainSetIpDeny, DomainPasswordProtect,
        HaConfigureStandby, HaPromoteToPrimary, HaSyncFrom, HaSyncDatabases, HaSyncEmail, HaStopSync, HaDemoteToStandby, HaAddFloatingIp, HaRemoveFloatingIp, HaSendGratuitousArp, HaReplicationLag,
        PanelUpdate, PanelRollback,
        AppConfigure, AppBuild, AppRestart, AppWriteEnv,
        MailProvision,
        MailConfigureSsl,
        MigrationRsyncFromPlesk, MigrationMysqldumpRemote, MigrationImapsync,
        MigrationDetectWordPress, MigrationVerifyCutover, MigrationPatchWpConfig
    ];
}
