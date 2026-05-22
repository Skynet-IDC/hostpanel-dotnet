# HostPanel Full Backend Test Pass — 2026-05-21

User-requested top-to-bottom test of all backend functionality. Scope:
- Destroy + reinstall VM 280 from GitHub main (commit `a86a84d`)
- Smoke-test all API + agent surfaces
- HA tests using VM 280 (primary) + VM 281 (standby, untouched)
- Plesk migration discovery against Hostinger Plesk
- Mail roundtrip via Testmail

**Rule:** note all failures with reason. Only fix data-loss bugs + installer showstoppers.

## State lost when VM 280 was destroyed (for the record)
- 6 subscriptions, 18 domains, 2 mailboxes, 2 hosted databases, 7 staged Plesk migration jobs
- Existing HA wiring on primary side (lsyncd config, SQL backup cron, ha-rsync-key, sudoers)
- All branding, customers, plans

## Phase A — Installer (destroy + reinstall VM 280)

### A.1 Destroy + reclone — DONE
- Stopped + destroyed VM 280, cloned template 900 → VM 280 (`hostpanel-r17`), resized to 50G, configured cloud-init with LAN IP + gw
- **Note (fixed):** template 900's cloud-init only had `ipconfig0: ip=...` without `gw=...`, leaving the new VM with no default route. Had to add `gw=192.168.10.1` and reboot. Template should probably ship with the default gateway baked in OR the test docs should list it.

### A.2 Install from GitHub main
- Pulled `installer/hostpanel-install.sh` from `mgardner323/hostpanel@main` via raw.githubusercontent.com
- Verified `--sentry-api-dsn` flag present (2 grep matches — flag + Sentry: section in appsettings)
- Launched detached with all flags incl. Sentry DSNs
- **Note (workflow):** curl with `-o` doesn't chmod +x; had to chmod manually before sudo could exec. The published test workflow assumes the operator runs `bash hostpanel-install.sh` or `chmod +x` first; this is documented in README implicitly but not in the README.

_Result: in progress — see PHASE-A-INSTALL-RESULT.md addendum once complete._

### A.3 Post-install service smoke
_TBD after install_

## Phase B — Auth + Bootstrap
- B.1: `GET /api/v1/setup/bootstrap-status` → expect `{adminExists: true}` (since installer passed --admin-email/--admin-password)
- B.2: `POST /api/v1/auth/login` with seeded creds
- B.3: `POST /api/v1/setup/bootstrap-admin` → expect 409 (closed)

## Phase C — Server + Agent
- C.1: `GET /api/v1/server/stats` → real CPU/memory/disk numbers (not zeros)
- C.2: `GET /api/v1/panel/version` + `/panel/check-update`
- C.3: `GET /api/v1/server/updates` (list pending OS updates)
- C.4: `GET /api/v1/server/updates/os-info` + `/server/updates/history` (Phase 19c)

## Phase D — Subscription provisioning (core flow)
- D.1: `POST /api/v1/service-plans` → create a test plan
- D.2: `POST /api/v1/customers` → create test customer
- D.3: `POST /api/v1/subscriptions` with `mainDomain=gardnerclan.us` → expect reconciler to create user, set quota, generate nginx vhost
- D.4: Poll `GET /api/v1/subscriptions/{id}` until `desiredState.state == Active`
- D.5: `curl -H "Host: gardnerclan.us" http://localhost/` → expect 200 (default index)
- D.6: `GET /api/v1/subscriptions/{id}/usage` → real disk/bandwidth/limits

## Phase E — Domain operations
- E.1: `POST /api/v1/domains` (add `test.gardnerclan.us`) → reconciler creates vhost
- E.2: `POST /api/v1/ssl/issue/{domainId}` → LE HTTP-01 cert
- E.3: File ops: upload, list, mkdir, rename, delete
- E.4: DNS records (PowerDNS not for CF zones — expect graceful skip)
- E.5: Per-domain settings: hotlink-protection, IP-deny, password-protect

## Phase F — Mail
- F.1: `POST /api/v1/domains/{id}/mailboxes` → mailbox created in Postfix + Dovecot
- F.2: `POST /api/v1/domains/{id}/aliases` → alias in Postfix
- F.3: `DELETE /api/v1/aliases/{id}` (Tier 2 #5 wire-up check)
- F.4: Run `tests/integration/mail-roundtrip.sh` → SMTP submit + Testmail poll + DKIM verify

## Phase G — Databases
- G.1: `POST /api/v1/databases` type=MySQL
- G.2: `POST /api/v1/databases` type=PostgreSQL
- G.3: `POST /api/v1/databases` type=MSSQL
- G.4: `GET /api/v1/databases/types` + `/databases/status` (Tier 2 #15)
- G.5: `DELETE /api/v1/databases/{id}`

## Phase H — WordPress + Apps
- H.1: `POST /api/v1/wordpress/install` for a domain → wp-cli completes wizard
- H.2: Front page returns 200 + site title rendered
- H.3: `GET /api/v1/domains/0/apps/available` → app catalog list
- H.4: `POST /api/v1/wordpress/{id}/update-all` (Tier 2 fix)

## Phase I — Plesk migration (Phase 20)
- I.1: `POST /api/v1/migration/sources` → register Hostinger Plesk (or re-register, key in /etc/hostpanel/plesk-migration-api-key — LOST in reinstall, will need re-creating from Plesk side)
- I.2: `POST /api/v1/migration/sources/{id}/discover` → expect 8 domains
- I.3: `POST /api/v1/migration/sources/{id}/preflight` for `webdesign101.us`

## Phase J — HA (VM 280 + 281)
- J.1: `POST /api/v1/servers` → add VM 281 (192.168.10.236) as remote server
- J.2: `POST /api/v1/ha/configure-failover` → creates HaSyncState row, calls agent on standby
- J.3: `GET /api/v1/ha/replication-lag` (Phase 19c)
- J.4: `POST /api/v1/ha/test` (probe-only — VM 281's lsyncd + cron + watchdog were left alone)
- J.5: VM 281's standby pipeline survived primary reinstall? The standby watchdog has been polling primary at https://192.168.10.235:8443/api/v1/health every 5s — once API comes back, ConsecutiveFailures resets. Should self-recover.

## Phase K — Billing
- K.1: `GET /api/v1/billing/invoices`
- K.2: `POST /api/v1/billing/invoices/generate` (Tier 1 #3)
- K.3: `GET /api/v1/billing/invoices/{id}/pdf` (wkhtmltopdf invoked)

## Phase L — Firewall + Webhooks + Branding + misc
- L.1: `POST /api/v1/firewall/rules` + `DELETE /api/v1/firewall/rules/{n}` (Tier 2 #6)
- L.2: `POST /api/v1/firewall/disable` then `/enable` (Tier 2 #7)
- L.3: `POST /api/v1/webhooks` + `POST /api/v1/webhooks/{id}/test` + `GET /api/v1/webhooks/{id}/deliveries` (Tier 2 #13)
- L.4: `GET /api/v1/webhooks/events` (Tier 2 #14)
- L.5: `PUT /api/v1/branding` (global)
- L.6: `PUT /api/v1/branding/customer/{userId}` + `POST .../logo` (Tier 1 #1)
- L.7: `GET /api/v1/audit-log` → known empty per bug #47 (not fixed this pass)
- L.8: Cron jobs CRUD
- L.9: FTP accounts CRUD
- L.10: Backups: create + restore

## Phase A — INSTALLER PASSED ✅ (2026-05-22 00:34 UTC)
Both VM 280 (`192.168.10.235`) + VM 281 (`192.168.10.236`) cleanly installed from GitHub `main` (commit `bfdab2c`) end-to-end with **no manual intervention**. Exit 0 on both.

**Final installer state — all 7 showstoppers fixed:**
| # | Bug | Commit |
|---|---|---|
| 1 | SOURCE_DIR layout (src/ subdir) | `dcbc043` |
| 3 | imapsync not in repos (NOTE — degraded warn) | n/a |
| 4 | mssql-conf rate-limit reset-failed | `ab727a1` |
| 5 | unattended-upgrades dpkg lock contention | `f475418` |
| 6 | dpkg --configure -a recovery after kill | `52dcb2b` |
| 7 | master.mdf as idempotency marker (not mssql.conf) | `e6fb0ff` |
| 8 | also stop apt-daily.{service,timer} | `bfdab2c` |

**Verified on both VMs:**
- 13/13 services active: hostpanel-api, hostpanel-agent, mssql-server, postfix, dovecot, nginx, mysql, postgresql, redis-server, fail2ban, clamav-daemon, php8.3-fpm, pdns
- `GET /api/v1/health` → 200

**Notes:**
- Build phase took ~25 min per VM (parallel install ~25 min total wall-clock)
- The 10-min "wait for apt-daily to drain" step usually completes in ~3 min on a fresh boot (cloud-init's apt-daily settles quickly)
- imapsync still NOT installed (BUG-003) — Plesk migration mailbox-content copy unavailable until manually installed

## Backend test results — 2026-05-22 01:00–01:15 UTC

After both VMs cleanly installed (Phase A ✅), ran B–G + J + F on VM 280 primary.

| Phase | Test | Result | Notes |
|---|---|---|---|
| **B.1** | `/setup/bootstrap-status` | ✅ PASS | `{adminExists:true, brandingConfigured:false}` |
| **B.2** | `/auth/login` | ✅ PASS | JWT length 572 |
| **B.3** | `/setup/bootstrap-admin` returns 409 | ⚠️ partial | Returned 400 (validation) on empty body, didn't reach 409 path — re-test with full creds |
| **C.1** | `/server/stats` | ✅ PASS | Real memory/disk/uptime/hostname |
| **C.2** | `/panel/version` | ✅ PASS | `{version:"1.0.0", dotnetVersion:"10.0.7"}` |
| **C.4** | `/server/updates/os-info` | ✅ PASS | Phase 19c new endpoint working |
| **D.1** | Create plan | ✅ PASS — **but route is `/ServicePlans` (PascalCase), NOT kebab-case `/service-plans`** (BUG-007) |
| **D.2** | Create customer | ✅ PASS |
| **D.3** | Create subscription with mainDomain | ✅ PASS — systemUsername + docroot + domain row |
| **D.5** | `curl -H Host: test1.gardnerclan.us http://localhost/` | ✅ PASS — **HTTP 200, reconciler chain fully worked** |
| **D.6** | `/subscriptions/1/usage` | ✅ PASS — Meter data + ratios |
| **E.1** | Add additional domain | ✅ PASS — alt vhost created |
| **F.1** | Create mailbox | ✅ PASS — DB row + Postfix maps |
| **F.SMTP** | Send via SMTP submission (587) | ❌ **FAIL (BUG-008)** — Postfix SASL can't find Dovecot's `private/auth` socket |
| **G.1** | Create MySQL DB | ❌ **FAIL (BUG-009)** — "Access denied for user 'root'@'localhost'" (Ubuntu 24.04 root uses auth_socket) |
| **G.2** | Create PostgreSQL DB | ✅ PASS — sudo -u postgres bypass works |
| **G.3** | Create MSSQL DB | ✅ PASS — SA password from appsettings works |
| **G.4** | `/databases/types` + `/status` | ✅ PASS / ⚠️ types right, but status says MySQL inactive when it IS active (BUG-010 — status endpoint wrong) |
| **J.1** | Register VM 281 as Server | ✅ PASS — server id=1 |
| **J.2** | `/ha/configure-failover` | ❌ **FAIL (BUG-011)** — "Primary or standby server not found" — no Server row marked IsLocal=true on fresh install |
| **J.3** | `/ha/replication-lag` | ✅ PASS — returns empty state (no SQL backup file on fresh — expected) |
| **J.4** | `/ha/test` (probe-only) | ✅ correctly returns "No standby configured" — blocked by J.2 above |
| **K.1** | List invoices | ✅ PASS — empty array |
| **Tier 2 #14** | `/webhooks/events` | ✅ PASS — rich event list |
| **Tier 2 #15** | `/databases/types` | ✅ PASS — 4 types |

## Bugs found (note-only unless tagged FIX-DATA-LOSS)

### BUG-007 — kebab-case routes don't work — NOTE-only
`GET /api/v1/service-plans` returns 404. Routes are PascalCase `[controller]` token, so it's `/ServicePlans` or case-insensitive `/serviceplans`. UI may be calling them correctly; just affects scripted REST callers using kebab-case. Recommend adding `[Route("api/v1/service-plans")]` aliases or use `AddRouteServices.LowercaseUrls + UseHyphenInRoutes` convention. Not a showstopper.

### BUG-008 — SMTP submission rejects: Postfix can't find Dovecot SASL socket — NOTE-only (mail submission broken)
Postfix/submission log: `warning: SASL: Connect to Dovecot auth socket 'private/auth' failed: No such file or directory` → `fatal: no SASL authentication mechanisms`. Means **outgoing mail submission via port 587 is broken** on a clean install — customers can't send. Inbound mail (LMTP from Postfix → Dovecot) likely also affected. Recommend wiring `dovecot.conf` to expose `service auth { unix_listener /var/spool/postfix/private/auth { mode = 0660; user = postfix; group = postfix; } }` and restart both. Should be part of `install_mail()` in installer. Real bug, not data-loss.

### BUG-009 — MySQL provisioner uses root password auth, fails on Ubuntu 24.04 — NOTE-only (MySQL DBs un-creatable)
`POST /databases` with type=MySql → "Access denied for user 'root'@'localhost'". Ubuntu 24.04's mysql-server defaults to `auth_socket` plugin for root (peer auth via Unix socket), no password. Panel's `MySqlProvisioner` connects as root with a password from appsettings. Solutions: (a) Match the PostgreSQL pattern — `sudo mysql -uroot` for admin ops (uses socket peer auth, no password needed), OR (b) installer sets a password during install_mysql() and saves to appsettings. Option (b) matches MSSQL pattern. Real bug, not data-loss.

### BUG-010 — `/databases/status` reports MySQL inactive when it is active — NOTE-only
`GET /databases/status` returns `{"engine":"MySql","available":false}` but `systemctl is-active mysql` returns `active`. Status check probably looks for `mysqld` service name not `mysql`. Minor UI display bug.

### BUG-011 — `/ha/configure-failover` fails on fresh install — NOTE-only (HA bootstrap blocked)
`POST /ha/configure-failover` returns 400 "Primary or standby server not found" because the controller queries `_db.Set<Server>().FirstOrDefaultAsync(s => s.IsLocal)` and no Server row is seeded as the local primary during install. Fix: installer's `deploy_hostpanel` should INSERT a self-referential Server row with `IsLocal=true, Hostname=$(hostname), IpAddress=$(LAN_IP)`. Until this fix lands, HA configuration cannot start.

### BUG-001 — INSTALLER SHOWSTOPPER — FIXED (commit `dcbc043`)
**Symptom:** Running installer with `--git-repo https://github.com/mgardner323/hostpanel.git --git-ref main` on a fresh VM gets all the way through apt package installs (Postfix, Dovecot, MSSQL, MySQL, PostgreSQL, Roundcube, Redis, Docker, PHP 8.{1,2,3,4}, wkhtmltopdf), then crashes at the build phase with:
```
[ERROR] Source directory does not contain HostPanel.slnx: /opt/hostpanel-src
```
and retries in a tight loop until killed.

**Root cause:** The repo layout has `HostPanel.slnx` under `src/`, not at the clone root. The `--source` workflow always pointed pre-set paths at `…/src` already, so this never surfaced. The `--git-repo` path (added in commit `0b06285`) doesn't account for the layout.

**Fix:** After cloning, detect the .slnx location. If it's at `$SOURCE_DIR/src/HostPanel.slnx` but not `$SOURCE_DIR/HostPanel.slnx`, transparently descend `SOURCE_DIR="$SOURCE_DIR/src"`. Existing `--source` invocations are unaffected.

**Pushed:** GitHub `mgardner323/hostpanel` commit `dcbc043` on `main`. Pulled fresh installer + re-launched on VM 280.

### BUG-003 — imapsync not available on Ubuntu 24.04 noble (NOTE-only)
**Symptom:** During clean install, `apt-get install imapsync` in `install_mail()` fails with `E: Unable to locate package imapsync`. Installer correctly degrades to a `[WARN]` and continues — no install break.

**Impact:** Plesk migration **cutover** can't copy mailbox contents (imapsync is the IMAP→IMAP relay). Discovery + preflight + staging all still work. WordPress + file migration unaffected.

**Why it happened:** `imapsync` was removed from the Ubuntu repos starting around 22.04/24.04 due to Perl dependency issues. The previous VM 280 must have had it installed out-of-band before our test workflow started.

**Recommended fix (deferred):** install from source — `imapsync` is a single Perl script with CPAN dependencies. Either bake the upstream tarball + `cpanm` install into the installer's `install_mail()` function (similar to the wkhtmltopdf .deb fallback pattern), OR document a manual post-install step.

### BUG-004 — INSTALLER SHOWSTOPPER — FIXED (commit `ab727a1`)
**Symptom:** Fresh-install on a virgin Ubuntu 24.04 VM completes the build phase but fails to start `hostpanel-api`. journalctl shows .NET aborts with `SocketException (0x80004005)` trying to connect to `localhost:1433`. `mssql-server` is in `failed` state. journalctl on mssql-server shows: "The SQL Server End-User License Agreement (EULA) must be accepted before SQL [...]" repeated 3× then `mssql-server.service: Start request repeated too quickly`.

**Root cause:** `apt-get install mssql-server` triggers a postinst that auto-starts the service. SQL Server crashes because no EULA accepted yet (mssql-conf hasn't run). systemd retries 3× then marks the unit as failed with rate-limit. After mssql-conf eventually accepts EULA, `systemctl start mssql-server` is silently ignored because the unit is still rate-limited.

**Fix:** add `systemctl reset-failed mssql-server` before the post-config `systemctl start mssql-server`. Clears systemd's start-rate-limit counter so the now-properly-configured server can come up.

**Pushed:** GitHub commit `ab727a1` on main.

### BUG-005 — OPNsense 1:1 NAT blocks outbound from standby on install (workaround documented)
**Symptom:** Fresh VM 281 (192.168.10.236) cannot reach the internet during install. Pings to `192.168.10.1` (LAN gw) succeed, but pings to `1.1.1.1` or DNS lookups time out. `curl raw.githubusercontent.com` hangs forever.

**Root cause:** OPNsense has an active 1:1 NAT rule (`192.168.10.236 ↔ 45.25.216.221`, uuid `1f586e70-bfc9-42a3-9fcf-c3ef98109e94`) for the HA standby's public-IP routing. When 1:1 NAT is active, OPNsense bypasses the default outbound masquerade for the matched LAN IP. The public IP `.221` has no firewall pinholes for outbound-initiated traffic, so it gets dropped at the WAN edge.

**Same gotcha as the `.220` migration** per [[project_hostpanel_ha_design]] — known pattern.

**Workaround (already in memory but worth re-stating here):**
1. Before install: `POST /api/firewall/one_to_one/toggle_rule/{uuid}` + `POST /api/firewall/one_to_one/apply` to disable the rule.
2. Run install.
3. After install: toggle it back on + apply.

**Recommendation:** add `--skip-network-check` or wrap the install in a script that toggles the NAT for you. Not fixing in the installer itself because the NAT rule is OPNsense-side; bake into the "fresh standby provisioning runbook" instead.

### BUG-002 — Template 900 missing default gateway (NOT fixed)
**Symptom:** Fresh VM cloned from template 900 has `ipconfig0: ip=192.168.10.235/24` with NO `gw=...`. Outbound DNS/HTTP fails (no default route), breaking `curl raw.githubusercontent.com` (and by extension the installer's git-clone fallback).

**Workaround:** Add `--ipconfig0 ip=...,gw=192.168.10.1` when cloning + reboot.

**Recommendation:** Either bake `gw=192.168.10.1` into template 900's cloud-init defaults, OR document this prominently in the canonical install runbook. Currently in [project_hostpanel_test.md](../../Users/mgard/.claude/projects/C--Projects-proxmox/memory/project_hostpanel_test.md) the `qm clone` recipe says `ip=192.168.10.235/24` only (no gw). Leaving this as a note — user said don't fix template-side issues, only installer showstoppers.

