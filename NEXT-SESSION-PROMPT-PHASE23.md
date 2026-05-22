# Next session — finish Phase 23 UI testing + wire LambdaTest

Paste the **"What to give Claude"** block at the end. Everything else is
context for the session.

---

## State to resume from

**Branch:** `mgardner323/hostpanel@main` HEAD `8ec017d`.
**VMs:** Both running, no changes needed.
- VM 280 (primary): https://panel.gardnerclan.us:8443 — **real LE cert** (cron auto-renews)
- VM 281 (standby): https://192.168.10.236:8443 — still self-signed (only matters if you try to Playwright it directly, which Phase 23 doesn't)
**Frontend bundle:** `index-BNpuBkiW.js`. If Playwright fails with
`Failed to load module script ... MIME type "text/html"`, the browser cached
an older bundle reference — append `?_=N` to the URL to force-reload.

**Test fixtures on VM 280 (do NOT wipe — workflows depend on them):**
| | |
|---|---|
| Admin | `mgardner323@gmail.com` / `Test@HostPanel2026` |
| Customer | id 1, "Workflow Tester" / `wf-test@example.com` / `WfTester2026Pass!` |
| Sub | id 1, Active, on custom plan "WF Custom Upgrade" $15/mo |
| Domain | id 1, `wf-test.example.com`, vhost up |
| WP site | http://wf-test.example.com, admin `admin`/`WpAdmin2026Pass!` |
| Invoice | id 1, $15.00, Sent, due 2026-06-05 |

## Done so far in Phase 23

**Visual smoke: 28/28 routes clean** (after fixing UI-BUG-001..005).
**Workflow tests: 5/14 PASS** (#2, #5, #6, #7, #8).
**Bugs fixed live this cycle:** UI-BUG-001 through UI-BUG-008 (commits
`a8f8ca2`, `bf66e2b`, `fadd84d`, `93bc6b4`, `4508550`, plus docs).

Full log: `C:\Projects\hostpanel\BUGS-PHASE23.md`. Memory: see
[[hostpanel-phase23-status]] in `~/.claude/projects/C--Projects-proxmox/memory/`.

## Methodology learnings to apply (don't re-learn these)

1. **Always `browser_wait_for {time: 2}` between `browser_navigate` and
   `browser_console_messages`** — first walk falsely declared 3 pages
   "clean" because async fetch errors hadn't fired yet.
2. **After any `npm run build` + rsync, the new bundle has a fresh hash.**
   The browser cache references the old hash. Use `?_=N` URL suffix to force
   a reload. Otherwise you'll see "Failed to load module script ... MIME
   type text/html" (nginx 404→SPA index.html fallback).
3. **Self-signed cert blocks both Playwright MCPs.** Already set up
   `panel.gardnerclan.us` with LE — use it, not the IP.
4. **Verify in BOTH UI + API** — workflow #6 looked fine in dialog but the
   list rendered "—" until I checked. Don't trust dialog success state alone.

## What to do this session — in order

### A. Wire LambdaTest (one-time setup, ~20 min)

User claimed the LambdaTest free 1-year plan. Pack offer is at
https://education.github.com/pack → LambdaTest. Once activated:

1. Generate access key from LambdaTest dashboard → Profile → Access Key.
   Save to `C:\Users\mgard\.claude\secrets\lambdatest.env` as
   `LT_USERNAME=...` + `LT_ACCESS_KEY=...`.
2. Install LambdaTest tunnel (cloud browsers reach `panel.gardnerclan.us` via
   public LE cert already, so tunnel ONLY needed if you want to test the LAN
   IP). Skip for first pass — public hostname is fine.
3. Build a one-shot Playwright config that targets LambdaTest's hub instead
   of local:
   ```typescript
   const browser = await playwright.chromium.connect({
     wsEndpoint: `wss://cdp.lambdatest.com/playwright?capabilities=${
       encodeURIComponent(JSON.stringify({
         browserName: 'Chrome',
         browserVersion: 'latest',
         'LT:Options': {
           platform: 'Windows 11',
           build: 'HostPanel Phase 23',
           name: 'Visual smoke',
           user: process.env.LT_USERNAME,
           accessKey: process.env.LT_ACCESS_KEY,
         },
       }))
     }`,
   });
   ```
4. Re-run the 28-route visual smoke on Chrome + Firefox + Safari + Edge
   (4 browsers × 28 pages × 5s = ~10 min). Goal: spot anything Chromium-only.
5. Re-run on mobile Safari (iPhone 15) + Chrome Android (Pixel 8) — mostly
   for the customer-facing pages (Login, Account, Files, Billing).

### B. Finish the 7 testable workflows

**#3 — Per-customer logo upload** (~10 min):
- Customers → row → Edit → CustomerBrandingPanel
- `browser_file_upload` with a small PNG (create one in `/tmp/test-logo.png`
  via Bash + ImageMagick OR scp from laptop's `C:\Projects\proxmox\test-fixtures/`)
- Verify `GET /branding/customer/{userId}` returns logoUrl + image displayed
  on the customer's portal view

**#4 — Quota enforcement** (~15 min):
- Plan has 5GB disk. Create a 6GB file in the docroot via filemanager file
  upload (or scp into `/var/www/hp_<systemuser>/httpdocs/big.bin` via
  laptop ssh)
- Expect: upload via UI returns 413 with quota error message; if scp'd
  directly to disk, the next `/subscriptions/{id}/usage` reflects > 100%
- This validates Phase 18 quotas wiring end-to-end

**#10 — Bandwidth tracker** (~5 min):
- `for i in {1..1000}; do curl http://wf-test.example.com/; done` from laptop
- Wait 30s for the bandwidth daemon's poll
- GET `/subscriptions/1/usage` → expect `bandwidthBytes > 0` + `bandwidthTracked=true`

**#11 — HA failover from panel** (~20 min, RISKY):
- First: register VM 281 as Server via /servers page (use `hp281-phase23`
  as hostname, IP `192.168.10.236`, port 50051, agent token from
  `ssh mgardner@192.168.10.236 'sudo cat /etc/hostpanel/agent.token'`)
- Then configure-failover via HA dashboard
- Click "Test failover" first (probe-only, safe)
- Then real failover — this DOES flip DNS via Cloudflare. **Set a 5-min
  cleanup reminder** to fail-back before the session ends, otherwise the
  panel will be serving from VM 281 and the next session's fixtures won't be
  on the active node.
- Failback recipe: `/usr/local/bin/hostpanel-ha-failback.sh` on the
  current-primary (after failover, that's VM 281)

**#13 — 2FA enrollment** (~10 min):
- /2fa → "Set up 2FA" → backend returns `{secret, qrCodeUrl}` (per the
  TwoFactorController.Setup action)
- Extract `secret` from the response
- Generate a 6-digit TOTP from the secret using a Python one-liner
  (`python3 -c "import pyotp; print(pyotp.TOTP('SECRET').now())"` — may need
  `pip install pyotp` first)
- Enter the code in the verify dialog
- Log out + log back in — expect 2FA challenge

**#14 — Webhook delivery** (~10 min):
- Start `nc -l 9000` on laptop (or on VM 280 via ssh to localhost:9000)
- Webhooks page → New → URL `http://100.x.x.x:9000/` (laptop's Tailscale IP)
  + secret `webhook-test-secret`
- Trigger an event by creating a 2nd domain on sub 1 (use
  `extra.gardnerclan.us` or similar)
- Verify `nc` receives a POST with `X-HostPanel-Signature` HMAC header
- /webhooks/{id}/deliveries → expect 1 row with status 200

### C. Re-verify after every fix

Same pattern as this session: bug found → fix in `src/hostpanel-ui/` or
backend → push to main → ssh VM 280 → `git fetch + reset --hard origin/main`
→ rebuild (frontend: `cd src/hostpanel-ui && npm run build && rsync -a
--delete dist/ /opt/hostpanel/wwwroot/`; backend: `dotnet publish
src/HostPanel.Api -c Release -o /opt/hostpanel && systemctl restart
hostpanel-api`) → cache-bust URL.

### D. Wrap-up

- Update `BUGS-PHASE23.md` with each new bug fix.
- Push a final commit `docs: Phase 23 complete — N/14 workflows + M bugs fixed`.
- Update [[hostpanel-phase23-status]] to mark Phase 23 done.

## Skipped workflows (decide later)

- **#9 Authentik SSO** — Authentik IS running on Proxmox per
  [[reference_authentik_oidc_gotchas]]. To enable: create OIDC provider
  + application in Authentik, register HostPanel as relying party
  (`POST /api/v1/sso/providers`). 30-45 min, dedicated session worth it
  because it's a real customer-facing feature.
- **#12 Claude PTY terminal** — needs the operator to enter a BYOK
  Anthropic key into the panel. Smoke-test only after the operator has done
  that.

## Pack offers status

- ✅ Sentry (already wired, shipping envelopes; flip session replay on for
  Phase 23 fallout investigation)
- ✅ Testmail (already wired)
- ✅ LambdaTest (claimed, NOT yet wired — Section A above)
- ✅ BrowserStack (claimed, NOT yet wired — for the mobile pass after
  LambdaTest desktop)
- ✅ Polypane (claimed, NOT yet wired — local dev tool for iterating on UI
  fixes; not needed for the smoke pass itself)

---

## What to give Claude (paste this verbatim)

> Pick up Phase 23 UI testing for HostPanel. Read `C:\Projects\hostpanel\NEXT-SESSION-PROMPT-PHASE23.md`
> for full context. Visual smoke is done, 5 of 14 workflows pass, 8 bugs
> fixed in this cycle. Test fixtures are preserved on VM 280 (don't wipe).
> Resume with: (A) wire LambdaTest from the claimed Pack offer so we can
> re-run smoke cross-browser, (B) finish the 7 remaining testable
> workflows (#3 logo upload, #4 quota, #10 bandwidth, #11 HA failover,
> #13 2FA, #14 webhook delivery), (C) commit + update memory.
> Authentik SSO (#9) and Claude PTY (#12) stay skipped unless I say
> otherwise. Apply the methodology learnings up front: 2s wait between
> navigate and console-check, cache-bust URLs after frontend rebuild, use
> `panel.gardnerclan.us` not the IP. Branch is at HEAD `8ec017d`.
