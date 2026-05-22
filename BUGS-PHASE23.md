# Phase 23 — UI test findings + fixes

Live log of every bug surfaced during the Playwright walk of HostPanel UI.
Each item is `UI-BUG-NNN` numbered sequentially. Routes from `App.tsx`,
auth as admin (`mgardner323@gmail.com`).

Frontend bundle running at audit start: `index-BMCljvuv.js` (built by installer
from main HEAD `24631b8`). After fixes, current bundle is `index-CDZjCi8v.js`
(commit `bf66e2b`).

## Visual smoke summary (28 routes)

- **22 routes clean** — no console errors, page renders the expected
  empty-state or content.
- **5 routes had crashes** (UI-BUG-001/002/003/004/005) — all FIXED + deployed.
- **1 route has a backend feature gap** (UI-BUG-006) — page degrades cleanly,
  backend endpoints to be added later.

| Route | Result | Notes |
|---|---|---|
| `/` | PASS post-fix | was UI-BUG-001/002 |
| `/customers` | PASS | |
| `/plans` | PASS post-fix | was UI-BUG-001 |
| `/subscriptions` | PASS post-fix | was UI-BUG-001 |
| `/domains` | PASS | |
| `/files` | PASS | |
| `/ftp` | PASS | |
| `/email` | PASS | |
| `/databases` | PASS post-fix | was UI-BUG-003 |
| `/ssl` | PASS | |
| `/dns` | PASS | |
| `/backups` | PASS | |
| `/wordpress` | PASS | |
| `/docker` | PASS | |
| `/git` | PASS | |
| `/cron` | PASS | |
| `/logs` | PASS | |
| `/claude` | PASS | |
| `/firewall` | PASS | |
| `/ip` | PASS | |
| `/fail2ban` | PASS post-fix | was UI-BUG-005 |
| `/2fa` | PASS post-fix | was UI-BUG-004 |
| `/malware` | PASS (gap) | UI-BUG-006 — page renders, backend endpoints missing |
| `/audit` | PASS | |
| `/webhooks` | PASS | |
| `/updates` | PASS | |
| `/servers` | PASS | |
| `/migration` | PASS | |
| `/branding` | PASS | |
| `/settings` | PASS | |
| `/account` | PASS | (3 console msgs at info level, no errors) |
| `/ha` | PASS | |


---

## UI-BUG-001 — Three API client URLs hardcoded `/plans` instead of `/serviceplans` — **FIXED**

**Surfaced:** Logging in lands on `/` with a console 404 on `/api/v1/plans`.
Same 404 on `/plans` and `/subscriptions` routes too.

**Root cause:** Backend serves `ServicePlansController` at `/serviceplans`,
`/service-plans`, and `/ServicePlans` (kebab convention) — but never at `/plans`.
Three frontend files hardcode `/plans`:
- `api/panel.ts:61` (`listPlans`)
- `pages/PlansPage.tsx:162-163` (PUT/POST)
- `components/wizard/SetupWizard.tsx:92` (POST during onboarding)

The other two callers (`api/domains.ts:39`, `pages/SubscriptionsPage.tsx:271,285`)
correctly use `/serviceplans` — so the right URL was already in the codebase,
just inconsistently applied.

**Fix:** Updated all three to `/serviceplans`. Pattern now consistent across all callers.

**Severity:** Medium. The error is recoverable (PlansPage already handles the 404
with an inline message + functional empty state), but the network noise and
broken dashboard plans-widget were real.

---

## UI-BUG-002 — Dashboard crashes because /audit returns a paginated wrapper, not an array — **FIXED**

**Surfaced:** Logging in lands on `/`. Console shows the UI-BUG-001 404, but
the **page crash** ("Something went wrong" Error Boundary) is actually from
`audit.map is not a function`.

**Root cause:** `panelApi.recentAudit(8)` types its result as `PanelAuditEntry[]`
but the backend's `/api/v1/audit` endpoint was paginated in a later phase and now
returns `{ items: [], totalCount: 0, page: 1, pageSize: 50, totalPages: 0,
hasNext: false, hasPrev: false }`. DashboardPage then calls `audit.map(...)` on
the wrapper object → TypeError → React Error Boundary catches it → user sees
"Something went wrong" instead of the dashboard.

**Fix:** `panel.ts` unwraps the `items` field if present, else passes through
(`Array.isArray(r.data) ? r.data : (r.data?.items ?? [])`). Belt-and-suspenders:
works whether the backend returns paginated or bare-array, and survives a future
revert.

**Severity:** **HIGH** — full-page crash on first thing every admin sees after
login. Worse, the Error Boundary's "Try again" button just re-renders into the
same crash. The dashboard was completely unusable on a fresh install until this fix.

---

## UI-BUG-003 — Databases page crashes rendering supportedTypes — **FIXED** (pending deploy)

**Surfaced:** Navigating to `/databases`. React error #31:
"Objects are not valid as a React child (found: object with keys {type, value})".

**Root cause:** `/databases/types` returns
`[{"type":"MsSql","value":0}, {"type":"MySql","value":1}, ...]`
(enum-with-numeric-value shape from a later backend phase) but the page
typed it as `string[]` and the fallback path then rendered each `t.type`
into JSX directly, where `t` was the wrapper object. Two-bug compound:
- `/databases/types`: array of objects, not strings (fixed by unwrapping `.type`)
- `/databases/status`: returns `{engine, available, ...}` but DbStatus
  interface keyed on `type`, so `e.type` was undefined when the status
  branch rendered (silent UI miss, fixed by mapping engine→type on load)

**Fix:** Normalized both responses in `DatabasesPage.tsx` useEffect — unwraps
the enum shape from /types, maps engine→type from /status. Defensive for
older bare-string-array responses too.

**Severity:** HIGH — Databases page completely crashes into Error Boundary.

---

## UI-BUG-004 — 2FA page hits wrong URL (405 then degrades) — **FIXED** (pending deploy)

**Surfaced:** `/2fa`. Console: `405 Method Not Allowed @ /api/v1/auth/2fa`.

**Root cause:** TwoFactorPage's load fn calls `GET /auth/2fa`. Backend's
`TwoFactorController` is mounted at `[Route("api/v1/auth/2fa")]` with
actions: `POST /setup`, `POST /verify`, `DELETE /` (root), `GET /status`.
So `GET /auth/2fa` hits the `DELETE /` route by URL match but the verb is
wrong → 405. The endpoint we want is `GET /auth/2fa/status`.

**Fix:** TwoFactorPage.tsx:29 — `/auth/2fa` → `/auth/2fa/status`.

**Severity:** LOW — the page degrades gracefully ("Request failed with
status code 405" inline + "Set up 2FA" empty state stays visible), so
operator can still kick off setup. But the status query is broken.

---

## UI-BUG-005 — Fail2ban page crashes (undefined.reduce) — **FIXED** (pending deploy)

**Surfaced:** `/fail2ban` shows Error Boundary. Console:
`TypeError: Cannot read properties of undefined (reading 'reduce')`.

**Root cause:** Compound. The frontend `Status` interface expects
`{enabled: boolean, jails: Jail[]}` but the backend's
`/api/v1/security/fail2ban` returns the raw `fail2ban-client status` shell
output verbatim:
`{"output":"Status\n|- Number of jail: 1\n`- Jail list: sshd\n","error":""}`.

So `status` is truthy but `status.jails` is undefined → `status?.jails`
returns undefined → `.reduce(...)` on undefined throws.

`status?.jails.reduce(...)` is the wrong chain — `?.` only protects the
parent. Should be `status?.jails?.reduce(...)`.

**Fix (frontend, this commit):**
- Made `Status.jails` and `Status.enabled` optional + accepted the raw `{output, error}` shape.
- Added optional chains on both `.reduce()` and `.length`.
- Added a "Fail2ban (raw output)" Card path that renders the agent text in a
  `<pre>` block when no structured data is available — operator still sees
  fail2ban's status, just unparsed.

**Fix needed (backend, deferred):** Agent's `security.fail2banStatus`
handler should parse fail2ban-client output into structured `{enabled, jails}`
shape. The "raw output" view in the frontend goes away once the agent
returns proper structure.

**Severity:** HIGH — full-page crash.

---

## UI-BUG-006 — /malware page: backend endpoints don't exist — **FIXED**

**Surfaced:** `/malware`. Console: 404 on `/security/malware/scans` AND
`/security/malware/findings`. Page renders cleanly because the fetches
have `.catch(() => ({ data: [] }))` — "Clean — No malware findings" + "No
scans run yet" empty states. "Scan now" button disabled (because no
subscription/domain selected, not because of the 404s).

**Root cause:** SecurityScanController has fail2ban + clam-scan-server but
no GET history/findings + no quarantine endpoints. The frontend was built
expecting them; the backend was never built. Tier 2 backlog item; no UI
crash since frontend degrades.

**Fix (commit `fadd84d`):**
- Added all 4 endpoints to `SecurityScanController`.
- File-backed JSON persistence at `/var/lib/hostpanel/malware-{scans,findings}.json`
  (no new EF migration — small volume, easy to upgrade later).
- New agent handler `SecurityQuarantineFileHandler` (`security.quarantineFile`)
  moves infected files from `/var/www/...` to
  `/var/quarantine/<stamp>.<guid>/<orig-path>` with read-only owner-only perms
  and a sibling `manifest.json` for forensics. Refuses paths outside `/var/www/`.
- Severity heuristic on findings (trojan/ransom/rootkit/backdoor → high,
  adware/pup → low, else medium) so UI can colour-code without knowing the AV taxonomy.

**Verified:** /malware page now 0 console errors. Endpoints return 200 with empty
arrays on a fresh install. "Scan now" button works once a domain is added.

**Severity:** LOW (was deferred, now FIXED).

---

## UI-BUG-007 — WordPress install rejects domains with hyphens — **FIXED**

**Surfaced:** Workflow test #2 (customer → sub → domain → WP install). Domain
`wf-test.example.com`. Install dialog 400'd with "WordPress install failed:
Invalid database name".

**Root cause:** `WordPressToolkit.InstallAsync` line 31:
```csharp
var dbName = $"wp_{...SystemUsername}_{domain.Name.Replace(".", "_")}";
```
Replaces only `.` not `-`. MySQL identifiers reject hyphens, so any customer
domain with a dash (very common in test/staging subdomains) produced an
invalid db name. `dbUser` on the next line already handled both, just the
`dbName` line was a copy-paste miss. Also no truncation against MySQL's
64-char db-name limit, which would have bitten long customer domains too.

**Fix (commit `93bc6b4`):** Introduced `Mysqlify(s)` helper, applied to both
`dbName` and `dbUser`. Added 64-char truncation on `dbName`.

**Verified:** Workflow test #2 retried after redeploy — WP install completed,
site appears at `/wordpress` with version=latest, URL=http://wf-test.example.com.

**Severity:** HIGH — affected any customer whose primary or sub-domain
contained a hyphen, which is the majority of real-world hosting domains.

---

## Phase 23 workflow test results so far

| # | Workflow | Status |
|---|---|---|
| 1 | First-launch wizard | N/A (admin seeded via installer flags) |
| 2 | **Create customer → sub → domain → WP** | **PASS** (after UI-BUG-007 fix) |
| 3 | Per-customer logo upload | not yet run |
| 4 | Quota enforcement | not yet run |
| 5 | Service plan change | not yet run |
| 6 | Custom plan create + attach | not yet run |
| 7 | Real PDF download | not yet run |
| 8 | Manual invoice generation | not yet run |
| 9 | Authentik SSO login | skipped — no Authentik in fresh test env |
| 10 | Bandwidth tracker | not yet run |
| 11 | HA failover from panel | not yet run (would leave standby in promoted state) |
| 12 | Claude PTY terminal | skipped — needs BYOK Anthropic API key |
| 13 | 2FA enrollment | not yet run |
| 14 | Webhook delivery | not yet run |

Test environment state after workflow #2:
- 1 service plan (id 1, "Workflow Test Plan", 5GB/50GB/5dom/5mb/3db)
- 1 customer (Workflow Tester / wf-test@example.com / 12+ char pw)
- 1 subscription (id 1, Active, wf-test.example.com)
- 1 domain (id 1, wf-test.example.com)
- 1 WordPress site (wp-cli installed, latest version)
- Tenant system user, nginx vhost, MySQL DB all provisioned

---

## Methodology note — async-error timing

My initial walk reported `/fail2ban`, `/databases`, and the Dashboard as
"0 console errors" because I called `browser_console_messages` IMMEDIATELY
after `browser_navigate`. Navigate returns when DOM is loaded, but async
API failures fire AFTER. So the check ran before the error.

**Fix going forward:** After every navigate, `browser_wait_for { time: 2 }`
before checking the console. Re-walk every "clean" page so far with this
protocol — at least the dashboard, databases, and fail2ban were silently
broken even though I declared them clean.

---

