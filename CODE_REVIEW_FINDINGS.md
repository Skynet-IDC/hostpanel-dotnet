# HostPanel — Full codebase review (per [REVIEW.md](REVIEW.md))

**Scope.** Every hand-written source folder under this repo was reviewed: `src/HostPanel.Api`, `src/HostPanel.Core`, `src/HostPanel.Infrastructure`, `src/HostPanel.Agent`, `src/HostPanel.Modules.*`, `src/hostpanel-ui/src`, `src/hostpanel-mcp`, `src/n8n-nodes-hostpanel`, and `installer/hostpanel-install.sh`. **Excluded:** `bin/`, `obj/`, `node_modules/`, build artifacts, and generated assembly info.

**Method.** Static reading of all `.cs` sources (excluding obj), all React/TS/JS integration files, and the installer script. No runtime tests or live API calls.

---

## Executive summary

| Theme | Findings |
|--------|----------|
| **Authorization** | Many endpoints use `[Authorize]` without scoping to tenant (customer/reseller). Several use only role attributes without checking resource ownership. |
| **Billing** | Inbound Stripe/PayPal webhooks do **not** verify cryptographic signatures. Invoice provider selection uses a **broken heuristic** (`User.Email.Contains("stripe")`). |
| **Agent vs API contract** | `AgentMethods.AllMethods` declares many RPC names, but **`HostPanel.Agent` only registers a subset**. Backup, cron sync, firewall, PHP/DNS/Mail/SSL agent paths are missing handlers — related API/features will fail or no-op dangerously. |
| **Architecture drift** | `SslService` and `LogsController` read/write paths on the machine **from the API process** (often `www-data`), conflicting with “API unprivileged / agent privileged” separation. |
| **Integrations** | n8n credentials and MCP send `Authorization: Bearer …`, but **the API has no API-key authentication** wired in `Program.cs` — integrations only work if the value is actually a JWT. n8n trigger does not verify outbound HMAC. |

---

## Severity-ranked findings

### Critical

1. **Forged payment webhooks** — `StripeProvider.HandleWebhookAsync` / `PayPalInvoicingProvider.HandleWebhookAsync` parse JSON and return success without validating `Stripe-Signature` or PayPal transmission verification (`BillingController`). Anyone who can reach the webhook URLs can mark invoices paid and reactivate subscriptions.

2. **Cross-tenant data access** — Endpoints that require only “logged in” (or reseller without ownership checks) expose other tenants’ data by integer ID, including:  
   - `DomainsController.Get`, `Update`, `Delete` (list filtered; **single-resource not**)  
   - `DatabasesController.List`  
   - `BillingController.ListInvoices`  
   - `DnsController.ListRecords`  
   - `EmailController` mailbox/alias lists  
   - `SslController.GetStatus`, `ToggleAutoRenew`  
   - `LogsController` access/error logs  
   - `CronJobsController.List`  
   - `BackupsController.List`  
   - `ServerController.GetStats` (aggregate counts for all tenants)

3. **Reseller / admin targeting** — `SubscriptionsController.Create` accepts any `UserId` without `CanAccess` on the target user. `DomainsController` / `DatabasesController` / DNS / backup / cron **mutations** do not verify the acting reseller may manage that subscription.

4. **SSO** — `SsoController`: unvalidated `returnUrl` on error redirects (open redirect); JWT returned in query string (history/referrer leakage).

5. **Agent token in logs** — `HostPanel.Agent.Worker` logs the full agent token at startup.

6. **Remote agent** — `RemoteServerAgent`: plaintext TCP; comments note future TLS — not production-safe as implemented.

### High

1. **Agent implementation gap** — Registered handlers: `SystemStatsHandler`, user/quota, nginx vhost/reload, system update handlers only. **Not implemented in agent** (but in allowlist): `backup.create`, `backup.restore`, `system.setCrontab`, `firewall.*`, `php.*`, `dns.*`, `mail.*`, `ssl.*`.  
   - `BackupService` / `CronJobsController` / `FirewallController` call these → **fail** at “no handler” or never achieve effect.  
   - `CronJobsController` comment even notes crontab method as “would be added”.

2. **Firewall list/rules bug** — `FirewallController.ListRules` calls `AgentMethods.SystemGetStats` with a `type=firewall` parameter; **`SystemStatsHandler` ignores parameters** → not a firewall listing. `FirewallController.AddRule` passes string method `"firewall.addRule"` instead of `AgentMethods.FirewallAddRule` (works if equal literal, but inconsistent); **no agent handler exists** anyway.

3. **Usage metering non-functional** — `UsageTrackingService.GetDiskUsageBytesAsync` expects `diskUsageBytes` from `SystemGetStats`, but **`SystemStatsHandler` never returns it** → always `0`.

4. **Billing provider selection** — `BillingCycleService` sets `Invoice.Provider` using `sub.User.Email?.Contains("stripe")` — wrong, non-deterministic, and unrelated to real payment preferences.

5. **SSL / ACME from API** — `SslService` writes under `domain.DocumentRoot` and `_certBasePath` using the **API process** identity. On Linux, `www-data` typically **cannot** write `/etc/hostpanel/ssl` or customer trees unless permissions are broad — issuance likely fails or violates least privilege.

6. **Logs from API** — `LogsController` reads `/var/log/nginx/...` via `File.ReadLines` in the API process — same privilege / deployment mismatch; **no domain tenancy check**.

7. **PostgreSQL provisioning** — `PostgreSqlProvisioner` uses `psql -c` with `\gexec` meta (`RunPsqlAsync`). **`\gexec` is unreliable in non-interactive `-c` mode**; DB creation may be broken — needs verification or `Npgsql`/`CREATE DATABASE` via library.

8. **Second `AddAuthentication`** — `Program.cs` adds JWT, then conditionally calls `AddAuthentication().AddOpenIdConnect(...)` — easy to misconfigure default schemes; prefer **one** builder with both schemes.

9. **Refresh tokens** — Issued in `AuthController` but **not stored or validated**; `ExpiresAt` in response hard-coded to 24h vs `Jwt:ExpiryHours`.

10. **No login rate limiting** — Brute-force still partially mitigated by Identity lockout only.

11. **MySQL root password on argv** — `MySqlProvisioner` passes `-p{password}` (visible in `ps`).

12. **ProcessRunner timeout** — Does not kill child process on timeout (`ProcessRunner`).

13. **Agent token file** — `TokenAuthService` may create token file without `chmod 600` on all code paths (installer does set `600` for production token).

14. **Unix socket ACL** — No `chmod` on `agent.sock`; any local user able to open the socket path may speak JSON-RPC if they learn the token (defense in depth: restrict socket group/mode).

### Medium

1. **Defaults in repo `appsettings.json`** — Dev JWT key, admin password, `AllowedHosts: "*"` — must be overridden in production.

2. **Webhook registration URL** — `WebhooksController.Create` does not validate URL (SSRF risk from panel host).

3. **Outbound webhooks** — `WebhookDispatcher`: 10s timeout, HMAC when secret set; parallel `Task.WhenAll` to all endpoints — slow/dead endpoints consume thread pool (mitigated partially by failure cap).

4. **Webhook consumer verification** — n8n `HostPanelTrigger.node.js`: **HMAC verification not implemented** (comment only).

5. **MCP / n8n “API key”** — Both send `Authorization: Bearer <value>`. **No API key handler** in `Program.cs`; only works if the credential is a **JWT** (misleading UX).

6. **n8n action node gaps** — `HostPanel.node.js`: `subscription` **create** is offered in UI but **not implemented** in `execute` routing; several resources omit delete/restore paths present in the API (e.g. backup restore, DB delete, DNS mutations).

7. **n8n trigger event parity** — Options omit some `WebhookEvents.All` entries (e.g. `subscription.deleted`, `customer.deactivated`, `mailbox.deleted`, `database.deleted`).

8. **Service plans API** — `ServicePlansController` returns full `ServicePlan` **entities** on GET (may be acceptable for catalog but leaks internal fields vs DTOs).

9. **Mail** — `MailService` persists mailboxes and generates Postfix/Dovecot snippets; **no agent deployment** of configs from this code path in review — runtime mail may not match DB. Dovecot quota example hard-codes `500M` in `GenerateDovecotConf` vs per-mailbox `QuotaMb`.

10. **Reconciliation** — Backoff math matches REVIEW checklist (30 × 2^(n-1) seconds). No row locking → duplicate workers could double-process. When work is in **Updating** state, reconciler still runs `ProvisionSubscriptionAsync` only (no distinct update path). No admin alert when `MaxRetries` exhausted.

11. **Nginx rollback** — `NginxCreateVhostHandler`: on failed `nginx -t`, symlink/config removed but **directory tree may remain**.

12. **DNS providers** — `PowerDnsProvider` uses `EnsureSuccessStatusCode`; errors bubble as exceptions — OK if callers catch. Cloudflare/New zone uses `jump_start` — documented behavior depends on DNS at registrar.

13. **`WebmailService.GenerateRoundcubeConfig`** — Uses `Guid.NewGuid()` inside a **verbatim** string (`GenerateRoundcubeConfig`); **`{{Guid.NewGuid():N}}` will not substitute** at runtime in C# raw string — **placeholder bug**.

14. **Plesk migration** — API import uses plausible XML packets; passwords stored as `{MIGRATED}` — **no automated force-reset flow** in app. Backup import is **stub** (notes only; cleanup of temp dir in `finally` is OK).

15. **Migrations** — `Program.cs` calls `MigrateAsync()`; **no `Migrations/*.cs` snapshot** present under `HostPanel.Infrastructure` in this tree — clarify whether migrations are generated elsewhere or startup is fragile.

### Low

1. **Frontend** — JWT in `localStorage`; 401 clears and redirects (`client.ts`). Routes: Dashboard, Domains, Files only; SSO callback route **not** in `App.tsx` (may 404 unless separate host). Domains page has **empty state** (good).

2. **Pagination / filtering** — Most list endpoints return full sets.

3. **HttpClient lifetime** — `PowerDnsProvider`, `CloudflareDnsProvider`, `StripeProvider`, `PleskMigrationService` use `new HttpClient` or long-lived instances — prefer `IHttpClientFactory`.

4. **Installer** — `set -euo pipefail` present; RAM/disk warnings (not hard fail). **`install_dotnet` / SQL Server repos use Ubuntu-specific Microsoft URLs** — **Debian installs are likely broken** despite preflight allowing `debian`. `add-apt-repository ppa:ondrej/php` is **Ubuntu-oriented** (may fail on Debian). Uses deprecated `apt-key` for MSSQL/Rspamd. Agent token file `chmod 600` — good. Self-signed panel cert generated; UFW opens expected ports.

5. **Tests / SignalR / OTel / Hangfire** — Not present; background work uses custom `BackgroundService` — acceptable if documented.

---

## REVIEW.md checklist (quick status)

| # | Topic | Status |
|---|--------|--------|
| 1 | JWT key / expiry / refresh | PARTIAL — key OK for dev; refresh unused; expiry mismatch |
| 1 | Password policy | OK in `Program.cs` Identity |
| 1 | RBAC + isolation | FAIL — resource checks missing widely |
| 1 | API key auth | FAIL — model only; no handler |
| 1 | SSO / rate limit | FAIL / missing |
| 2 | Agent token / allowlist / ProcessRunner | PARTIAL — compare OK; **handlers missing**; token logged |
| 2 | Nginx / system user validation | MOSTLY OK on implemented handlers |
| 2 | Token file 0600 / socket ACL | PARTIAL |
| 3 | FileManager traversal | OK baseline; upload filename harden |
| 3 | Backup path user control | Restore uses DB path only — OK |
| 4 | SQL injection | Controlled via `ValidateName` + escaping in provisioners |
| 5 | XSS / validation | Domain not always regex-validated at API; webhook URL weak |
| 6 | Secrets | Config + SMTP in DB — encryption not reviewed in depth |
| 7 | Webhook HMAC | Outbound signing OK; inbound payment FAIL; n8n verify missing |
| 8 | API errors / pagination / DTOs | Inconsistent |
| 9 | Reconciliation | Backoff OK; locking/alerts/state machine partial |
| 10 | DB module (5 engines) | OK registration; Postgres psql concern |
| 11 | Email module | Generation helpers OK; deploy + quota consistency gaps |
| 12 | DNS module | Provider + controller OK; tenancy missing on read |
| 13 | SSL module | TLS 1.2/1.3 in nginx snippet; ACME cleanup partial; privilege issue |
| 14 | Billing module | Webhook FAIL; provider heuristic FAIL; overdue/suspend logic present |
| 15 | Plesk migration | PARTIAL — API path plausible; backup stub |
| 16 | Multi-server | Factory OK; TCP plaintext |
| 17 | Frontend | localStorage JWT; limited pages |
| 18 | n8n | Event / operation parity gaps; HMAC not verified |
| 19 | Installer | Strong structure; OS/repo gaps for Debian |
| 20 | Code quality | Generally async; timestamps in `AppDbContext`; few antipatterns |
| 21 | Missing features | Tests, migrations, SignalR, OTel — as expected |

---

## Agent method coverage (contract vs implementation)

| Method group | In `AgentMethods.AllMethods` | Handler in `HostPanel.Agent` |
|--------------|-----------------------------|------------------------------|
| system.create/delete user, setQuota, getStats | Yes | Yes |
| nginx.* | Yes | Yes |
| system.check/list/install updates, updateHistory | Yes | Yes |
| backup.* | Yes | **No** |
| system.setCrontab | Yes | **No** |
| firewall.* | Yes | **No** |
| php.*, dns.*, mail.*, ssl.* | Yes | **No** |

---

## Files reviewed (representative)

- **API:** All controllers under `HostPanel.Api/Controllers/`, `Program.cs`
- **Infrastructure:** `AppDbContext`, `JwtService`, `LocalServerAgent`, `RemoteServerAgent`, `ServerAgentFactory`, `ReconciliationService`, `ReconciliationBackgroundService`, `SubscriptionReconciler`, `WebhookDispatcher`, `BackupService`, `UsageTrackingService`, `PleskMigrationService`
- **Agent:** `Program.cs`, `Worker`, all `Services/*Handler*.cs`, `ProcessRunner`, `MethodRegistry`, `TokenAuthService`
- **Modules:** Web (`AppHostingService`), Mail (`MailService`, `WebmailService`), Dns (`*Provider*`, `DnsProviderFactory`), Database (all provisioners + factory), Ssl (`SslService`, `SslRenewalBackgroundService`), Billing (`BillingCycleService`, both providers), FileManager (`FileManagerService`)
- **Core:** DTOs, interfaces, enums, models (all non-generated)
- **UI:** All `hostpanel-ui/src/**`
- **MCP:** `hostpanel-mcp/index.js`
- **n8n:** `nodes/**`, `credentials/**`
- **Installer:** `hostpanel-install.sh` (full script)

---

## Suggested fix order

1. Verify **inbound** Stripe/PayPal webhooks with official signature algorithms; add negative tests.  
2. Introduce **resource authorization** helpers and apply across every scoped controller action.  
3. **Implement or remove** agent methods: prioritize `backup.*`, `system.setCrontab`, `firewall.*`, then PHP/DNS/mail/ssl if still routed through agent.  
4. Replace billing **provider heuristic** with explicit subscription/user payment preference.  
5. Move file/log/ssl operations that require elevated FS access to **`IServerAgent`** (or grant narrowly scoped POSIX ACLs — agent preferred).  
6. Remove agent token from logs; tighten UDS permissions.  
7. Fix **PostgreSQL** provisioning (`Npgsql`).  
8. Wire **API key** auth or rename MCP/n8n docs to **JWT-only**.  
9. Fix **Roundcube config** GUID generation in `WebmailService`.  
10. Add EF **migrations** to repo or adjust startup expectations.

---

*This document supersedes the earlier partial review and reflects a full-folder pass against [REVIEW.md](REVIEW.md).*
