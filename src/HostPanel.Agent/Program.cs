using HostPanel.Agent;
using HostPanel.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

// Register services
builder.Services.AddSingleton<TokenAuthService>();

// Method handlers - System
builder.Services.AddSingleton<IAgentMethodHandler, SystemStatsHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, SystemGetBandwidthHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, SystemCreateUserHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, SystemDeleteUserHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, SystemSetQuotaHandler>();

// Nginx
builder.Services.AddSingleton<IAgentMethodHandler, NginxCreateVhostHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, NginxDeleteVhostHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, NginxReloadHandler>();

// PHP-FPM
builder.Services.AddSingleton<IAgentMethodHandler, PhpCreatePoolHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, PhpDeletePoolHandler>();

// SSL
builder.Services.AddSingleton<IAgentMethodHandler, SslIssueCertHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, SslRenewCertHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, SslRevokeCertHandler>();

// Backup
builder.Services.AddSingleton<IAgentMethodHandler, BackupCreateHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, BackupRestoreHandler>();

// Cron
builder.Services.AddSingleton<IAgentMethodHandler, CrontabHandler>();

// Firewall
builder.Services.AddSingleton<IAgentMethodHandler, FirewallAddRuleHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, FirewallDeleteRuleHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, FirewallEnableHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, FirewallDisableHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, FirewallStatusHandler>();

// Server updates
builder.Services.AddSingleton<IAgentMethodHandler, SystemCheckUpdatesHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, SystemListUpdatesHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, SystemInstallUpdatesHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, SystemUpdateHistoryHandler>();

// Log reader
builder.Services.AddSingleton<IAgentMethodHandler, LogReaderHandler>();

// WordPress
builder.Services.AddSingleton<IAgentMethodHandler, WordPressInstallHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, WordPressCloneHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, WordPressPushStagingHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, WordPressUpdateHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, WordPressSecurityScanHandler>();

// Docker
builder.Services.AddSingleton<IAgentMethodHandler, DockerCreateHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, DockerStartHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, DockerStopHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, DockerRestartHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, DockerRemoveHandler>();

// Git, FTP, Security
builder.Services.AddSingleton<IAgentMethodHandler, GitDeployHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, FtpCreateAccountHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, FtpDeleteAccountHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, ClamScanHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, ClamScanAllHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, Fail2BanStatusHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, Fail2BanUnbanHandler>();

// Analytics, Apps, Domain settings
builder.Services.AddSingleton<IAgentMethodHandler, AnalyticsVisitorsHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, AnalyticsBandwidthHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, AnalyticsServerResourcesHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, AppInstallHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, DomainHotlinkProtectionHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, DomainIpDenyHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, DomainPasswordProtectHandler>();

// App configuration (Next.js, Node, Passenger, PM2, etc.)
builder.Services.AddSingleton<IAgentMethodHandler, AppConfigureHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, AppBuildHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, AppRestartHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, AppWriteEnvHandler>();

// HA, Panel updates
builder.Services.AddSingleton<IAgentMethodHandler, HaConfigureStandbyHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, HaPromoteToPrimaryHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, HaSyncFromHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, HaSyncDatabasesHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, HaSyncEmailHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, HaStopSyncHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, HaDemoteToStandbyHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, HaAddFloatingIpHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, HaRemoveFloatingIpHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, HaSendGratuitousArpHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, HaReplicationLagHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, PanelFullUpdateHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, PanelFullRollbackHandler>();

// Mail provisioning and SSL
builder.Services.AddSingleton<IAgentMethodHandler, MailProvisionHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, MailConfigureSslHandler>();

// Migration (Plesk → HostPanel)
builder.Services.AddSingleton<IAgentMethodHandler, MigrationRsyncFromPleskHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, MigrationMysqldumpRemoteHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, MigrationImapsyncHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, MigrationDetectWordPressHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, MigrationVerifyCutoverHandler>();
builder.Services.AddSingleton<IAgentMethodHandler, MigrationPatchWpConfigHandler>();

builder.Services.AddSingleton<MethodRegistry>();
builder.Services.AddSingleton<AgentSessionProcessor>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<TcpListenerWorker>();

var host = builder.Build();
host.Run();
