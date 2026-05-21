import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Checkbox, Dialog, Empty, Field, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface SyncState {
  serverId: number;
  serverHostname: string;
  serverIp: string;
  role: string;
  lastSyncAt?: string;
  autoFailback: boolean;
  consecutiveFailures: number;
  failoverTriggeredAt?: string;
  failbackCompletedAt?: string;
  syncFiles: boolean;
  syncDatabases: boolean;
  syncEmail: boolean;
  failoverStrategy?: number;
  isHealthy: boolean;
  lastHealthCheck?: string;
  // Phase 19c additions
  lastTestedAt?: string;
  lastTestResult?: string;
  lastReplicationLagSeconds?: number;
  lastReplicationCheckAt?: string;
  cloudflareUseLoadBalancer?: boolean;
  cloudflareLoadBalancerId?: string;
}

interface ReplicationLag {
  sqlBackupPath?: string;
  sqlBackupBytes?: number;
  sqlBackupMtime?: string;
  sqlBackupAgeSeconds?: number;
  lsyncdActive?: boolean;
  filesInFlight?: boolean;
  filesBacklogSeconds?: number;
  filesLastSyncMtime?: string;
  watchdogState?: string | null;
  watchdogStaleSeconds?: number;
  lastFailoverLogMtime?: string;
}

interface TestResult {
  ok: boolean;
  standby?: string;
  standbyIp?: string;
  testedAt?: string;
  checks: Array<{ check: string; ok: boolean; error?: string; lagSeconds?: number; thresholdSeconds?: number }>;
  message?: string;
}

const STRATEGY_NAMES: Record<number, string> = { 0: 'DNS Swap', 1: 'Floating IP', 2: 'Cloudflare', 3: 'Per-Domain' };

function roleTone(role: string): 'primary' | 'warn' | 'danger' | 'info' | 'default' {
  if (role === 'primary') return 'primary';
  if (role === 'standby') return 'warn';
  if (role === 'down') return 'danger';
  if (role === 'recovering') return 'info';
  return 'default';
}

function LagTile({ label, valueSeconds, warnThreshold, dangerThreshold, detail }: {
  label: string;
  valueSeconds: number | null | undefined;
  warnThreshold?: number;
  dangerThreshold?: number;
  detail?: string;
}) {
  let display = '—';
  let tone: 'success' | 'warn' | 'danger' | 'default' = 'default';
  if (valueSeconds === null || valueSeconds === undefined) {
    display = '—';
  } else if (valueSeconds < 0) {
    display = 'n/a';
    tone = 'default';
  } else {
    display = valueSeconds < 60 ? `${valueSeconds}s`
      : valueSeconds < 3600 ? `${Math.floor(valueSeconds / 60)}m ${valueSeconds % 60}s`
      : `${Math.floor(valueSeconds / 3600)}h ${Math.floor((valueSeconds % 3600) / 60)}m`;
    if (dangerThreshold !== undefined && valueSeconds >= dangerThreshold) tone = 'danger';
    else if (warnThreshold !== undefined && valueSeconds >= warnThreshold) tone = 'warn';
    else tone = 'success';
  }
  return (
    <div style={{
      padding: 12, borderRadius: 8,
      border: '1px solid var(--border)',
      background: tone === 'danger' ? 'rgba(220,38,38,0.06)' : tone === 'warn' ? 'rgba(234,179,8,0.06)' : 'transparent',
    }}>
      <div className="muted" style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: 0.5 }}>{label}</div>
      <div style={{ fontSize: 22, fontWeight: 600, fontFamily: 'var(--font-mono)' }}>
        <Badge tone={tone}>{display}</Badge>
      </div>
      {detail && <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>{detail}</div>}
    </div>
  );
}

interface ConfigFormState {
  autoFailback: boolean;
  syncFiles: boolean;
  syncDatabases: boolean;
  syncEmail: boolean;
  failoverStrategy: number;
  floatingIp: string;
  cloudflareApiToken: string;
  cloudflareZoneId: string;
}

interface ServerRow { id: number; hostname: string; ipAddress: string; isLocal: boolean; }

export function HaDashboardPage() {
  const [states, setStates] = useState<SyncState[]>([]);
  const [servers, setServers] = useState<ServerRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [configId, setConfigId] = useState<number | null>(null);
  const [bootstrapOpen, setBootstrapOpen] = useState(false);
  const [bootstrap, setBootstrap] = useState({
    standbyServerId: 0,
    syncIntervalMinutes: 15,
    syncDomains: true,
    syncDatabases: true,
    syncEmail: true,
  });
  const [bootstrapBusy, setBootstrapBusy] = useState(false);
  const [bootstrapErr, setBootstrapErr] = useState<string | null>(null);
  const [cfg, setCfg] = useState<ConfigFormState>({
    autoFailback: true, syncFiles: true, syncDatabases: true, syncEmail: true,
    failoverStrategy: 0, floatingIp: '', cloudflareApiToken: '', cloudflareZoneId: '',
  });
  const [lag, setLag] = useState<ReplicationLag | null>(null);
  const [lagErr, setLagErr] = useState<string | null>(null);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<TestResult | null>(null);
  const [lbBusy, setLbBusy] = useState(false);

  const load = async () => {
    setErr(null);
    try {
      const r = await api.get<SyncState[]>('/ha/sync-state').catch(() => api.get<SyncState[]>('/ha/status'));
      setStates(r.data);
      const sr = await api.get<ServerRow[]>('/servers').catch(() => ({ data: [] as ServerRow[] }));
      setServers(sr.data);
    } catch (e) { setErr(e instanceof Error ? e.message : 'Load failed'); }
    finally { setLoading(false); }
  };
  const loadLag = async () => {
    try {
      const r = await api.get<ReplicationLag>('/ha/replication-lag');
      setLag(r.data);
      setLagErr(null);
    } catch (e) {
      setLagErr(e instanceof Error ? e.message : 'Lag probe failed');
    }
  };
  useEffect(() => { void load(); void loadLag(); }, []);
  useEffect(() => {
    const id = setInterval(() => { void loadLag(); }, 15000);
    return () => clearInterval(id);
  }, []);

  const runTest = async (standbyServerId?: number) => {
    setTesting(true);
    setTestResult(null);
    try {
      const r = await api.post<TestResult>('/ha/test', { standbyServerId, maxAcceptableLagSeconds: 300 });
      setTestResult(r.data);
      void load(); // refresh LastTestedAt
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { message?: string } } }).response?.data?.message
        : null;
      setTestResult({ ok: false, checks: [], message: msg ?? (e instanceof Error ? e.message : 'Test failed') });
    } finally {
      setTesting(false);
    }
  };

  const toggleLoadBalancer = async (state: SyncState) => {
    if (state.cloudflareUseLoadBalancer) {
      if (!confirm(`Disable Cloudflare Load Balancer for ${state.serverHostname}? The configured DNS A-records will resume direct routing.`)) return;
      setLbBusy(true);
      try {
        const r = await api.post<{ ok: boolean; errors: string[] }>('/ha/loadbalancer/disable', { standbyServerId: state.serverId });
        if (!r.data.ok) alert('Some teardown steps failed:\n' + r.data.errors.join('\n'));
        void load();
      } catch (e) {
        alert(e instanceof Error ? e.message : 'Disable failed');
      } finally { setLbBusy(false); }
    } else {
      const hostname = prompt('Hostname to load-balance (leave blank for apex):', '') ?? undefined;
      setLbBusy(true);
      try {
        await api.post('/ha/loadbalancer/enable', { standbyServerId: state.serverId, hostname: hostname || null });
        void load();
      } catch (e) {
        const msg = e && typeof e === 'object' && 'response' in e
          ? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data
          : null;
        alert(msg?.message ? `${msg.message}\n\n${msg.detail ?? ''}` : (e instanceof Error ? e.message : 'Enable failed'));
      } finally { setLbBusy(false); }
    }
  };

  const submitBootstrap = async () => {
    if (!bootstrap.standbyServerId) { setBootstrapErr('Pick a standby server'); return; }
    setBootstrapBusy(true);
    setBootstrapErr(null);
    try {
      await api.post('/ha/configure-failover', bootstrap);
      setBootstrapOpen(false);
      void load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { message?: string } } }).response?.data?.message
        : null;
      setBootstrapErr(msg ?? (e instanceof Error ? e.message : 'Configure failed'));
    } finally {
      setBootstrapBusy(false);
    }
  };

  // Servers that are remote + active + don't yet have an HaSyncState row → eligible to bootstrap as standbys
  const eligibleStandbys = servers.filter(s => !s.isLocal && !states.some(st => st.serverId === s.id));

  const failover = async (id: number) => {
    if (!confirm('FAILOVER: Promote this server to primary?')) return;
    await api.post('/ha/failover', { promoteServerId: id }); void load();
  };
  const failback = async (id: number) => {
    if (!confirm('FAILBACK: Sync changes back and restore as primary?')) return;
    await api.post('/ha/failback', { recoveredServerId: id }); void load();
  };
  const promotePermanent = async (id: number) => {
    if (!confirm('PERMANENT PROMOTION: This server will be primary from now on. Continue?')) return;
    const decom = confirm('Also decommission the old primary?\n\nOK = Decommission old primary\nCancel = Keep as standby');
    await api.post('/ha/promote-permanent', { serverId: id, decommissionOldPrimary: decom });
    void load();
  };
  const saveConfig = async () => {
    if (!configId) return;
    await api.put(`/ha/sync-state/${configId}`, cfg);
    setConfigId(null);
    void load();
  };

  return (
    <>
      <PageHead title="High availability" sub={`${states.length} server${states.length === 1 ? '' : 's'} in cluster`}
        actions={
          <>
            <button className="btn" onClick={() => { void load(); void loadLag(); }}><Icons.refresh />Refresh</button>
            <button
              className="btn"
              disabled={states.length === 0 || testing}
              title={states.length === 0 ? 'Configure a standby first' : 'Probe-only test (no DNS or traffic touched)'}
              onClick={() => runTest()}
            >
              <Icons.shield />{testing ? 'Testing…' : 'Test failover'}
            </button>
            <button
              className="btn primary"
              disabled={eligibleStandbys.length === 0}
              title={eligibleStandbys.length === 0 ? 'Add a remote server first (Server fleet)' : 'Configure a new standby'}
              onClick={() => {
                setBootstrap(b => ({ ...b, standbyServerId: eligibleStandbys[0]?.id ?? 0 }));
                setBootstrapErr(null);
                setBootstrapOpen(true);
              }}
            >
              <Icons.plus />Configure failover
            </button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {/* HA vs Backups education panel — explains the distinction */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 16 }}>
        <Card title="High availability">
          <div className="muted" style={{ fontSize: 12, lineHeight: 1.6 }}>
            Protects against <strong>hardware failure</strong>. If the primary server's disk dies or
            the host goes offline, traffic flips to the standby within <code>~60s</code> (DNS swap)
            or <code>~30s</code> (Cloudflare Load Balancer). Customer data lives on both hosts in
            near real time.
          </div>
        </Card>
        <Card title="Backups">
          <div className="muted" style={{ fontSize: 12, lineHeight: 1.6 }}>
            Protects against <strong>logical corruption</strong>: bad migration, accidental delete,
            ransomware. Replication faithfully copies the corruption too — backups are your only
            recovery for those. Keep <strong>both</strong> enabled.
          </div>
        </Card>
      </div>

      {/* Replication-lag card */}
      {lag && (
        <Card title="Replication lag" sub="Refreshes every 15s"
          actions={lagErr ? <span className="badge danger" title={lagErr}>probe failed</span> : null}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 12 }}>
            <LagTile label="SQL backup" valueSeconds={lag.sqlBackupAgeSeconds} warnThreshold={420} dangerThreshold={900}
              detail={lag.sqlBackupMtime ? `Last: ${new Date(lag.sqlBackupMtime).toLocaleTimeString()}` : 'No backup yet'} />
            <LagTile label="File sync (lsyncd)" valueSeconds={lag.lsyncdActive ? (lag.filesInFlight ? lag.filesBacklogSeconds ?? 0 : 0) : -1}
              warnThreshold={30} dangerThreshold={120}
              detail={lag.lsyncdActive
                ? (lag.filesInFlight ? 'syncs in flight' : `caught up${lag.filesLastSyncMtime ? ` — last activity ${new Date(lag.filesLastSyncMtime).toLocaleString()}` : ''}`)
                : 'lsyncd not running'} />
            <LagTile label="Watchdog" valueSeconds={null}
              detail={lag.watchdogState
                ? `${lag.watchdogState}${lag.watchdogStaleSeconds !== undefined ? ` (state ${lag.watchdogStaleSeconds}s old)` : ''}`
                : 'Not running on this node (expected on primary)'} />
          </div>
        </Card>
      )}

      {/* Test result panel — shows the latest probe-only test */}
      {testResult && (
        <Card title={`HA test ${testResult.ok ? 'PASSED' : 'FAILED'}`}
          sub={testResult.standby ? `Probed ${testResult.standby} (${testResult.standbyIp}) at ${testResult.testedAt ? new Date(testResult.testedAt).toLocaleTimeString() : ''}` : testResult.message}
          actions={<Badge tone={testResult.ok ? 'success' : 'danger'} dot>{testResult.ok ? 'ready' : 'not ready'}</Badge>}>
          <div className="col gap-2">
            {testResult.checks.map((c, i) => (
              <div key={i} className="row gap-3" style={{ alignItems: 'center' }}>
                <Badge tone={c.ok ? 'success' : 'danger'} dot>{c.ok ? 'OK' : 'FAIL'}</Badge>
                <span style={{ fontFamily: 'var(--font-mono)', fontSize: 12 }}>{c.check}</span>
                {c.lagSeconds !== undefined && <span className="muted" style={{ fontSize: 12 }}>lag {c.lagSeconds}s / threshold {c.thresholdSeconds}s</span>}
                {c.error && <span className="muted" style={{ fontSize: 12 }}>{c.error}</span>}
              </div>
            ))}
          </div>
        </Card>
      )}

      {loading ? <Card><div className="muted" style={{ padding: 8 }}>Loading…</div></Card>
        : states.length === 0 ? <Card><Empty icon="cloud" title="No HA servers configured" sub="Add servers in Server Management, then configure sync here." /></Card>
        : (
          <div className="col gap-3">
            {states.map(s => (
              <Card key={s.serverId} title={s.serverHostname} sub={s.serverIp}
                actions={
                  <div className="row gap-2">
                    {s.role === 'standby' && (
                      <>
                        <button className="btn" onClick={() => runTest(s.serverId)} disabled={testing} title="Probe-only — no DNS or traffic touched"><Icons.shield />Test</button>
                        <button className="btn" onClick={() => toggleLoadBalancer(s)} disabled={lbBusy}
                          title={s.cloudflareUseLoadBalancer ? 'Tear down the Cloudflare Load Balancer' : 'Front this cluster with a Cloudflare Load Balancer (Premium HA)'}>
                          <Icons.globe />{s.cloudflareUseLoadBalancer ? 'Disable CF LB' : 'Enable CF LB'}
                        </button>
                        <button className="btn" onClick={() => failover(s.serverId)}><Icons.zap />Failover</button>
                        <button className="btn danger" onClick={() => promotePermanent(s.serverId)}><Icons.shield />Make permanent</button>
                      </>
                    )}
                    {s.role === 'down' && <button className="btn primary" onClick={() => failback(s.serverId)}><Icons.refresh />Failback</button>}
                    {s.role !== 'decommissioned' && (
                      <button className="btn" onClick={() => { setConfigId(s.serverId); setCfg({
                        autoFailback: s.autoFailback, syncFiles: s.syncFiles, syncDatabases: s.syncDatabases, syncEmail: s.syncEmail,
                        failoverStrategy: s.failoverStrategy ?? 0, floatingIp: '', cloudflareApiToken: '', cloudflareZoneId: '',
                      }); }}><Icons.settings />Configure</button>
                    )}
                  </div>
                }
              >
                <div className="row gap-2" style={{ marginBottom: 12, flexWrap: 'wrap' }}>
                  <Badge tone={roleTone(s.role)} dot>{s.role}</Badge>
                  <Badge tone={s.isHealthy ? 'success' : 'danger'} dot>{s.isHealthy ? 'Healthy' : 'Down'}</Badge>
                  {s.failoverStrategy !== undefined && <Badge tone="info">{STRATEGY_NAMES[s.failoverStrategy]}</Badge>}
                  {s.autoFailback && <Badge>Auto-failback</Badge>}
                  {s.cloudflareUseLoadBalancer && <Badge tone="info" dot>CF LB</Badge>}
                  {s.consecutiveFailures > 0 && <Badge tone="danger">{s.consecutiveFailures} failures</Badge>}
                  {s.lastTestedAt && (
                    <Badge tone={s.lastTestResult?.includes('"ok":true') ? 'success' : 'warn'} dot>
                      HA tested {new Date(s.lastTestedAt).toLocaleString()}
                    </Badge>
                  )}
                  {s.lastReplicationLagSeconds !== undefined && s.lastReplicationLagSeconds !== null && (
                    <Badge tone={s.lastReplicationLagSeconds > 900 ? 'danger' : s.lastReplicationLagSeconds > 420 ? 'warn' : 'success'}>
                      lag {s.lastReplicationLagSeconds}s
                    </Badge>
                  )}
                </div>
                <div className="muted" style={{ fontSize: 12, fontFamily: 'var(--font-mono)', lineHeight: 1.8 }}>
                  {s.lastSyncAt && <div>Last sync: {new Date(s.lastSyncAt).toLocaleString()}</div>}
                  {s.lastHealthCheck && <div>Health check: {new Date(s.lastHealthCheck).toLocaleString()}</div>}
                  {s.failoverTriggeredAt && <div>Failover triggered: {new Date(s.failoverTriggeredAt).toLocaleString()}</div>}
                  {s.failbackCompletedAt && <div>Failback completed: {new Date(s.failbackCompletedAt).toLocaleString()}</div>}
                  <div>Sync: {[s.syncFiles && 'Files', s.syncDatabases && 'Databases', s.syncEmail && 'Email'].filter(Boolean).join(', ') || 'None'}</div>
                </div>
              </Card>
            ))}
          </div>
        )}

      <Dialog
        open={bootstrapOpen}
        onClose={() => setBootstrapOpen(false)}
        title="Configure failover (one-time setup)"
        sub="Registers a remote server as a failover standby and starts initial sync."
        foot={
          <>
            {bootstrapErr && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{bootstrapErr}</span>}
            <button className="btn ghost" onClick={() => setBootstrapOpen(false)} disabled={bootstrapBusy}>Cancel</button>
            <button className="btn primary" onClick={submitBootstrap} disabled={bootstrapBusy || !bootstrap.standbyServerId}>
              {bootstrapBusy ? 'Configuring…' : 'Configure standby'}
            </button>
          </>
        }
      >
        <div className="col gap-3">
          <Field label="Standby server" hint="Only remote, active servers without an existing HA sync state appear here.">
            <select className="select" value={bootstrap.standbyServerId} onChange={e => setBootstrap(b => ({ ...b, standbyServerId: Number(e.target.value) }))}>
              <option value={0}>— pick one —</option>
              {eligibleStandbys.map(s => <option key={s.id} value={s.id}>{s.hostname} ({s.ipAddress})</option>)}
            </select>
          </Field>
          <Field label="Sync interval (minutes)" hint="How often the standby pulls deltas from the primary.">
            <input className="input mono" type="number" min={1} max={1440} value={bootstrap.syncIntervalMinutes}
              onChange={e => setBootstrap(b => ({ ...b, syncIntervalMinutes: Math.max(1, Number(e.target.value)) }))} />
          </Field>
          <div className="row gap-4" style={{ flexWrap: 'wrap' }}>
            <Checkbox checked={bootstrap.syncDomains} onChange={v => setBootstrap(b => ({ ...b, syncDomains: v }))} label="Domains / vhost files" />
            <Checkbox checked={bootstrap.syncDatabases} onChange={v => setBootstrap(b => ({ ...b, syncDatabases: v }))} label="Databases" />
            <Checkbox checked={bootstrap.syncEmail} onChange={v => setBootstrap(b => ({ ...b, syncEmail: v }))} label="Mailboxes" />
          </div>
        </div>
      </Dialog>

      <Dialog open={configId !== null} onClose={() => setConfigId(null)} title="HA configuration"
        foot={<>
          <button className="btn ghost" onClick={() => setConfigId(null)}>Cancel</button>
          <button className="btn primary" onClick={saveConfig}>Save</button>
        </>}>
        <div className="col gap-3">
          <Field label="Failover strategy">
            <select className="select" value={cfg.failoverStrategy} onChange={e => setCfg(c => ({ ...c, failoverStrategy: Number(e.target.value) }))}>
              <option value={0}>DNS Swap (free, TTL-dependent)</option>
              <option value={1}>Floating IP (instant, same LAN)</option>
              <option value={2}>Cloudflare (free or $5/mo LB)</option>
              <option value={3}>Per-Domain (selective)</option>
            </select>
          </Field>
          {cfg.failoverStrategy === 1 && (
            <Field label="Floating IP"><input className="input mono" value={cfg.floatingIp} onChange={e => setCfg(c => ({ ...c, floatingIp: e.target.value }))} /></Field>
          )}
          {cfg.failoverStrategy === 2 && (
            <>
              <Field label="Cloudflare API token"><input className="input mono" type="password" value={cfg.cloudflareApiToken} onChange={e => setCfg(c => ({ ...c, cloudflareApiToken: e.target.value }))} /></Field>
              <Field label="Cloudflare zone ID"><input className="input mono" value={cfg.cloudflareZoneId} onChange={e => setCfg(c => ({ ...c, cloudflareZoneId: e.target.value }))} /></Field>
            </>
          )}
          <div className="row gap-4" style={{ flexWrap: 'wrap' }}>
            <Checkbox checked={cfg.syncFiles} onChange={v => setCfg(c => ({ ...c, syncFiles: v }))} label="Files" />
            <Checkbox checked={cfg.syncDatabases} onChange={v => setCfg(c => ({ ...c, syncDatabases: v }))} label="Databases" />
            <Checkbox checked={cfg.syncEmail} onChange={v => setCfg(c => ({ ...c, syncEmail: v }))} label="Email" />
          </div>
          <Checkbox checked={cfg.autoFailback} onChange={v => setCfg(c => ({ ...c, autoFailback: v }))} label="Auto-failback when recovered" />
        </div>
      </Dialog>
    </>
  );
}
