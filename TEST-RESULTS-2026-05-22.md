# HostPanel Test Pass — 2026-05-22 (HA TCP Listener)

Picked up from `NEXT-SESSION-PROMPT.md` (handoff commit `9eabbee`). Goal:
clean wipe + reinstall both VMs from `mgardner323/hostpanel@main`, run the
full backend regression, then fix the HA agent TCP listener bug so
`configure-failover` works end-to-end across primary + standby.

## Commits shipped this session

| SHA | Subject |
|---|---|
| `bc1f952` | ha: agent TCP listener so primary panel can reach standby agent |
| `f982a86` | ha: installer must write agent appsettings + run agent with DOTNET_ENVIRONMENT=Production |
| `eb2f267` | ha: UFW HA-port CIDR must come from default NIC, not first scope-link route |
| `73cba92` | ha+reconciler: close two state-bridge gaps from 2026-05-22 regression |

All four pushed to GitHub `mgardner323/hostpanel@main` and live on both VMs.

## Bugs found + fixed (this session)

### BUG-012 — Agent TCP listener missing entirely
`RemoteServerAgent.cs` opens TCP to `${standby_ip}:50051`, but the agent only
had a Unix-socket listener. `POST /ha/configure-failover` always timed out.

**Fix (`bc1f952`):** Extracted per-connection request handling from `Worker`
into a shared `AgentSessionProcessor`. Added `TcpListenerWorker` as a sibling
`BackgroundService` that uses the same auth + dispatch path. Binds to a
specific IP (refuses 0.0.0.0); falls back to loopback if no bind address
configured. UFW rule auto-installed restricted to the LAN CIDR.

### BUG-013 — Agent reads its own appsettings, not the API's
Followup to BUG-012: even with the listener code in place, the agent logged
"Agent TCP listener disabled (Agent:Tcp:Enabled=false)" on first install.
Root cause: the agent's `ContentRootPath` is `/opt/hostpanel/agent/`, not
`/opt/hostpanel/`. The installer wrote `Agent:Tcp` config into the API's
`appsettings.Production.json` but the agent never reads that file.

**Fix (`f982a86`):** Installer now writes a separate
`/opt/hostpanel/agent/appsettings.Production.json` AND adds
`Environment=DOTNET_ENVIRONMENT=Production` to the agent systemd unit (the
Worker SDK reads `DOTNET_ENVIRONMENT`, not `ASPNETCORE_ENVIRONMENT`). LAN IP
detection lifted to function scope (`DEPLOY_LAN_IP`) since both writes use it.

### BUG-015 — Subscription stays PendingSetup after reconciler success
The reconciler updates `DesiredState.State = Active` on success but never
touches `Subscription.Status`, so the dashboard misreports state and any
caller polling `/subscriptions/{id}` never sees a transition. Two state
machines drifting apart.

**Fix (`73cba92`):** In `ReconciliationService.ReconcileOneAsync`, after a
successful reconciliation, if `resourceType == "subscription"`, fetch the
Subscription and flip `Status PendingSetup → Active`. Suspended and
Cancelled are operator decisions, so the bridge only touches PendingSetup.

### BUG-016 — configure-failover doesn't create HaSyncState
The agent call to set up replication crons succeeds, but no `HaSyncState`
row is inserted on the primary side. Result: `/ha/test` bails with
"No standby configured", `/ha/sync-state` returns empty, and the
loadbalancer endpoints have nothing to anchor against.

**Fix (`73cba92`):** Upsert one `HaSyncState` row per standby `Server` in
`ConfigureFailover` — `Role = "standby"`, `SyncDirection = "pull"`, sync
channel flags from the request. Response now includes `haSyncStateId` for
the caller's convenience.

### BUG-014 — UFW rule installed for Docker bridge instead of LAN
Detection used `ip -4 route show | awk '$1!="default" && /scope link/'`,
which on hosts with Docker installed picks `172.17.0.0/16` (docker0) before
`192.168.10.0/24` (eth0). Result: UFW allowed nothing from the LAN, so
configure-failover still failed at the firewall even with the listener bound.

**Fix (`eb2f267`):** Resolve the default-route NIC first
(`ip route show default | awk '{print $5}'`), THEN take the scope-link CIDR
attached to ONLY that NIC. `--ha-allow-cidr` still wins for custom layouts.

## Phase A — clean reinstall (both VMs in parallel)

- VM 280: destroy → reclone template 900 → cloud-init (LAN .235 + gw) → start
- VM 281: same with .236 (NAT temporarily disabled via OPNsense API to allow outbound during install)
- Both installs from `mgardner323/hostpanel@main` HEAD via `--git-repo` flag
- Exit 0 on both VMs after ~25 min wall-clock parallel
- All services up: hostpanel-agent, hostpanel-api, mssql-server, nginx
- Unix socket listener present, TCP listener (50051) bound after the BUG-013/014 hot-patches

## Phase B–L regression on VM 280 (post-fixes)

**Final score: PASS=22 / FAIL=0** — clean sweep after the BUG-015 + BUG-016
bridge fixes landed (commit `73cba92`).

Earlier in the session this was PASS=19 / FAIL=3; the 3 failures (D.4, J.4,
J.4b) were the state-machine drift bugs above and were closed by `73cba92`.

| Phase | Check | Result | Notes |
|---|---|---|---|
| B.1 | `/setup/bootstrap-status adminExists=true` | PASS | |
| B.2 | `/auth/login` returns JWT | PASS | |
| B.3 | `/setup/bootstrap-admin` returns 409 (closed) | PASS | |
| C.1 | `/server/stats` returns 200 + has cpuUsage | PASS | BUG-007 kebab routing |
| C.2 | `/panel/version` 200 | PASS | |
| D.1 | `/service-plans` POST | PASS | id=6 |
| D.2 | `/customers` POST | PASS | (Identity password ≥12 chars required) |
| D.3 | `/subscriptions` POST | PASS | id=1 |
| D.4 | reconciler reaches Active | PASS | BUG-015 fix — bridge from DesiredState.Active → Subscription.Status |
| D.5 | nginx Host-header curl 200 | PASS | reconciler proven via the actual vhost |
| G.4 | `/databases/status` mentions MySql | PASS | BUG-007 kebab |
| G.1 | MySQL db create | PASS | BUG-009 sudo + `9765a25` 32-char hash truncation |
| G.2 | PostgreSQL db create | PASS | BUG-009 sudo peer auth |
| G.3 | MSSQL db create | PASS | |
| F.0 | domain POST | PASS | |
| F.1 | mailbox POST | PASS | BUG-008 dovecot SASL handshake |
| J.0 | standby agent.token present (44 chars) | PASS | |
| J.1 | `/servers` POST (register standby) | PASS | id=6 |
| **J.2** | **`/ha/configure-failover` returns 200** | **PASS** | **was 504 pre-fix — the headline result** |
| J.3 | `/ha/replication-lag` returns 2xx | PASS | local agent, always worked |
| J.4 | `/ha/test` returns standbyAgent check | PASS | BUG-016 fix — configure-failover now upserts HaSyncState |
| J.4b | `/ha/test` standbyAgent=true | PASS | all 3 checks ok (standbyAgent + standbyNginx + replicationLag=0s within 300s threshold) |

## Independent wire-level proof of the TCP listener

```
$ ssh mgardner@192.168.10.235 'printf "%s\n" "{\"id\":\"t\",\"token\":\"BAD\",\"method\":\"system.getStats\",\"params\":{}}" | nc -w 3 192.168.10.236 50051'
{"id":"t","traceId":null,"result":null,"error":{"code":-32001,"message":"Unauthorized"},"IsSuccess":false}

$ ssh mgardner@192.168.10.236 "printf '%s\n' '{\"id\":\"t\",\"token\":\"<vm280-token>\",\"method\":\"system.getStats\",\"params\":{}}' | nc -w 3 192.168.10.235 50051"
{"id":"t","traceId":null,"result":{"success":true,"data":{"hostname":"hp280","osVersion":"Unix 6.8.0.100","processorCount":2,"totalMemoryMb":5906,"uptimeSeconds":1961}},"error":null,"IsSuccess":true}
```

VM 281's hostpanel-agent journal also confirms 3× incoming
`Executing ha.configureStandby` lines — one per regression-test run, each
originating from VM 280's `RemoteServerAgent.ExecuteAsync`.

## Future work surfaced

- mTLS for the agent TCP listener (currently bearer token + UFW source-IP
  allowlist; mTLS would defang a leaked token entirely). The TcpListenerWorker
  has a comment block describing the hardening path.
- Grafana + PatchMon integration (still pending, task #8 in MEMORY.md).
