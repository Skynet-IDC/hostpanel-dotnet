import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Empty, IconBtn, PageHead } from '../components/ui/primitives';
import { panelApi, type PanelCustomer, type PanelPlan } from '../api/panel';

interface MigrationSource {
  id: number;
  name: string;
  host: string;
  pleskApiPort: number;
  sshUser: string;
  sshPort: number;
  dnsProvider: string;
  isActive: boolean;
  createdAt: string;
}

interface MigrationJob {
  id: number;
  migrationSourceId: number;
  sourceDomain: string;
  state: string;
  targetSubscriptionId?: number;
  stagedAt?: string;
  cutoverAt?: string;
  rolledBackAt?: string;
  errorMessage?: string;
  createdAt: string;
}

interface DiscoveryResult {
  status: string;
  sourceHost: string;
  discoveredAt: string;
  domains?: Array<{ id: number; name: string; www_root?: string; base_domain_id?: number }>;
  perDomain?: Array<{
    id: number;
    name: string;
    wwwRoot?: string;
    baseDomainId?: number;
    databases?: Array<{ id: number; name: string; type: string }>;
    mail?: Array<{ address: string }>;
  }>;
  partialErrors?: Array<{ section: string; error: string }>;
  error?: string;
}

type Tab = 'sources' | 'discovery' | 'jobs';

export function MigrationPage() {
  const [tab, setTab] = useState<Tab>('sources');
  const [sources, setSources] = useState<MigrationSource[]>([]);
  const [jobs, setJobs] = useState<MigrationJob[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [showCreateSource, setShowCreateSource] = useState(false);

  const reload = async () => {
    try {
      const [s, j] = await Promise.all([
        api.get<MigrationSource[]>('/migration/sources'),
        api.get<MigrationJob[]>('/migration/jobs'),
      ]);
      setSources(s.data);
      setJobs(j.data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void reload(); }, []);

  return (
    <>
      <PageHead
        title="Plesk migration"
        sub={`${sources.length} source${sources.length === 1 ? '' : 's'} • ${jobs.length} job${jobs.length === 1 ? '' : 's'}`}
        actions={
          <>
            <IconBtn icon="refresh" title="Refresh" onClick={reload} />
            {tab === 'sources' && (
              <button className="btn btn-primary" onClick={() => setShowCreateSource(true)}>
                Add Plesk source
              </button>
            )}
          </>
        }
      />

      <Card>
        <div className="tabs" style={{ marginBottom: 16 }}>
          <button className={`tab ${tab === 'sources' ? 'tab--active' : ''}`} onClick={() => setTab('sources')}>
            Sources ({sources.length})
          </button>
          <button className={`tab ${tab === 'discovery' ? 'tab--active' : ''}`} onClick={() => setTab('discovery')}>
            Discover &amp; preflight
          </button>
          <button className={`tab ${tab === 'jobs' ? 'tab--active' : ''}`} onClick={() => setTab('jobs')}>
            Jobs ({jobs.length})
          </button>
        </div>

        {err && <div className="alert alert--error">{err}</div>}
        {loading && <Empty title="Loading…" />}

        {!loading && tab === 'sources' && (
          <SourcesPanel sources={sources} onChange={reload} />
        )}
        {!loading && tab === 'discovery' && (
          <DiscoveryPanel sources={sources} onJobsCreated={reload} />
        )}
        {!loading && tab === 'jobs' && (
          <JobsPanel jobs={jobs} sources={sources} onChange={reload} />
        )}
      </Card>

      {showCreateSource && (
        <CreateSourceDialog onClose={() => setShowCreateSource(false)} onCreated={() => { setShowCreateSource(false); void reload(); }} />
      )}
    </>
  );
}

function SourcesPanel({ sources, onChange }: { sources: MigrationSource[]; onChange: () => void }) {
  const del = async (id: number) => {
    if (!confirm('Delete this migration source? Existing jobs are preserved.')) return;
    await api.delete(`/migration/sources/${id}`);
    onChange();
  };

  if (sources.length === 0)
    return <Empty title="No Plesk sources registered yet" sub="Add one to start migrating from Plesk to HostPanel." />;

  return (
    <table className="table">
      <thead>
        <tr><th>Name</th><th>Host</th><th>SSH</th><th>DNS</th><th>Status</th><th></th></tr>
      </thead>
      <tbody>
        {sources.map(s => (
          <tr key={s.id}>
            <td>{s.name}</td>
            <td><code>{s.host}:{s.pleskApiPort}</code></td>
            <td><code>{s.sshUser}@{s.host}:{s.sshPort}</code></td>
            <td><Badge>{s.dnsProvider}</Badge></td>
            <td><Badge tone={s.isActive ? 'success' : 'default'} dot>{s.isActive ? 'active' : 'disabled'}</Badge></td>
            <td><IconBtn icon="trash" title="Delete" onClick={() => del(s.id)} /></td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function CreateSourceDialog({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const [name, setName] = useState('Hostinger Plesk');
  const [host, setHost] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [sshKeyPath, setSshKeyPath] = useState('/etc/hostpanel/plesk-migration-key');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    if (!host || !apiKey) { setError('Host and API key are required'); return; }
    setSubmitting(true);
    setError(null);
    try {
      await api.post('/migration/sources', { name, host, pleskApiKey: apiKey, sshKeyPath });
      onCreated();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { error?: string } } }).response?.data?.error
        : null;
      setError(msg ?? (e instanceof Error ? e.message : 'Create failed'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal__header">
          <h3>Add Plesk migration source</h3>
          <IconBtn icon="x" title="Close" onClick={onClose} />
        </div>
        <div className="modal__body">
          <label className="field">
            <span className="field__label">Name</span>
            <input className="input" value={name} onChange={e => setName(e.target.value)} />
          </label>
          <label className="field">
            <span className="field__label">Plesk host (IP or FQDN)</span>
            <input className="input" placeholder="178.16.142.159" value={host} onChange={e => setHost(e.target.value)} />
          </label>
          <label className="field">
            <span className="field__label">Plesk REST API key</span>
            <input className="input" type="password" placeholder="Generate via plesk bin secret_key --create"
              value={apiKey} onChange={e => setApiKey(e.target.value)} />
            <span className="field__hint">SSH to the source as root and run <code>plesk bin secret_key --create -login admin -description "HostPanel Migration"</code>. Paste the UUID returned.</span>
          </label>
          <label className="field">
            <span className="field__label">SSH private key path (on HostPanel)</span>
            <input className="input" value={sshKeyPath} onChange={e => setSshKeyPath(e.target.value)} />
            <span className="field__hint">VM 280's key must be in the source's <code>/root/.ssh/authorized_keys</code>.</span>
          </label>
          {error && <div className="alert alert--error">{error}</div>}
        </div>
        <div className="modal__footer">
          <button className="btn btn-ghost" onClick={onClose}>Cancel</button>
          <button className="btn btn-primary" onClick={submit} disabled={submitting}>
            {submitting ? 'Creating…' : 'Create source'}
          </button>
        </div>
      </div>
    </div>
  );
}

function DiscoveryPanel({ sources, onJobsCreated }: { sources: MigrationSource[]; onJobsCreated: () => void }) {
  const [sourceId, setSourceId] = useState<number | null>(sources[0]?.id ?? null);
  const [discovery, setDiscovery] = useState<DiscoveryResult | null>(null);
  const [preflight, setPreflight] = useState<unknown>(null);
  const [loading, setLoading] = useState(false);
  const [running, setRunning] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());

  // Customer/plan selection for job creation
  const [customers, setCustomers] = useState<PanelCustomer[]>([]);
  const [plans, setPlans] = useState<PanelPlan[]>([]);
  const [targetUserId, setTargetUserId] = useState<string>('');
  const [planId, setPlanId] = useState<number | null>(null);
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    panelApi.listCustomers().then(setCustomers).catch(() => {});
    panelApi.listPlans().then(setPlans).catch(() => {});
  }, []);

  useEffect(() => {
    if (sources.length > 0 && sourceId === null) setSourceId(sources[0].id);
  }, [sources, sourceId]);

  const runDiscover = async () => {
    if (!sourceId) return;
    setRunning('discover');
    setLoading(true);
    try {
      const { data } = await api.post<DiscoveryResult>(`/migration/sources/${sourceId}/discover`, {});
      setDiscovery(data);
    } finally {
      setRunning(null);
      setLoading(false);
    }
  };

  const runPreflight = async () => {
    if (!sourceId) return;
    setRunning('preflight');
    try {
      const body = selected.size > 0 ? { domains: [...selected] } : {};
      const { data } = await api.post(`/migration/sources/${sourceId}/preflight`, body);
      setPreflight(data);
    } finally {
      setRunning(null);
    }
  };

  const createJobs = async () => {
    if (!sourceId || !targetUserId || !planId || selected.size === 0) {
      alert('Select customer, plan, and at least one domain');
      return;
    }
    setCreating(true);
    try {
      await api.post('/migration/jobs', {
        migrationSourceId: sourceId,
        domains: [...selected],
        targetUserId,
        servicePlanId: planId,
      });
      alert(`Created ${selected.size} job(s) — switch to the Jobs tab to stage them.`);
      setSelected(new Set());
      onJobsCreated();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { error?: string } } }).response?.data?.error
        : null;
      alert(msg ?? 'Create failed');
    } finally {
      setCreating(false);
    }
  };

  if (sources.length === 0)
    return <Empty title="No sources to discover" sub="Add a Plesk source first." />;

  return (
    <div>
      <div className="row" style={{ gap: 12, marginBottom: 16, alignItems: 'flex-end' }}>
        <label className="field" style={{ minWidth: 240 }}>
          <span className="field__label">Source</span>
          <select className="select" value={sourceId ?? ''} onChange={e => setSourceId(Number(e.target.value))}>
            {sources.map(s => <option key={s.id} value={s.id}>{s.name} ({s.host})</option>)}
          </select>
        </label>
        <button className="btn btn-primary" disabled={!!running || loading} onClick={runDiscover}>
          {running === 'discover' ? 'Discovering…' : 'Run discovery'}
        </button>
        {discovery && (
          <button className="btn" disabled={!!running} onClick={runPreflight}>
            {running === 'preflight' ? 'Checking…' : 'Run preflight'}
          </button>
        )}
      </div>

      {discovery && (
        <>
          <div className="row" style={{ gap: 16, marginBottom: 12 }}>
            <Badge>{discovery.status}</Badge>
            <span style={{ opacity: 0.7 }}>{discovery.perDomain?.length ?? 0} domains</span>
            <span style={{ opacity: 0.7 }}>discovered {new Date(discovery.discoveredAt).toLocaleTimeString()}</span>
          </div>

          {discovery.partialErrors && discovery.partialErrors.length > 0 && (
            <div className="alert alert--warning" style={{ marginBottom: 12 }}>
              <strong>Partial discovery:</strong>
              <ul style={{ margin: '4px 0 0 16px' }}>
                {discovery.partialErrors.map((e, i) => (
                  <li key={i}><code>{e.section}</code>: {e.error}</li>
                ))}
              </ul>
            </div>
          )}

          <table className="table">
            <thead>
              <tr>
                <th><input type="checkbox" checked={selected.size === discovery.perDomain?.length}
                  onChange={e => setSelected(e.target.checked ? new Set(discovery.perDomain?.map(d => d.name)) : new Set())} /></th>
                <th>Domain</th>
                <th>Type</th>
                <th>DBs</th>
                <th>Mailboxes</th>
                <th>Docroot</th>
              </tr>
            </thead>
            <tbody>
              {discovery.perDomain?.map(d => (
                <tr key={d.id}>
                  <td>
                    <input type="checkbox" checked={selected.has(d.name)}
                      onChange={e => {
                        const next = new Set(selected);
                        if (e.target.checked) next.add(d.name); else next.delete(d.name);
                        setSelected(next);
                      }} />
                  </td>
                  <td><strong>{d.name}</strong></td>
                  <td>{d.baseDomainId ? <Badge>subdomain</Badge> : <Badge>apex</Badge>}</td>
                  <td>{d.databases?.length ?? 0}</td>
                  <td>{d.mail?.length ?? 0}</td>
                  <td><code style={{ fontSize: 12 }}>{d.wwwRoot}</code></td>
                </tr>
              ))}
            </tbody>
          </table>

          <div className="row" style={{ gap: 12, marginTop: 16, alignItems: 'flex-end', padding: 12, background: 'var(--color-bg-subtle)', borderRadius: 8 }}>
            <label className="field" style={{ minWidth: 220 }}>
              <span className="field__label">Target customer</span>
              <select className="select" value={targetUserId} onChange={e => setTargetUserId(e.target.value)}>
                <option value="">— select —</option>
                {customers.map(c => <option key={c.id} value={c.id}>{c.email}</option>)}
              </select>
            </label>
            <label className="field" style={{ minWidth: 220 }}>
              <span className="field__label">Service plan</span>
              <select className="select" value={planId ?? ''} onChange={e => setPlanId(Number(e.target.value))}>
                <option value="">— select —</option>
                {plans.map(p => <option key={p.id} value={p.id}>{p.name} (${p.pricePerPeriod})</option>)}
              </select>
            </label>
            <button className="btn btn-primary" disabled={creating || selected.size === 0 || !targetUserId || !planId} onClick={createJobs}>
              {creating ? 'Creating…' : `Create ${selected.size} job${selected.size === 1 ? '' : 's'}`}
            </button>
          </div>
        </>
      )}

      {preflight !== null && (
        <details style={{ marginTop: 16 }}>
          <summary><strong>Preflight report</strong></summary>
          <pre style={{ maxHeight: 400, overflow: 'auto', background: 'var(--color-bg-subtle)', padding: 12, borderRadius: 6, fontSize: 11 }}>
            {JSON.stringify(preflight, null, 2)}
          </pre>
        </details>
      )}
    </div>
  );
}

function JobsPanel({ jobs, sources: _sources, onChange }: { jobs: MigrationJob[]; sources: MigrationSource[]; onChange: () => void }) {
  const [selected, setSelected] = useState<MigrationJob | null>(null);

  const stage = async (id: number) => {
    if (!confirm('Start staging this domain? Files + databases will be copied to HostPanel. Plesk stays live.')) return;
    await api.post(`/migration/jobs/${id}/stage`, {});
    alert('Staging started in the background. Refresh in 30–60s.');
    onChange();
  };

  const cutover = async (id: number, domain: string) => {
    if (!confirm(`CUTOVER ${domain}: this flips DNS at Cloudflare to HostPanel and validates. Proceed?`)) return;
    try {
      const { data } = await api.post(`/migration/jobs/${id}/cutover`, {});
      alert(`Cutover complete. State: ${(data as { state?: string }).state}`);
      onChange();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { error?: string } } }).response?.data?.error
        : null;
      alert(msg ?? 'Cutover failed');
    }
  };

  const rollback = async (id: number, domain: string) => {
    const ip = prompt(`Roll back ${domain} — enter source Plesk IP to point DNS back at:`);
    if (!ip) return;
    await api.post(`/migration/jobs/${id}/rollback`, { sourceIp: ip });
    alert('DNS pointed back at source IP');
    onChange();
  };

  const toneForState = (s: string): 'success' | 'danger' | 'warn' | 'default' => {
    if (s === 'cutover' || s === 'staged') return 'success';
    if (s.includes('failed') || s === 'rolled-back') return 'danger';
    if (s === 'staging' || s === 'cutover-pending') return 'warn';
    return 'default';
  };

  if (jobs.length === 0)
    return <Empty title="No migration jobs yet" sub="Run discovery, select domains, and create jobs." />;

  return (
    <>
      <table className="table">
        <thead>
          <tr><th>Domain</th><th>State</th><th>Target sub</th><th>Staged</th><th>Cutover</th><th>Actions</th></tr>
        </thead>
        <tbody>
          {jobs.map(j => (
            <tr key={j.id}>
              <td><strong>{j.sourceDomain}</strong></td>
              <td><Badge tone={toneForState(j.state)} dot>{j.state}</Badge></td>
              <td>{j.targetSubscriptionId ?? '—'}</td>
              <td>{j.stagedAt ? new Date(j.stagedAt).toLocaleString() : '—'}</td>
              <td>{j.cutoverAt ? new Date(j.cutoverAt).toLocaleString() : '—'}</td>
              <td className="row" style={{ gap: 6 }}>
                <IconBtn icon="info" title="Details" onClick={() => setSelected(j)} />
                {(j.state === 'preflight-pass' || j.state === 'failed') && (
                  <button className="btn btn-small" onClick={() => stage(j.id)}>Stage</button>
                )}
                {j.state === 'staged' && (
                  <button className="btn btn-small btn-primary" onClick={() => cutover(j.id, j.sourceDomain)}>Cutover</button>
                )}
                {(j.state === 'cutover' || j.state === 'cutover-failed') && (
                  <button className="btn btn-small btn-danger" onClick={() => rollback(j.id, j.sourceDomain)}>Rollback</button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {selected && <JobDetail jobId={selected.id} onClose={() => setSelected(null)} />}
    </>
  );
}

function JobDetail({ jobId, onClose }: { jobId: number; onClose: () => void }) {
  const [job, setJob] = useState<MigrationJob & { logTail?: string; discovery?: unknown; preflight?: unknown } | null>(null);
  useEffect(() => {
    const refresh = () => api.get(`/migration/jobs/${jobId}`).then(r => setJob(r.data)).catch(() => {});
    refresh();
    const id = setInterval(refresh, 4000);
    return () => clearInterval(id);
  }, [jobId]);

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal modal--wide" onClick={e => e.stopPropagation()}>
        <div className="modal__header">
          <h3>Job #{jobId} — {job?.sourceDomain}</h3>
          <IconBtn icon="x" title="Close" onClick={onClose} />
        </div>
        <div className="modal__body">
          {!job && <Empty title="Loading…" />}
          {job && (
            <>
              <div className="row" style={{ gap: 16, marginBottom: 12 }}>
                <Badge>{job.state}</Badge>
                <span>Created: {new Date(job.createdAt).toLocaleString()}</span>
                {job.stagedAt && <span>Staged: {new Date(job.stagedAt).toLocaleString()}</span>}
                {job.cutoverAt && <span>Cutover: {new Date(job.cutoverAt).toLocaleString()}</span>}
              </div>
              {job.errorMessage && <div className="alert alert--error">{job.errorMessage}</div>}
              <h4>Migration log (live, polls every 4s)</h4>
              <pre style={{ maxHeight: 380, overflow: 'auto', background: 'var(--color-bg-subtle)', padding: 12, borderRadius: 6, fontSize: 11, whiteSpace: 'pre-wrap' }}>
                {job.logTail || '(no log yet)'}
              </pre>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
