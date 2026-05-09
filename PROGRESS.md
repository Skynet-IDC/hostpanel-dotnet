# HostPanel - Development Progress Tracker

## Project: Plesk Replacement Platform
**Start Date**: 2026-05-07
**Stack**: ASP.NET Core 8+ / React 18+ / SQL Server / Linux

---

## Phase 1: Foundation (MVP)
- [x] 1.1 Project scaffolding - Solution structure, EF Core, base API
- [x] 1.2 Authentication & RBAC - Admin/Reseller/Customer roles, JWT, login UI
- [x] 1.3 System Agent daemon - Unix socket, token auth, method allowlist, JSON-RPC
- [x] 1.4 Desired-state engine - Idempotent reconciler, partial-failure retry
- [x] 1.5 Dashboard - Server stats (CPU, RAM, disk, bandwidth)
- [x] 1.6 Interface stubs - IBackupService, IBillingMeter, IPaymentProvider, IServerAgent

## Phase 2: Web Hosting Core
- [x] 2.1 Domain/Subscription management - CRUD, assign to customers, resource quotas
- [x] 2.2 Nginx vhost management - Auto-generate configs, reload, PHP version selection
- [x] 2.3 PHP-FPM pool management - Shared pools per plan tier, cgroup limits
- [x] 2.4 SSL/TLS automation - Let's Encrypt HTTP-01 + DNS-01, auto-renewal
- [x] 2.5 File Manager - Browse, upload, download, edit, permissions

## Phase 3: Services
- [x] 3.1 DNS management - IDnsProvider + PowerDNS + Cloudflare, zone/record CRUD
- [x] 3.2 Email hosting - Postfix/Dovecot, multi-port (25/587/465/2525), SMTP relay support
- [x] 3.3 Spam filtering - Rspamd config generation, DKIM signing
- [x] 3.4 Webmail - WebmailService (Roundcube + Snappymail config gen, Nginx proxy)
- [x] 3.5 Database management - MSSQL/MySQL/MariaDB/PostgreSQL/SQLite provisioners

## Phase 4: Multi-tenancy & Business
- [x] 4.1 Reseller system - CustomersController with RBAC, reseller customer management
- [x] 4.2 Service plans - Already built in Phase 2 (ServicePlansController)
- [x] 4.3 Customer self-service portal - API-driven, customer role sees own resources
- [x] 4.4 Automated billing - PayPal Invoicing + Stripe providers, BillingCycleService, webhooks
- [x] 4.5 Usage tracking - UsageTrackingService + background job (6hr cycle), overage calc
- [x] 4.6 Backup & restore - BackupService via Agent, BackupsController
- [x] 4.7 Plesk migration tool - PleskMigrationService (API + backup import), MigrationController

## Phase 5: Advanced
- [x] 5.1 .NET/Node.js/Python app hosting - AppHostingService, systemd unit gen, Nginx reverse proxy
- [x] 5.2 Cron job management - CronJobsController CRUD, agent crontab sync
- [x] 5.3 Firewall management - FirewallController (UFW rules add/delete/enable/disable)
- [x] 5.4 Log viewer - LogsController (access/error/mail logs, tail last N lines)
- [x] 5.5 Server updates - ServerUpdatesController + Agent handlers (check/list/install/history)
- [x] 5.6 Multi-server support - Server model, RemoteServerAgent, ServerAgentFactory, ServersController
- [x] 5.7 Installer script - hostpanel-install.sh (Ubuntu/Debian, all dependencies, firewall)
- [x] 5.8 HostPanel MCP server - 17 tools for Claude Code / Cursor integration
- [x] 5.9 Authentik SSO integration - OIDC login, auto-provision users, SsoController
- [x] 5.10 n8n automation - WebhookDispatcher, 23 event types, WebhooksController, n8n custom node + trigger

## Phase 6: Competitive Feature Parity
- [x] 6.1 WordPress Toolkit - One-click install, staging/clone, push to production, bulk updates, security scan, maintenance mode, WP-CLI
- [x] 6.2 Two-Factor Auth (TOTP) - Built-in Google Authenticator/Authy, setup/verify/disable, recovery codes
- [x] 6.3 FTP/SFTP Management - Account CRUD, per-directory chroot, quota management
- [x] 6.4 Git Deployment - GitHub/GitLab/Bitbucket webhook deploy, manual deploy, auto-deploy on push
- [x] 6.5 Fail2Ban Integration - Status, unban, jail management via Agent
- [x] 6.6 Docker Container Management - Create/start/stop/restart/delete, Nginx reverse proxy
- [x] 6.7 IP Address Management - IP pool, dedicated/shared assignment, blacklist with expiry
- [x] 6.8 ClamAV Virus Scanner - Per-domain scan, full server scan via Agent
- [x] 6.9 Analytics Dashboard - Visitor stats, bandwidth usage, server resource history
- [x] 6.10 One-Click App Installer - 14 apps (WordPress, Joomla, Drupal, PrestaShop, Nextcloud, Ghost, n8n, etc.)
- [x] 6.11 Email Catch-All - Per-domain catch-all forwarding
- [x] 6.12 Hotlink Protection - Nginx-level image hotlink blocking with allowed domains
- [x] 6.13 IP Deny Manager - Per-domain IP blocking
- [x] 6.14 Directory Password Protection - .htpasswd-style auth via UI
- [x] 6.15 Panel Self-Update - Pull from source, build, deploy, rollback
- [x] 6.16 High Availability - Standby server sync, manual/auto failover, health endpoint for external monitors

---

## Completed Items Log

| Date | Item | Notes |
|------|------|-------|
| 2026-05-07 | Project planning | Architecture, tech stack, phases defined |
| 2026-05-07 | 1.1 Project scaffolding | 12-project .NET solution, all references configured |
| 2026-05-07 | 1.2 Auth & RBAC | ASP.NET Identity + JWT, Admin/Reseller/Customer roles, seed admin |
| 2026-05-07 | 1.3 System Agent | Worker service with Unix socket, PSK auth, method registry |
| 2026-05-07 | 1.5 Dashboard | React + Vite + TailwindCSS, login page, dashboard with stats |
| 2026-05-07 | 1.6 Interface stubs | IServerAgent, IPaymentProvider, IBackupService, IBillingMeter |
| 2026-05-07 | 1.4 Desired-state engine | DesiredState model, ReconciliationService, exponential backoff, SubscriptionReconciler |
| 2026-05-07 | 2.1 Domain/Subscription mgmt | Full CRUD APIs for subscriptions, domains, service plans + React domains page |
| 2026-05-07 | 2.2-2.3 Nginx/PHP-FPM | Agent handlers for vhost create/delete/reload, system user create/delete/quota, ProcessRunner |
| 2026-05-07 | 2.4 SSL/TLS | SslService with Certes ACME, HTTP-01 challenge, auto-renewal background job, SslController |
| 2026-05-07 | 2.5 File Manager | FileManagerService with path traversal protection, FilesController (CRUD/upload/download), React file browser |
| 2026-05-07 | 3.1 DNS | IDnsProvider interface, PowerDnsProvider, CloudflareDnsProvider, DnsProviderFactory, DnsController |
| 2026-05-07 | 3.2-3.3 Email+Spam | MailService (Postfix+Dovecot), multi-port SMTP (25/587/465/2525), SMTP relay config, Rspamd, EmailController |
| 2026-05-07 | 3.5 Databases | IDbProvisioningService, 5 engines (MSSQL/MySQL/MariaDB/PostgreSQL/SQLite), DbProvisioningFactory, DatabasesController |
| 2026-05-07 | 4.1-4.3 Reseller/Customers | CustomersController with RBAC scoping, create/list/toggle-active/delete |
| 2026-05-07 | 4.4 Billing | PayPalInvoicingProvider + StripeProvider, BillingCycleService (daily), webhooks, auto-suspend/reactivate |
| 2026-05-07 | 4.6 Backup/Restore | BackupService via Agent, create/restore/list endpoints |
| 2026-05-07 | 4.7 Plesk Migration | PleskMigrationService (XML API import + backup file), domains/email/databases, MigrationController |
| 2026-05-07 | 3.4+4.5 Webmail+Usage | WebmailService (Roundcube/Snappymail), UsageTrackingService + background job |
| 2026-05-07 | 5.1 App Hosting | AppHostingService (.NET/Node/Python), systemd unit gen, Nginx reverse proxy |
| 2026-05-07 | 5.2 Cron Jobs | CronJob model, CronJobsController CRUD, crontab sync via Agent |
| 2026-05-07 | 5.3 Firewall | FirewallController (UFW rules add/delete/enable/disable) |
| 2026-05-07 | 5.4 Log Viewer | LogsController (access/error/mail logs per domain) |
| 2026-05-07 | 5.9 Authentik SSO | OIDC integration, auto-provision users, SsoController, appsettings config |
| 2026-05-07 | 5.5 Server Updates | ServerUpdatesController + 4 Agent handlers (check/list/install/history) |
| 2026-05-07 | 5.6 Multi-server | Server model, RemoteServerAgent (TCP), ServerAgentFactory, ServersController with health checks |
| 2026-05-07 | 5.7 Installer | hostpanel-install.sh - full bootstrap for Ubuntu/Debian (Nginx, PHP, MSSQL, MySQL, PG, mail, DNS, firewall) |
| 2026-05-07 | 5.8 MCP Server | hostpanel-mcp with 17 tools (domains, DNS, SSL, email, databases, files, backups, billing, customers) |
| 2026-05-07 | 5.10 n8n Integration | WebhookDispatcher (HMAC-signed), 23 event types, WebhooksController, n8n-nodes-hostpanel (action + trigger nodes) |
| 2026-05-07 | Security Review Fixes | All Critical + High issues from Cursor review fixed (see below) |

## Security Fixes Applied (from CODE_REVIEW_FINDINGS.md)
- **CRITICAL: Webhook signatures** - Stripe HMAC-SHA256 verification + replay protection; PayPal API verification endpoint
- **CRITICAL: Cross-tenant access** - TenantAuthorizationService added, ownership checks on ALL single-resource endpoints
- **CRITICAL: SSO open redirect** - returnUrl sanitized (relative paths only), token moved to URL fragment
- **CRITICAL: Agent token leak** - Removed from logs, only length shown
- **HIGH: Missing agent handlers** - Implemented all: backup, cron, firewall, PHP-FPM, SSL, log reader (22 total handlers)
- **HIGH: Billing heuristic** - Replaced email.Contains("stripe") with PreferredPaymentProvider on Subscription
- **HIGH: PostgreSQL** - Replaced \gexec with createdb binary
- **HIGH: ProcessRunner timeout** - Now kills child process tree on timeout/cancellation
- **HIGH: Socket ACL** - Set Unix file mode (user+group read/write only)
- **MEDIUM: API key auth** - ApiKeyAuthHandler added, works alongside JWT, ApiKeysController for key management
- **MEDIUM: Roundcube GUID** - Fixed raw string interpolation bug
- **MEDIUM: Logs via Agent** - LogsController now reads logs through Agent (privilege separation)
