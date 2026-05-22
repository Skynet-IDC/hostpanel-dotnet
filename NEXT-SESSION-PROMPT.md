# HostPanel — next-session prompt (paste into new chat)

## Context to give Claude

We did a major install + backend test cycle on 2026-05-21/22. Eight installer
showstoppers were found + fixed (commits `dcbc043`, `ab727a1`, `f475418`,
`52dcb2b`, `e6fb0ff`, `bfdab2c`, `77781d7`, `9765a25` on
`mgardner323/hostpanel@main`). Both VM 280 (primary) and VM 281 (standby)
currently have a clean install from GitHub `main`.

**Goal for this session:** wipe and re-test everything from scratch again on
the latest GitHub `main`, then fix the one remaining architectural bug (HA
agent TCP listener) so HA configure-failover works end-to-end across both VMs.

## Prereqs to know

- Working dir: `C:\Projects\proxmox` (memory dir there too)
- HostPanel source: `C:\Projects\hostpanel\` (local mirror) and
  `C:\Projects\hostpanel-git\` (git clone for pushes)
- GitHub PAT: `C:\Users\mgard\.claude\secrets\github-hostpanel.env`
- Sentry DSNs: `C:\Users\mgard\.claude\secrets\sentry.env`
- Testmail creds: `C:\Users\mgard\.claude\secrets\testmail.env`
  (api key `5051d8fe-463b-49da-b576-2ebe9ce50300`, namespace `96wfr`)
- OPNsense API: `C:\Users\mgard\.claude\secrets\opnsense.env`
- HostPanel test creds: see `reference_hostpanel_credentials.md`

## VMs

| VM | IP | Role | Specs |
|---|---|---|---|
| 280 | 192.168.10.235 (LAN) / 45.25.216.220 (NAT) | Primary | 6GB RAM, 2 cores, 50GB |
| 281 | 192.168.10.236 (LAN) / 45.25.216.221 (NAT) | Standby | 6GB RAM, 2 cores, 50GB |

**For fresh install on either VM**: if it's the standby (VM 281), DISABLE
its 1:1 NAT rule first or outbound dies — UUID
`1f586e70-bfc9-42a3-9fcf-c3ef98109e94`. Re-enable after install.

## Pre-canned install runbook (proven)

```bash
# pve side — wipe + reclone + boot both
ssh root@192.168.10.177 'for VM in 280 281; do qm stop $VM; done
sleep 8
for VM in 280 281; do qm destroy $VM --purge; done
sleep 5
qm clone 900 280 --name hp280 --full --storage hdd-storage
qm clone 900 281 --name hp281 --full --storage hdd-storage
qm set 280 --memory 6144 --cores 2 --ipconfig0 ip=192.168.10.235/24,gw=192.168.10.1 \
       --nameserver 192.168.10.1 --ciuser mgardner --sshkeys /tmp/sshkeys.txt \
       --vga std --onboot 0
qm resize 280 scsi0 +18G && qm start 280
qm set 281 --memory 6144 --cores 2 --ipconfig0 ip=192.168.10.236/24,gw=192.168.10.1 \
       --nameserver 192.168.10.1 --ciuser mgardner --sshkeys /tmp/sshkeys.txt \
       --vga std --onboot 0
qm resize 281 scsi0 +18G && qm start 281'

# laptop side — clear host keys, disable NAT, launch installs in parallel
ssh-keygen -R 192.168.10.235; ssh-keygen -R 192.168.10.236
# disable 281's NAT for install — see OPNsense API call above

# wait for ssh on both, then for each VM run the wrapper from
# C:\Projects\hostpanel\TEST-RESULTS-2026-05-21.md (it's documented there)
```

## The HA TCP listener bug — needs fixing

**Symptom:** `POST /api/v1/ha/configure-failover` returns 504 Gateway Timeout
because it eventually times out trying to reach the standby's agent.

**Root cause** (verified 2026-05-22): The agent listens ONLY on a Unix socket
(`/var/run/hostpanel/agent.sock`). The `RemoteServerAgent` class in
`HostPanel.Infrastructure/Services/RemoteServerAgent.cs` opens a TCP
connection to `${standby.IpAddress}:50051`, but nothing is listening there.
Comment in that file already flags it: `// For production, replace with gRPC + mTLS`.

`ss -tlnp` on VM 281 confirms: no listener on 50051. UFW also has no rule for 50051.

**Fix sketch:**
1. `HostPanel.Agent/Program.cs` — add a TCP listener mode that runs alongside
   the Unix socket listener. Configurable via `Agent:TcpEnabled=true` +
   `Agent:TcpPort=50051` in appsettings. Should auth via the same
   `Agent:Token` value (constant-time compare against the JSON `token` field
   already in `AgentRequest`).
2. The TCP listener should ONLY bind to the LAN IP (not 0.0.0.0) to avoid
   exposing it on the WAN. Or, require mTLS — but that's heavier; the
   `Agent:Token` already provides auth and the LAN is internal-only.
3. Installer (`installer/hostpanel-install.sh`) — write the Tcp config to
   `appsettings.Production.json`, AND add a UFW rule `ufw allow from
   192.168.10.0/24 to any port 50051 proto tcp` (LAN-only). Don't open it to
   Anywhere — that would let anyone with the agent token execute as root.
4. Restart `hostpanel-agent` to pick up the new listener.
5. After fix: `POST /ha/configure-failover` should succeed; `POST /ha/test`
   should report both `standbyAgent: ok` and `standbyNginx: ok`.

**Files to look at:**
- `src/HostPanel.Agent/Program.cs` — where the Unix socket listener is set up
- `src/HostPanel.Agent/Services/UnixSocketServer.cs` (or similar) — mirror this
  pattern for a `TcpAgentServer`
- `src/HostPanel.Infrastructure/Services/RemoteServerAgent.cs:34` — already
  calls `TcpClient.ConnectAsync(ip, port)`, will just work once the listener
  exists
- `installer/hostpanel-install.sh` — `deploy_hostpanel` writes appsettings;
  `configure_firewall` adds UFW rules

## Other bugs discovered but NOT fixed

- **MySQL username 33-char overflow** — FIXED in commit `9765a25` (SHA256
  hash). Re-test in fresh install.
- **HA configure-failover 504** — the architectural one above.
- **Mail outbound delivery** — Postfix SASL handshake works (BUG-008 fixed)
  but messages from a fresh VM don't reach the wider internet (no SPF, no
  reverse DNS, port 25 probably blocked outbound at ATT). Not really a
  HostPanel bug — production deployments would use Cloudflare email routing
  or a real SMTP relay.

## Future work logged
- Task #8: Grafana + PatchMon integration for HostPanel (Prometheus /metrics
  endpoint scraped by LXC 240 Grafana stack; PatchMon agent baked into
  installer so customer servers report into patchmon.webdesign101.us). See
  `MEMORY.md` for full scope.

## Test script targets to re-run (proven working last time)

Phase A: clean install both VMs (~25 min parallel) — exit 0 expected
Phase B/C: auth + bootstrap + server stats — all PASS expected
Phase D: plan + customer + subscription + reconciler + Host-header curl 200
  — full chain PROVEN
Phase E: additional domain, files mkdir/rename
Phase F: mailbox + alias create; `tests/integration/mail-roundtrip.sh` —
  SMTP submit passes, outbound delivery may fail (see note above)
Phase G: PostgreSQL + MSSQL + MySQL DB create (all 3 should pass with
  9765a25 fix)
Phase H: WordPress install (untested in last pass — try it)
Phase I: Plesk migration discovery against Hostinger (key in
  `/etc/hostpanel/plesk-migration-api-key` will be LOST after wipe, regenerate
  via `plesk bin secret_key --create -login admin -description "HostPanel Migration"`
  on Hostinger Plesk server, save to that file on VM 280)
Phase J: register VM 281 as Server → configure-failover → SHOULD work after
  HA TCP listener fix. Then `/ha/test` probe-only, then `/ha/replication-lag`.
Phase K: billing — generate invoice
Phase L: firewall + webhooks + branding

## What to PASTE into the new prompt

> Pick up the HostPanel project. Read `C:\Projects\hostpanel\NEXT-SESSION-PROMPT.md`
> for full context. We're on `mgardner323/hostpanel@main` (latest commit
> `9765a25`). Goal: wipe both VM 280 + VM 281, reinstall cleanly, run the full
> backend test pass again to verify nothing regressed, then fix the HA agent
> TCP listener bug so configure-failover works end-to-end across both VMs.
> All fixes should be pushed to GitHub `main` as commits.
