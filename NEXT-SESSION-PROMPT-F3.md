# Next session — finish PayPal end-to-end + build the web installer (F3)

Paste the **"What to give Claude"** block at the very bottom. Everything else is context
the agent reads to orient itself.

---

## State to resume from

**Branch:** `mgardner323/hostpanel@main` HEAD `2011c1c`.
**Frontend bundle:** `index-DPC_9HI9.js`. Append `?_=N` cache-buster to URLs after any
rebuild so the browser drops the cached index.html.
**Panel (Playwright-friendly real LE cert):** https://panel.gardnerclan.us:8443
**Standby:** https://192.168.10.236:8443 (self-signed — don't Playwright it directly).

**Test fixtures on VM 280 (do NOT wipe):**
| | |
|---|---|
| Admin | `mgardner323@gmail.com` / `Test@HostPanel2026` (2FA disabled) |
| Customer | "Workflow Tester" / `wf-test@example.com` / `WfTester2026Pass!` |
| Sub | id 1, Active, custom plan "WF Custom Upgrade" $15/mo (10GB) |
| Domain | id 1, `wf-test.example.com`, WordPress installed |
| Invoice | id 1, $15.00, Sent (smoke-test invoices 2+3 also exist as PayPal samples) |
| HA | hp280 primary + hp281 standby, both Healthy, HaSyncStates seeded |
| Branding | per-customer logo at `/branding/customers/cust_63e961bd_*.png` |
| ServicePlans | id 2 "WF Custom Upgrade" (custom, attached to wf-test) |

**Already wired in `appsettings.Production.json` on VM 280:**
- `Dns:Cloudflare:ApiToken` ✓ (verified — `/dns/cf/zones` returns 3 zones)
- Sentry DSNs ✓ (API + UI shipping envelopes)
- Testmail ✓
- **PayPal credentials NOT yet configured** — this is the first piece of work.

## Decisions already locked from previous session

| Topic | Choice |
|---|---|
| Multi-cycle pricing | Per-cycle override prices (4 nullable decimals on ServicePlan, linear fallback) |
| Invoice provider routing | Per-subscription default + per-invoice override at creation |
| Web installer architecture | Full replacement — standalone .NET self-contained binary |
| Self-update behavior | Pull → detect → build only what changed → restart (already shipped F5) |
| Billing focus this session | **PayPal only**. Stripe is future work — leave the IPaymentProvider scaffolding in place but de-emphasize in UI. |

## Methodology learnings to apply (don't re-learn these)

1. **`sites-enabled/hostpanel.conf` is a real file after certbot ran**, not a symlink. Edit
   the file nginx actually serves; `nginx -T | grep <directive>` confirms what's live.
2. **CF API token belongs in `appsettings.Production.json#Dns:Cloudflare:ApiToken`** — not
   in env vars, not in DB (yet). Settings UI could move it to DB later but isn't required.
3. **Empty POST body curl needs `-H "Content-Length: 0"` explicitly** to satisfy some
   middleware/nginx combos. axios handles this correctly from the UI — only a curl gotcha.
4. **EF migrations land in `src/HostPanel.Infrastructure/Data/Migrations/`** (note the
   `Data/` subfolder — the dotnet-ef default), NOT `src/HostPanel.Infrastructure/Migrations/`.
5. **Multi-line commit messages over SSH:** write the message to a file (`/tmp/commit-msg.txt`),
   `scp` it over, `git commit -F /tmp/...` — heredoc escaping breaks when the message has
   `*`, `$`, or `"`.
6. **Always `browser_wait_for {time: 2}` between navigate and console-check** in Playwright
   — fetch-then-render async failures fire after navigation completes.
7. **scp + sudo cp deploy pattern** works when rsync isn't available on Git Bash. `mkdir`
   the target on the VM first, scp into a tmp path, `ssh sudo cp -r`.

## What to do this session — in order

### A. Finish PayPal end-to-end (~1.5 hrs)

**Goal:** From a fresh-install panel, the operator can paste PayPal credentials, generate
an invoice, the customer gets a real PayPal-hosted payment link, and a webhook reconciles
the payment back to HostPanel.

**A1. Settings UI for PayPal credentials.** Today `appsettings.Production.json#Billing:PayPal`
is the only place credentials live, which means edit-file-and-restart workflow. Replace
with a Settings page section:
- Fields: Client ID, Client Secret (password-masked), Webhook ID, Sandbox toggle.
- Backend: new `SettingsController` route group `/settings/billing/paypal` (GET masks the
  secret, PUT upserts). Store in a new `PaymentProviderConfig` EF entity keyed by
  `(Provider, Sandbox)` so future Stripe addition is symmetric. Wrap secret with
  `IDataProtector` so it's not plaintext at rest.
- `PayPalInvoicingProvider`: read from the DB-backed config on each request instead of
  `IConfiguration["Billing:PayPal:..."]`. Cache for 60s to avoid hammering EF.
- Validation: PUT should call PayPal's `/v1/oauth2/token` once with the supplied creds
  before persisting — surface the result inline ("Connected — using sandbox" / "Invalid
  client credentials").

**A2. Verify end-to-end invoice flow on VM 280.** Get PayPal sandbox credentials from
https://developer.paypal.com/dashboard/applications/sandbox (operator may already have
a sandbox app; if not, create one). Paste into the new Settings UI. Generate an invoice
through `/billing` → expect `paymentLink` to be populated with a `https://www.sandbox.paypal.com/invoice/...`
URL. Open it in the browser, complete the payment with a sandbox buyer account, watch
the webhook fire.

**A3. Webhook landing.** `BillingController` already has the route per the controller
grep earlier; verify it's wired to `PayPalInvoicingProvider.HandleWebhookAsync`. Confirm
the webhook signature verification step actually runs (don't just trust the payload).
Add a `/billing/webhooks/paypal/test` admin-only endpoint that synthesizes a `PAYMENT.CAPTURE.COMPLETED`
event so we can integration-test without bouncing through real PayPal sandbox.

**A4. Hide Stripe from the UI (don't delete).** In the Generate-invoice dialog dropdown,
the "Stripe" option should be disabled with title="Stripe integration coming soon — use
PayPal for now". Keep `PaymentProvider.Stripe` enum + StripeProvider class in place;
just hide the UI affordance. Same with the per-subscription default — UI selector should
only offer PayPal until further notice.

**A5. Default `Subscription.PreferredPaymentProvider`.** Already defaults to PayPal in
the model (`= PaymentProvider.PayPal`). New customer creation flows should not require a
provider pick; the value gets set from the operator's PayPal config.

### B. Web installer — .NET self-contained binary (~3-4 hrs)

**Goal:** A single command (`curl https://hostpanel.example.com/installer | sudo bash`)
downloads + extracts + runs a .NET self-contained binary that boots a wizard UI on
http://<server>:8080. Operator clicks through the wizard, watches live progress, ends up
with a fully-running panel on :8443. Replaces the 1129-line bash installer entirely.

**LOCK THESE DECISIONS AT SESSION START via AskUserQuestion before writing code:**

1. **DB choice during install** — MSSQL only (matches current prod), or also offer
   Postgres/MySQL? Recommendation: MSSQL only this pass. Adding Postgres/MySQL means
   replumbing EF provider per-tenant.
2. **Cert during install** — self-signed only (operator runs certbot later), LE auto via
   certbot --nginx (requires public DNS), or operator-chosen toggle? Recommendation:
   toggle, default self-signed (works on private LANs too).
3. **Distribution** — pre-built binary on `mgardner323/hostpanel/releases` (publish via
   GitHub Actions on tag push), or build-from-source during install (operator's box needs
   dotnet SDK + npm at install time, which is the current bash flow's pain point)?
   Recommendation: pre-built binary. Cleaner, faster (~30s vs ~5 min), no SDK requirement.
4. **One-liner UX** — what does the operator paste? `curl … | bash` (classic, scary to
   some), `wget && sudo ./hostpanel-installer` (explicit), or `apt-get install` from an
   apt repo (overkill for one binary)? Recommendation: curl-pipe-bash but with a checksum
   verification step printed before extraction.

**Architecture sketch (refine after Q&A):**

```
new project: src/HostPanel.Installer/
├── HostPanel.Installer.csproj         # PublishSingleFile=true, SelfContained=true, RID=linux-x64
├── Program.cs                          # boots Kestrel on :8080, serves embedded SPA
├── Pages/wizard/                       # Razor pages OR embedded React (likely Razor for size)
│   ├── Welcome.cshtml                  # OS/RAM/disk pre-flight checks
│   ├── Database.cshtml                 # SA password, instance name
│   ├── Admin.cshtml                    # First admin email + password
│   ├── Network.cshtml                  # hostname, port, self-signed vs LE
│   └── Install.cshtml                  # live SSE progress, post-install handoff link
├── Services/
│   ├── PreflightService.cs             # apt-list installed, ufw status, disk free, ram, ports 8443/50051 open
│   ├── InstallOrchestrator.cs          # the actual provisioning pipeline (replaces the bash installer's body)
│   └── SseProgressBroker.cs            # one writer (orchestrator), many readers (browser SSE)
└── wwwroot/                            # any static JS/CSS the wizard needs
```

The `InstallOrchestrator` does everything the current bash installer does (install
mssql-server, .NET runtime, nginx, certbot, create system users, write configs, run EF
migrations, seed admin, start services) but emits progress events instead of `echo`.

**Stages to ship in order so you can validate each:**

1. **Skeleton** — empty Razor project, single-file publish, runs on a fresh VM, serves
   `localhost:8080/welcome` with hardcoded HTML.
2. **Preflight page** — checks Ubuntu 24.04, ≥4GB RAM, ≥40GB disk, ports 8443/50051 free,
   no existing `/opt/hostpanel`. Refuses to proceed if any blocker.
3. **Form pages** — collect SA password, admin email/pwd, hostname, port. Validate
   client-side + server-side. Stash in-memory until Install fires.
4. **Install orchestrator** — port the bash installer's logic into C# `Process.Start` calls.
   Each step pushes a `{step, status, log}` event to the SSE broker. Reuse the existing
   `installer/hostpanel-install.sh` as the line-by-line spec — don't re-derive what to do.
5. **Live progress page** — JS client connects to SSE, renders a checklist with running/done/failed
   status per step and a tailing log pane. After all steps complete + health check passes,
   shows "Open HostPanel →" linking to `https://<chosen-hostname>:8443`.
6. **GitHub Release pipeline** — `.github/workflows/release.yml` builds the installer
   binary on tag push, attaches to release. Bash wrapper script (`installer/web.sh`)
   detects arch, downloads correct binary, runs it.

**Validation plan:**
- Spin up a clean VM (cloud-init Ubuntu 24.04, 4 vCPU 8GB) via Proxmox template clone.
- Run the new one-liner.
- Walk through the wizard in a browser.
- End state must match what the bash installer produces today (same systemd units, same
  paths, same default settings).
- Run the existing Phase B–L regression from `TEST-RESULTS-2026-05-22.md` against the
  newly-installed panel — full pass required.

### C. Wrap up

- Commit each stage as you go (skeleton, preflight, forms, orchestrator, progress, release).
- Push to `mgardner323/hostpanel` after each stage so the operator can preview.
- Update `MEMORY.md` index entry + `project_hostpanel_phase23.md` (or create a new
  `project_hostpanel_f3.md` if F3 deserves its own file).
- Final session report: which decisions got locked, what was deferred, what credentials
  were needed and where they came from.

## Things to ask the operator at session start

1. PayPal sandbox app exists? If yes, where are the credentials? (Otherwise, they create
   one at developer.paypal.com and paste into the new Settings UI.)
2. Confirm the 4 installer-design choices (DB, cert, distribution, one-liner UX) — use
   AskUserQuestion for these BEFORE writing installer code.
3. Should the Stripe option be visibly disabled ("Coming soon") or completely removed
   from the UI for now? (Recommendation: disabled with tooltip; cheaper to re-enable
   later.)

## Skipped / deferred from this session

- **LambdaTest / BrowserStack / Polypane wiring** — Pack offers still unclaimed in the
  HostPanel Playwright config. Pick up after F3 if cross-browser smoke matters.
- **Self-update SSE live log** — current update flow returns final result; live log
  during update is a polish item.

---

## What to give Claude (paste verbatim)

> Pick up where the prior session ended on `mgardner323/hostpanel@2011c1c`. Read
> `C:\Projects\hostpanel\NEXT-SESSION-PROMPT-F3.md` for full context. **Focus is
> PayPal end-to-end (Stripe is future work) and F3 web installer.**
>
> Order:
> (A) Finish PayPal: new Settings UI section for credentials backed by a
>     `PaymentProviderConfig` EF entity (encrypted secret via IDataProtector),
>     refactor `PayPalInvoicingProvider` to read from DB instead of IConfiguration,
>     verify invoice creation produces a real PayPal-hosted link end-to-end on
>     VM 280, hide the Stripe option in the Generate-invoice dropdown ("Coming
>     soon"), and stand up a `/billing/webhooks/paypal/test` endpoint so we can
>     integration-test signature verification without a real PayPal payment.
>
> (B) F3 web installer: BEFORE writing code, use AskUserQuestion to lock the four
>     installer-design decisions (DB choice, cert behavior, distribution,
>     one-liner UX) listed in the handoff doc. Then build the .NET self-contained
>     installer binary in stages: skeleton → preflight → form pages → install
>     orchestrator (port from `installer/hostpanel-install.sh`) → live SSE
>     progress → GitHub Release pipeline. Validate on a fresh Proxmox-cloned
>     Ubuntu 24.04 VM by walking the wizard end-to-end, then running the existing
>     Phase B–L regression against the new install.
>
> Apply the methodology learnings from the prior session, especially: edit
> `/etc/nginx/sites-enabled/hostpanel.conf` directly (it's a real file post-certbot),
> EF migrations land in `Data/Migrations/`, use `git commit -F file` for multi-line
> messages over SSH. Commit each stage as you go and push to GitHub.
>
> Branch HEAD: `2011c1c`. Frontend bundle: `index-DPC_9HI9.js`. Test fixtures
> preserved on VM 280 — do not wipe.
