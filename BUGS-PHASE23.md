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

## UI-BUG-006 — /malware page: backend endpoints don't exist (deferred)

**Surfaced:** `/malware`. Console: 404 on `/security/malware/scans` AND
`/security/malware/findings`. Page renders cleanly because the fetches
have `.catch(() => ({ data: [] }))` — "Clean — No malware findings" + "No
scans run yet" empty states. "Scan now" button disabled (because no
subscription/domain selected, not because of the 404s).

**Root cause:** SecurityScanController has fail2ban + clam-scan-server but
no GET history/findings + no quarantine endpoints. The frontend was built
expecting them; the backend was never built. Tier 2 backlog item; no UI
crash since frontend degrades.

**Fix scope (deferred):** Add to SecurityScanController:
- `GET /security/malware/scans` — return scan history from agent log
- `GET /security/malware/findings` — return cached ClamAV detections
- `POST /security/malware-scan` — per-path scan (frontend POSTs to this)
- `POST /security/malware/findings/{id}/quarantine` — move file out of webroot

**Severity:** LOW — UI behaves correctly (degraded but not broken).

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

