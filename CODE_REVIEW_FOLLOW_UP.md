# Follow-up audit — third pass (current state)

Static re-review of the previously flagged areas. **Most follow-up items are now addressed.**

---

## Verified fixed (this pass)

| Earlier gap | Current state |
|-------------|----------------|
| **Backup restore IDOR** | `BackupsController.Restore` loads `backup`, then `CanAccessSubscriptionAsync(User, backup.SubscriptionId)` before restore. |
| **PayPal webhook headers** | `BillingController.PayPalWebhook` builds a dict of `PAYPAL-AUTH-ALGO`, `PAYPAL-CERT-URL`, `PAYPAL-TRANSMISSION-ID`, `PAYPAL-TRANSMISSION-TIME`; `PayPalInvoicingProvider.HandleWebhookAsync(..., headers)` passes them into `verify-webhook-signature`. |
| **DNS record update/delete tenancy** | `UpdateRecord` / `DeleteRecord` call `CanAccessDomainAsync(User, record.DnsZone.DomainId)` after loading the record. |
| **Cron update/delete tenancy** | `Update` / `Delete` start with `CanAccessSubscriptionAsync(User, subscriptionId)`. |
| **Domains create tenancy** | `Create` calls `CanAccessSubscriptionAsync(User, request.SubscriptionId)` first. |
| **Databases create tenancy** | `Create` calls `CanAccessSubscriptionAsync(User, request.SubscriptionId)` first. |
| **Subscriptions create / reseller target** | `Create` checks reseller: `user.ParentUserId != currentUserId` → `Forbid()`. |
| **Firewall list rules** | `ListRules` calls `AgentMethods.FirewallStatus`; `FirewallStatusHandler` runs `ufw status numbered`. |
| **Disk usage / metering** | `SystemStatsHandler` runs `du -sb /var/www/{username}` when `username` is supplied and sets `diskUsageBytes`. |
| **Agent allowlist drift (dns/mail)** | `Dns*` / `Mail*` removed from `AgentMethods.AllMethods`; comment documents API-level DNS/mail. |
| **OIDC + JWT on one builder** | `Program.cs` uses `var authBuilder = builder.Services.AddAuthentication(...)` then `authBuilder.AddOpenIdConnect(...)` (no second `AddAuthentication()`). |
| **`IPaymentProvider` webhook contract** | Interface includes `HandleWebhookAsync(..., IDictionary<string, string>? headers = null)`; Stripe implements the same shape. |

Previously verified items (Stripe HMAC, SSO fragment/sanitize, tenant service on other routes, backup/cron/firewall agent handlers, API key scheme, billing `PreferredPaymentProvider`, etc.) remain in good shape unless regressed elsewhere.

---

## Residual notes (low severity / polish)

1. **`FirewallStatusHandler`** uses the string literal `"firewall.status"` instead of `AgentMethods.FirewallStatus` — behavior matches; consider using the constant for refactors.

2. **PayPal header availability** — Verification depends on PayPal sending the expected headers. Confirm in staging that header names match what the controller maps (Kestrel preserves PayPal’s typical `PAYPAL-TRANSMISSION-ID` style).

3. **JWT + ApiKey default policy** — Still worth a **manual smoke test**: `Authorization: Bearer <jwt>` vs `Bearer hp_<apikey>` on a few `[Authorize]` endpoints.

4. **`du` disk usage** — Best-effort (`du` under `/var/www/{username}`); failures are swallowed in `SystemStatsHandler` — acceptable, but operators may want logging on parse failure.

5. **Subscriptions `Create` / reseller self-service** — Resellers may only create subscriptions for users with `ParentUserId == currentUserId`; a reseller assigning a subscription to **themselves** (`UserId` = reseller id) will fail if their own `ParentUserId` is null. Usually intended; document if resellers should ever self-subscribe.

---

## Summary

| Tier | Status |
|------|--------|
| Critical / high tenant + payment items from the **second** follow-up | **Resolved** in code as of this third pass |
| Product / ops polish | See residual notes above |

*Third pass: static review only, no runtime webhook or auth tests.*


---

# Installer Test — 2026-05-07 (Proxmox VM 280, Ubuntu 24.04 LTS)

End-to-end test of `installer/hostpanel-install.sh` on a fresh Proxmox VM (cloned from the Ubuntu 24.04 cloud-init template). Three blocking bugs found.

## Test environment
- Proxmox VE 9.1, VMID 280, 4 GB RAM / 2 vCPU / 50 GB disk
- Ubuntu 24.04.4 LTS (cloud-init), dual NIC (LAN + ATT public 45.25.216.219)
- Ran with: `--skip-mssql --admin-email mgardner323@gmail.com --admin-password REDACTED-PASSWORD`

## Outcome
After working around all three bugs, the platform installed cleanly. Verified active: nginx, php8.1/8.2/8.3-fpm, mysql 8.0.45, postgresql 16.13, postfix 3.8.6, dovecot 2.3.21, rspamd, redis, pdns 4.8.3. .NET 10.0.7, Node 22.22.2 installed. UFW enabled with the expected ports. Self-signed cert + JWT + agent token generated. Public IP routed inbound to :8443 (HTTP 500 on `/` because no `wwwroot` deployed — expected). Test VM was destroyed after the smoke test.

## Blocking bugs

### 1. SQL Server 2022 repo URL is hardcoded to current release codename
**Location:** `installer/hostpanel-install.sh` `install_mssql()`
**Symptom:** On Ubuntu 24.04 the script writes a 404 HTML page as an apt source, the next `apt-get update` fails, and `set -euo pipefail` aborts the entire install.
**Root cause:** `curl -s https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/mssql-server-2022.list` — Microsoft has not published an `mssql-server-2022.list` for Ubuntu 24.04 (only 20.04 and 22.04 exist as of 2026-05-07).
**Fix:** Pin the mssql repo to `22.04` regardless of host release, or detect-and-fallback. The `packages-microsoft-prod.deb` for .NET *does* exist for 24.04, so leave that lookup using `lsb_release -rs`.

```bash
# Replace the curl line in install_mssql with:
MSSQL_RELEASE="22.04"
curl -s https://packages.microsoft.com/config/ubuntu/${MSSQL_RELEASE}/mssql-server-2022.list \
    > /etc/apt/sources.list.d/mssql-server.list
```

### 2. `install_webmail` hangs the installer on Roundcube postinst
**Location:** `installer/hostpanel-install.sh` `install_webmail()`
**Symptom:** Installer hangs indefinitely (no progress for 17+ minutes in our test). The wrapping `|| log_warn "Roundcube installation failed - can be installed manually"` never fires because apt-get never returns.
**Root cause:** `apt-get install -y -qq roundcube roundcube-plugins` runs without `DEBIAN_FRONTEND=noninteractive` and without preseeding `roundcube/dbconfig-install`. The roundcube-core postinst falls back to the Readline frontend and prompts for a database admin password on stdin. With no controlling tty (we ran via SSH + tee), it loops forever on "Password mismatch".
**Fix:** Set `DEBIAN_FRONTEND=noninteractive` globally, and preseed roundcube to skip dbconfig:

```bash
# Add at the top of the script, just under `set -euo pipefail`:
export DEBIAN_FRONTEND=noninteractive
export NEEDRESTART_MODE=a

# In install_webmail, before the apt-get line:
debconf-set-selections <<< "roundcube-core roundcube/dbconfig-install boolean false"
debconf-set-selections <<< "roundcube-core roundcube/database-type select sqlite3"
```

### 3. PowerDNS crashloops on port 53 conflict with systemd-resolved
**Location:** `installer/hostpanel-install.sh` `install_powerdns()`
**Symptom:** `systemctl is-active pdns` returns `activating` indefinitely. Journal shows: `Unable to bind UDP socket to '0.0.0.0:53': Address already in use`. Restart counter reached 514 by the end of our install.
**Root cause:** `systemd-resolved` (default on Ubuntu 24.04 cloud images) holds `127.0.0.53:53`. PowerDNS's default `local-address=0.0.0.0` collides with the loopback bind. The installer never writes a `local-address` config.
**Fix:** Detect the primary IPs and write a config snippet, or disable the resolved stub:

```bash
# In install_powerdns, after apt-get install, add:
PRIMARY_IP=$(ip -4 -br addr show | awk '/^e/{split($3,a,"/");print a[1];exit}')
mkdir -p /etc/powerdns/pdns.d
echo "local-address=${PRIMARY_IP}" > /etc/powerdns/pdns.d/local-address.conf
systemctl restart pdns
```

## Other notes
- `deploy_hostpanel` writes `/etc/nginx/sites-enabled/hostpanel.conf` but does not run `nginx -t && systemctl reload nginx`. Operator must reload manually before the panel responds. Recommend appending the reload to the function.
- `install_mssql` uses the deprecated `apt-key add -`. Works on 24.04 (with warnings) but breaks the SecureApt model. Recommend switching to `gpg --dearmor > /etc/apt/keyrings/microsoft.gpg` + `signed-by=` in the source list. Same applies to `install_mail` for the rspamd key.
- Pre-flight check uses `df -m / | awk 'NR==2{print $4}'` and warns at <15 GB. On a fresh cloud-init template before disk resize, the rootfs is small — consider warning earlier in the run.

*Test driver: Claude Code session, 2026-05-07.*


### 5. `gpg --dearmor` fails when no controlling tty is available
**Location:** `installer/hostpanel-install.sh` `install_mssql()` line ~158 and `install_mail()` line ~215.
**Symptom:** Script aborts immediately at "Installing SQL Server..." with `gpg: cannot open '/dev/tty': No such device or address`. With `set -euo pipefail` this is fatal. Found running the installer detached (`setsid` + stdin from `/dev/null` + redirected stdout/stderr) so it survives SSH disconnects — the same conditions any non-interactive runner (CI/CD pipeline, n8n executor, ansible run, cron) would have.
**Root cause:** Modern `gnupg` opens `/dev/tty` for warnings/status messages even on `--dearmor`. Without a controlling terminal it fails out.
**Fix:** Add `--batch --no-tty` to both invocations:

```bash
# install_mssql:
curl -s https://packages.microsoft.com/keys/microsoft.asc \
    | gpg --batch --no-tty --dearmor -o /usr/share/keyrings/microsoft.gpg

# install_mail (rspamd):
wget -qO- https://rspamd.com/apt-stable/gpg.key \
    | gpg --batch --no-tty --dearmor -o /usr/share/keyrings/rspamd.gpg
```

Recommend grepping for any other `gpg` invocation added later (e.g., for nodesource) and applying the same flags as a project-wide convention.
