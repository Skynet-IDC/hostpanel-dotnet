# HostPanel Phase 6 + Frontend Review Checklist

Review of new agent handlers, frontend pages, and remaining gaps.
Updated after Cursor third-pass validation (2026-05-08).

---

## Agent Handlers (28 + 4 AppConfig = 32 methods)

### WordPress (5 handlers) — CONFIRMED IN CODE
- [x] `WordPressInstallHandler` - MySQL DB creation uses string interpolation. **OPEN: injection risk** — dbName/dbUser/dbPass interpolated into `-e` MySQL command (WordPressHandlers.cs ~32-44). ValidateName not applied.
- [x] `WordPressCloneHandler` - **OPEN: uses `bash -c` for mysqldump pipe** — contradicts no-shell-pipeline rule. Refactor to temp file + two commands.
- [x] `WordPressPushStagingHandler` - Same `bash -c` mysqldump issue. Uses rsync — **rsync not explicitly in installer deps** (usually present on Ubuntu but not guaranteed by script).
- [x] `WordPressUpdateHandler` - Runs WP-CLI as `--allow-root`, chown after. Confirmed working pattern.
- [x] `WordPressSecurityScanHandler` - Uses `UnixFileMode` (Windows build warning CA1416). Acceptable — agent only runs on Linux.

**OPEN: WP-CLI not in installer.** Handler calls `/usr/local/bin/wp` but `hostpanel-install.sh` never installs it. Non-zero exit paths from WP-CLI are tolerated (install continues via wget+tar), but update/scan/staging will fail.

### Docker (5 handlers) — CONFIRMED IN CODE
- [x] `DockerCreateHandler` - Parses env vars from JSON, comma-split ports each passed as `-p`. **OPEN: no port format validation** — arbitrary flags accepted.
- [x] `DockerStartHandler` / `DockerStopHandler` / `DockerRestartHandler` - Confirmed; idempotent.
- [x] `DockerRemoveHandler` - Stops then removes. Stop on already-stopped container is idempotent — benign.

**OPEN: Docker Engine not in installer.** `hostpanel-install.sh` has no docker installation step.

### Git / FTP / Security (7 handlers) — CONFIRMED IN CODE
- [x] `GitDeployHandler` - Runs `git clone` with user-provided `repoUrl`. **OPEN: no URL scheme validation** — `file:///etc/passwd` or other local paths accepted.
- [x] `FtpCreateAccountHandler` - **OPEN: shell pipeline injection** — uses `bash -c "echo 'user:pass' | chpasswd"`. Password with `'` chars breaks or injects. Also has a dead no-op `ProcessRunner.RunAsync("/usr/sbin/chpasswd", [], ct)` call before the bash line.
- [x] `FtpDeleteAccountHandler` - Confirmed; handles "does not exist" gracefully.
- [x] `ClamScanHandler` - **OPEN: domainId ignored**, hardcoded `/var/www` scan. Should resolve domain's actual docRoot.
- [x] `ClamScanAllHandler` - Scans `/var/www` and `/tmp`. Confirmed.
- [x] `Fail2BanStatusHandler` - Confirmed. fail2ban IS in installer (`install_base()`).
- [x] `Fail2BanUnbanHandler` - Confirmed; validates jail + ip params.

### Analytics (3 handlers) — CONFIRMED IN CODE
- [x] `AnalyticsVisitorsHandler` - **OPEN: reads ALL `*.access.log` files**, domainId parameter received but unused for filtering. Should match by domain name in filename.
- [x] `AnalyticsBandwidthHandler` - vnstat primary, log file size fallback. **OPEN: vnstat not in installer.**
- [x] `AnalyticsServerResourcesHandler` - Reads `/proc/meminfo`, `/proc/loadavg`, runs `df`. Linux-only (correct).

### Apps / Domain Settings / HA / Panel (10 handlers) — CONFIRMED IN CODE
- [x] `AppInstallHandler` - **Stubs/partial.** WordPress redirects to WP endpoint. Generic apps need download URL. Ghost runs `npm install -g ghost-cli`.
- [x] `DomainHotlinkProtectionHandler` - **OPEN: returns Nginx config snippet but does NOT write it** to the domain's Nginx config file. Needs to write to includes directory.
- [x] `DomainIpDenyHandler` - **Same: returns deny block but doesn't apply it.**
- [x] `DomainPasswordProtectHandler` - Calls `/usr/bin/htpasswd`. **OPEN: apache2-utils not in installer** (htpasswd may be absent).
- [x] `HaConfigureStandbyHandler` - rsync cron with no SSH key setup. **OPEN: replaces ENTIRE user crontab** with single rsync line (`crontab tmpfile`), destroying any existing cron jobs. Worse than documented.
- [x] `HaPromoteToPrimaryHandler` - `crontab -r` removes ALL crontab entries. **OPEN: destructive to non-sync cron jobs.**
- [x] `PanelUpdateHandler` - Backup `${installDir}_backup_<timestamp>`, git pull, dotnet publish, restart. Confirmed.
- [x] `PanelRollbackHandler` - Searches `hostpanel_backup_*` in parent dir. **Naming matches** PanelUpdateHandler — rollback pattern is correct.

### App Configuration (4 handlers) — NEW
- [x] `AppConfigureHandler` - Supports systemd / passenger / pm2 restart methods. Writes .env file, generates systemd unit or Passenger config. Nginx reverse proxy with Next.js `/_next/static` alias.
- [x] `AppBuildHandler` - Runs `npm ci` + multi-step build commands (supports `npx prisma migrate deploy && npm run build`).
- [x] `AppRestartHandler` - Passenger: `touch tmp/restart.txt`. PM2: `pm2 restart`. Systemd: `systemctl restart`.
- [x] `AppWriteEnvHandler` - Writes JSON env vars as `.env` file to working directory.

---

## Handler Registration + Contract — VERIFIED
- [x] All 32 methods in `AgentMethods.AllMethods` (AgentMethods.cs ~112-139)
- [x] All 32 handlers registered in `Agent/Program.cs`
- [x] Method string constants match between controllers and handlers
- [x] HA typo fixed: `ha.promoteToPrimary` (was `ha.promoteToP primary`)

---

## Frontend Pages (20 .tsx files) — ALL COMPILE

### Confirmed in App.tsx routes + Sidebar
- [x] DashboardPage, DomainsPage, EmailPage, DnsPage, DatabasesPage, FilesPage
- [x] SslPage, BillingPage, CustomersPage, FirewallPage, CronPage
- [x] ServerUpdatesPage, AnalyticsPage, SecurityPage, AppsPage
- [x] WordPressPage, DockerPage, GitDeployPage, SettingsPage
- [x] LoginPage
- [x] Sidebar reorganized: Overview, Hosting, Email, Applications, Server, Business sections
- [x] `npm run build` succeeds — 102 modules, 362KB JS, 0 errors

---

## Database — VERIFIED
- [x] Phase 6 models (WordPressSite, FtpAccount, GitDeployment, DockerContainer, ServerIpAddress, IpBlacklistEntry, TotpToken, BrandingConfig) are in `InitialCreate` migration — NOT in the Phase6Models migration (which is empty/stub)
- [x] AppConfig model added with separate `AppConfigModel` migration
- [x] No FK cycle issues in new models
- [x] Self-referential FK on AppUser fixed to `DeleteBehavior.Restrict`

---

## Known Gaps — CONFIRMED OPEN

### Missing from installer (features won't work without these)
- [ ] **Docker Engine** — DockerController calls `/usr/bin/docker` but not installed
- [ ] **WP-CLI** — WordPress Toolkit calls `/usr/local/bin/wp` but not installed
- [ ] **vnstat** — Analytics bandwidth uses `/usr/bin/vnstat` but not installed
- [ ] **apache2-utils** — Password protection uses `/usr/bin/htpasswd` but not installed
- [ ] **ClamAV** — Security scanner uses `/usr/bin/clamscan` but not installed
- [ ] **rsync** — WordPress staging + HA use `/usr/bin/rsync` but not explicitly in deps
- [x] **fail2ban** — IS installed in `install_base()` ✅

### Security — OPEN ISSUES
1. **Shell injection in WordPress handlers** — MySQL commands via string interpolation (High)
2. **Shell injection in FTP handler** — `bash -c "echo 'user:pass' | chpasswd"` (High)
3. **Git repoUrl no validation** — accepts `file://` scheme for local file access (High)
4. **Docker no image whitelist** — can pull any image (Medium)
5. **Domain settings handlers don't apply** — return snippets but don't write to Nginx (Medium)
6. **HA crontab destructive** — both configure and promote destroy entire crontab (Medium)
7. **ClamScan ignores domainId** — scans all of /var/www (Low)
8. **Analytics reads all logs** — no per-domain filtering (Low)

### Tenant Isolation — VERIFIED GOOD
- [x] `WordPressController.List` uses `FilterDomains` for tenant scoping
- [x] All siteId actions use `CanAccessSiteAsync` (staging, push, scan, maintenance, auto-update)
- [x] `update-all` restricted to `[Authorize(Roles = "Admin")]` — platform-wide, not per-tenant
- [x] `security-scan` restricted to `[Authorize(Roles = "Admin,Reseller")]` — was missing role attr, now added
- [x] AppConfigController uses `CanAccessDomainAsync` on all endpoints

---

## Priority Fix Order

1. **Shell injection** in WordPress MySQL + FTP chpasswd (High — security)
2. **Git repoUrl validation** — block `file://`, `ssh://` without allowlist (High — security)
3. **Add missing installer deps** — Docker, WP-CLI, vnstat, apache2-utils, ClamAV, rsync (Medium — features broken)
4. **Domain settings write to Nginx** — hotlink + IP deny need to actually apply (Medium — functionality)
5. **HA crontab safety** — append sync entries instead of replacing entire crontab (Medium — data loss)
6. **FTP dead code** — remove no-op chpasswd call (Low — cleanup)
7. **ClamScan per-domain** — resolve domainId to docRoot (Low — correctness)
8. **Analytics per-domain** — filter log files by domain name (Low — correctness)
