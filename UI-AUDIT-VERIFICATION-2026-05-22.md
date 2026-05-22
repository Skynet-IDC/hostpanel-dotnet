# Phase 19c UI gap closure verification — 2026-05-22

Re-verification of the claim that Phase 19c closed 4 of 4 Tier 1 + 7 of 11
Tier 2 UI gaps from the 2026-05-17 coverage audit. Done by grepping for
each endpoint in `src/hostpanel-ui/src/` and dereferencing template-literal
URLs by reading the consuming page.

## Result: 11 / 11 verified — no drift since 2026-05-17

### Tier 1 (all 4 claimed closed)

| # | Endpoint | Page | Evidence |
|---|---|---|---|
| 1 | `GET/PUT/DELETE /branding/customer/...` + logo upload | AccountPage | `pages/AccountPage.tsx:46,79,97,112` |
| 2 | `GET /subscriptions/{id}/usage` | SubscriptionsPage | `pages/SubscriptionsPage.tsx:81` (template literal — false-negatived by literal grep, hand-verified) + Meter render at lines 180+ |
| 3 | `POST /billing/invoices/generate` | BillingPage | `pages/BillingPage.tsx:51` |
| 4 | `POST /ha/configure-failover` | HaDashboardPage | `pages/HaDashboardPage.tsx:213` |

### Tier 2 (all 7 claimed closed)

| # | Endpoint | Page | Evidence |
|---|---|---|---|
| 5 | `DELETE /aliases/{id}` | EmailPage | `pages/EmailPage.tsx:55` |
| 6 | `DELETE /firewall/rules/{n}` | FirewallPage | `pages/FirewallPage.tsx:42` |
| 7 | `POST /firewall/enable\|disable` | FirewallPage | `pages/FirewallPage.tsx:30` |
| 9 | `GET /server/updates/os-info` + `/history` | ServerUpdatesPage | `pages/ServerUpdatesPage.tsx:97,101` |
| 12 | `POST /domains/{id}/files/mkdir` + `/rename` | FilesPage | `pages/FilesPage.tsx:89,102` |
| 13 | `GET /webhooks/{id}/deliveries` | WebhooksPage | `pages/WebhooksPage.tsx:55` (+ state mgmt 46–48) |
| 15 | `GET /databases/types` + `/status` | DatabasesPage | `pages/DatabasesPage.tsx:40,45` |

## Tier 2 still open (not in Phase 19c scope, carry into Phase 23 backlog)

| # | Endpoint | Page that should consume |
|---|---|---|
| 8 | `GET /panel/version` + `/check-update` + `POST /panel/update` + `/rollback` | New PanelUpdatesPage (or section on ServerUpdatesPage) |
| 10 | `GET /domains/{id}/logs/access\|error\|mail` | New `/domains/{id}/logs` page or DomainDetailPage section |
| 11 | `POST /domains/{id}/app/build` + `/restart` | AppConfigPage / per-domain — buttons next to env-var editor |
| 14 | `GET /webhooks/events` | WebhooksPage Create dialog — replace the current hardcoded event list with a dynamic fetch |

## Tier 3 (5, no UI expected)

Unchanged from 2026-05-17: machine-to-machine endpoints
(`POST /migration/plesk/*` — implementation lives in Phase 20 backend;
`/billing/webhooks/*` — Stripe/PayPal callbacks; `/sso/callback` —
browser-redirect middleware; `/health` — HA watchdog probe;
`/domains/{id}/git/webhook` — git push receiver).

## Bottom line

Phase 19c's UI deliverables are intact. Phase 23 UI testing can proceed
without re-checking the 11 closures listed above. The 4 still-open Tier 2
items are known limitations; opportunistic to close during the UI sweep
or after.
