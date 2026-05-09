# HostPanel Code Review Checklist

Review the entire codebase for security vulnerabilities, functionality gaps, and code quality issues.

## Project Structure
- **Solution**: `src/HostPanel.slnx` (.NET 10)
- **Backend API**: `src/HostPanel.Api/` (ASP.NET Core, 18 controllers)
- **Core Models/Interfaces**: `src/HostPanel.Core/`
- **Infrastructure (EF Core, Services)**: `src/HostPanel.Infrastructure/`
- **System Agent (root daemon)**: `src/HostPanel.Agent/`
- **Modules**: `src/HostPanel.Modules.{Web,Mail,Dns,Database,Ssl,FileManager,Billing}/`
- **Frontend**: `src/hostpanel-ui/` (React + TypeScript + Vite + TailwindCSS)
- **MCP Server**: `src/hostpanel-mcp/` (Node.js)
- **n8n Nodes**: `src/n8n-nodes-hostpanel/`
- **Installer**: `installer/hostpanel-install.sh`

---

## Security Review

### 1. Authentication & Authorization
- [ ] JWT key strength - is the default key in `appsettings.json` safe for dev only? Verify it's overridden in production
- [ ] JWT token expiry and refresh token implementation - is refresh token stored/validated server-side or just generated?
- [ ] Password policy (12 chars, upper/lower/digit/special) - is this enforced consistently?
- [ ] RBAC enforcement - verify every controller checks roles properly (Admin > Reseller > Customer)
- [ ] Reseller isolation - can a reseller access another reseller's customers?
- [ ] Customer isolation - can a customer access another customer's domains/files/email?
- [ ] API key authentication - verify `ApiKey` model is used for API auth, not just JWT
- [ ] Authentik SSO callback - verify the callback can't be exploited (open redirect, token injection)
- [ ] Rate limiting on login endpoint - is it implemented or just mentioned?

### 2. Agent Security (CRITICAL - runs as root)
- [ ] **Token authentication** - verify `TokenAuthService` uses constant-time comparison (it does use `CryptographicOperations.FixedTimeEquals`)
- [ ] **Method allowlist** - verify `MethodRegistry` rejects methods not in `AgentMethods.AllMethods`
- [ ] **No shell injection** - verify `ProcessRunner` never uses `sh -c`, only spawns binaries with argv
- [ ] **Input validation** - check all agent handlers validate parameters (domain format, path traversal, username format)
- [ ] **NginxCreateVhostHandler** - domain regex validation, docRoot must be under `/var/www/`, config test before reload with rollback
- [ ] **SystemCreateUserHandler** - username regex validation `^[a-zA-Z_][a-zA-Z0-9_]{0,31}$`
- [ ] **Token storage** - agent token file should be `0600` root-owned. Is this enforced?
- [ ] Socket permissions - is the Unix domain socket restricted to the API user?

### 3. Path Traversal
- [ ] `FileManagerService.ResolveSafePath()` - verify it prevents `../` traversal via `Path.GetFullPath` comparison
- [ ] File upload path - can a crafted filename escape the document root?
- [ ] Agent handlers - do they validate all paths before file operations?
- [ ] Backup file paths - can a user specify arbitrary restore paths?

### 4. SQL Injection
- [ ] EF Core parameterized queries - verify no raw SQL with string concatenation
- [ ] **MsSqlProvisioner** - uses string interpolation in SQL (`$"CREATE DATABASE [{dbName}]"`) - are `ValidateName()` checks sufficient?
- [ ] **MySqlProvisioner** - same concern with mysql command-line execution
- [ ] **PostgreSqlProvisioner** - same concern with psql execution
- [ ] DNS controller - any raw queries?

### 5. XSS / Input Validation
- [ ] Domain names - validated with regex before storage?
- [ ] Email addresses - validated before creation?
- [ ] File content editing - any sanitization needed?
- [ ] Webhook URLs - validated as proper URLs?

### 6. Secrets Management
- [ ] `appsettings.json` has placeholder passwords - verify they're not committed to prod
- [ ] Database passwords for provisioned DBs - are they properly hashed before storage?
- [ ] PayPal/Stripe API keys - stored in config, should they use a secrets manager?
- [ ] SMTP relay passwords - stored in DB, encrypted at rest?
- [ ] Webhook secrets - auto-generated with `RandomNumberGenerator`? Verify entropy

### 7. Webhook Security
- [ ] HMAC-SHA256 signature verification - is it implemented in the dispatcher?
- [ ] Webhook delivery timeout (10s) - can a malicious endpoint cause resource exhaustion?
- [ ] Failure auto-disable (10 failures) - is the threshold appropriate?
- [ ] Test endpoint - is it admin-only?

---

## Functionality Review

### 8. API Completeness
- [ ] All CRUD operations have proper error responses (400, 401, 403, 404, 409)
- [ ] Pagination - are list endpoints paginated? (currently they return all results)
- [ ] Filtering/sorting - do list endpoints support query parameters?
- [ ] Consistent DTO patterns - are all responses using DTOs or leaking entity models?
- [ ] OpenAPI spec generation - does `/openapi/v1.json` include all endpoints?

### 9. Desired-State Reconciliation
- [ ] `ReconciliationService` - verify exponential backoff math (30s, 1m, 2m, 4m, 8m)
- [ ] Max retries (5) - what happens after exhaustion? Is there admin notification?
- [ ] Concurrent reconciliation - can two reconciler runs conflict on the same resource?
- [ ] State machine transitions - verify `ProvisioningState` transitions are valid (no Deleted -> Active)

### 10. Database Module
- [ ] **5 engines registered** - MSSQL, MySQL, MariaDB, PostgreSQL, SQLite
- [ ] `ValidateName()` on all provisioners - consistent regex `^[a-zA-Z_][a-zA-Z0-9_]{0,63}$`
- [ ] Connection string generation - are credentials properly escaped?
- [ ] Password generation - `GeneratePassword()` in DatabasesController - is 24 chars sufficient entropy?
- [ ] SQLite provisioner - is the base path configurable and secure?

### 11. Email Module
- [ ] Multi-port SMTP config generation (25, 587, 465, 2525) - verify master.cf syntax
- [ ] SMTP relay configuration - does the relay password get written to `/etc/postfix/sasl_passwd`?
- [ ] Password hashing (SSHA512) - compatible with Dovecot?
- [ ] Mailbox quota enforcement - is it just stored or actually enforced via Dovecot config?
- [ ] Plan limit check on mailbox creation - verify `MaxEmailAccounts` is enforced

### 12. DNS Module
- [ ] PowerDNS API calls - error handling on API failures?
- [ ] Cloudflare API - token validation, zone ID management
- [ ] Per-domain provider choice - is `DnsProviderType` persisted on the zone?
- [ ] Record ID tracking - `ProviderRecordId` stored for update/delete operations?

### 13. SSL Module
- [ ] ACME account persistence - `account.pem` stored securely?
- [ ] HTTP-01 challenge cleanup - is the challenge file deleted after validation?
- [ ] Certificate renewal - 30-day threshold, 12-hour check interval
- [ ] Nginx SSL config generation - are TLS versions/ciphers current best practices?

### 14. Billing Module
- [ ] PayPal OAuth2 token refresh - is expiry handled?
- [ ] Stripe customer deduplication - searches by email before creating
- [ ] Invoice status tracking - webhook -> DB update flow
- [ ] Auto-suspend logic - grace period enforcement, configurable per plan
- [ ] Auto-reactivate on payment - verified in both webhook handlers and overdue checker?
- [ ] Billing cycle advancement - verify monthly/quarterly/semi-annual/annual date math

### 15. Plesk Migration
- [ ] XML API request format - valid Plesk API packets?
- [ ] Migrated passwords marked as `{MIGRATED}` - is there a force-reset flow?
- [ ] Error collection in `MigrationResult` - are partial failures handled gracefully?
- [ ] Backup file import - is the temp directory cleaned up on failure?

### 16. Multi-Server
- [ ] `RemoteServerAgent` uses plaintext TCP - flag for mTLS upgrade in production
- [ ] `ServerAgentFactory` - correctly routes local vs remote
- [ ] Health check endpoint - stores result and OS info
- [ ] Server model has TLS cert paths but they're not yet used

### 17. Frontend
- [ ] Login flow - JWT stored in localStorage (XSS risk vs convenience tradeoff)
- [ ] Auth context - token refresh on 401?
- [ ] Protected routes - redirect to login on unauthorized
- [ ] File manager - upload size limit, directory traversal via UI?
- [ ] Domain list - proper empty state handling
- [ ] Pages missing: Email management UI, DNS management UI, Database management UI, SSL management UI, Billing UI, Customer management UI (API exists but no React pages yet)

### 18. n8n Integration
- [ ] Webhook event names match between `WebhookEvents.cs` and n8n trigger node options
- [ ] n8n credential test endpoint (`/auth/me`) - works with API key auth?
- [ ] Action node covers all resources and operations
- [ ] Trigger node filters events correctly

### 19. Installer Script
- [ ] OS detection (Ubuntu/Debian only)
- [ ] Minimum spec checks (2GB RAM, 20GB disk)
- [ ] SQL Server Express (free) vs Standard licensing
- [ ] PHP PPA (`ppa:ondrej/php`) - works on both Ubuntu and Debian?
- [ ] Rspamd repo key import - gpg key handling
- [ ] Self-signed cert generation for panel
- [ ] Firewall rules - all necessary ports opened
- [ ] `set -euo pipefail` - script stops on errors

---

## Code Quality

### 20. General
- [ ] Consistent error handling patterns across all controllers
- [ ] Async/await used correctly (no `.Result` or `.Wait()` calls)
- [ ] Disposable resources properly disposed (HttpClient via factory, streams, etc.)
- [ ] EF Core `SaveChanges` timestamp interception working for `UpdatedAt`
- [ ] No N+1 query patterns in list endpoints (verify `.Include()` usage)
- [ ] Logging - structured logging with Serilog, appropriate log levels

### 21. Missing Features to Flag (not bugs, just gaps)
- [ ] EF Core migrations not yet generated (only code-first models exist)
- [ ] No unit tests yet
- [ ] No integration tests yet
- [ ] SignalR for real-time log streaming (mentioned in plan but not implemented)
- [ ] OpenTelemetry tracing (mentioned in plan but not wired up)
- [ ] Hangfire (plan mentions it but using custom BackgroundService instead - is this OK?)
- [ ] Several React pages not yet built (only Dashboard, Domains, Files exist)

---

## Priority Issues to Fix

After review, list findings here ranked by severity:

### Critical (security/data loss risk)
1. _[fill in after review]_

### High (functionality broken)
1. _[fill in after review]_

### Medium (improvements needed)
1. _[fill in after review]_

### Low (nice to have)
1. _[fill in after review]_
