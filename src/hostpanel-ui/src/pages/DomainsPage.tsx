import { useEffect, useState } from 'react';
import type { DomainDto } from '../types/domain';
import { deleteDomain, getDomains } from '../api/domains';
import api from '../api/client';
import { Badge, Card, Dialog, Empty, Field, IconBtn, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';
import { panelApi, type PanelSubscription } from '../api/panel';

export function DomainsPage() {
  const [domains, setDomains] = useState<DomainDto[]>([]);
  const [subs, setSubs] = useState<PanelSubscription[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [manageDomain, setManageDomain] = useState<DomainDto | null>(null);

  const load = async () => {
    setLoading(true);
    setErr(null);
    try {
      const [d, s] = await Promise.all([getDomains(), panelApi.listSubscriptions()]);
      setDomains(d);
      setSubs(s);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const handleDelete = async (id: number, name: string) => {
    if (!confirm(`Delete domain ${name}? This removes the nginx vhost.`)) return;
    try {
      await deleteDomain(id);
      void load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Delete failed');
    }
  };

  return (
    <>
      <PageHead
        title="Domains"
        sub={`${domains.length} hosted ${domains.length === 1 ? 'domain' : 'domains'}`}
        actions={
          <button className="btn primary" onClick={() => setCreateOpen(true)} disabled={subs.length === 0}>
            <Icons.plus />Add domain
          </button>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card flush>
        {loading ? (
          <div className="muted" style={{ padding: 16 }}>Loading…</div>
        ) : domains.length === 0 ? (
          <Empty
            icon="globe"
            title="No domains yet"
            sub={subs.length === 0
              ? 'You need a subscription before you can add a domain. Create one in Subscriptions.'
              : 'Add the first domain — nginx vhost + docroot + PowerDNS zone get provisioned automatically.'}
            action={subs.length > 0 ? (
              <button className="btn primary" onClick={() => setCreateOpen(true)} style={{ marginTop: 4 }}>
                <Icons.plus />Add domain
              </button>
            ) : undefined}
          />
        ) : (
          <table className="tbl">
            <thead>
              <tr>
                <th>Domain</th>
                <th>Doc root</th>
                <th>PHP</th>
                <th>SSL</th>
                <th style={{ textAlign: 'right' }} />
              </tr>
            </thead>
            <tbody>
              {domains.map(d => (
                <tr key={d.id}>
                  <td>
                    <div className="row gap-2">
                      <span className="mono" style={{ fontWeight: 600, fontSize: 12 }}>{d.name}</span>
                      {d.isMainDomain && <Badge tone="primary">Primary</Badge>}
                    </div>
                  </td>
                  <td className="mono muted" style={{ fontSize: 11 }}>{d.documentRoot}</td>
                  <td>{d.phpVersion ?? '—'}</td>
                  <td>{d.sslEnabled ? <Badge tone="success" dot>SSL</Badge> : <Badge>HTTP only</Badge>}</td>
                  <td>
                    <div className="row-actions">
                      <IconBtn icon="external" title="Open" onClick={() => window.open(`https://${d.name}`, '_blank')} />
                      <IconBtn icon="settings" title="Manage" onClick={() => setManageDomain(d)} />
                      {!d.isMainDomain && <IconBtn icon="trash" danger title="Delete" onClick={() => handleDelete(d.id, d.name)} />}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <AddDomainDialog open={createOpen} onClose={() => setCreateOpen(false)} subs={subs} onSaved={() => { setCreateOpen(false); void load(); }} />
      {manageDomain && (
        <ManageDomainDialog domain={manageDomain} onClose={() => setManageDomain(null)} />
      )}
    </>
  );
}

type ManageTab = 'hotlink' | 'ipdeny' | 'pwprotect' | 'app' | 'php';

function ManageDomainDialog({ domain, onClose }: { domain: DomainDto; onClose: () => void }) {
  const [tab, setTab] = useState<ManageTab>('hotlink');
  return (
    <Dialog
      open={true}
      onClose={onClose}
      title={`Manage ${domain.name}`}
      sub="Per-domain protections and runtime app config"
      foot={<button className="btn" onClick={onClose}>Close</button>}
    >
      <div className="tabs" style={{ marginBottom: 14 }}>
        <button className={`tab ${tab === 'hotlink' ? 'tab--active' : ''}`} onClick={() => setTab('hotlink')}>Hotlink protection</button>
        <button className={`tab ${tab === 'ipdeny' ? 'tab--active' : ''}`} onClick={() => setTab('ipdeny')}>IP deny</button>
        <button className={`tab ${tab === 'pwprotect' ? 'tab--active' : ''}`} onClick={() => setTab('pwprotect')}>Password protect</button>
        <button className={`tab ${tab === 'app' ? 'tab--active' : ''}`} onClick={() => setTab('app')}>App config</button>
        <button className={`tab ${tab === 'php' ? 'tab--active' : ''}`} onClick={() => setTab('php')}>PHP</button>
      </div>
      {tab === 'hotlink' && <HotlinkPane domainId={domain.id} />}
      {tab === 'ipdeny' && <IpDenyPane domainId={domain.id} />}
      {tab === 'pwprotect' && <PwProtectPane domainId={domain.id} />}
      {tab === 'app' && <AppPane domainId={domain.id} />}
      {tab === 'php' && <PhpPane domainId={domain.id} />}
    </Dialog>
  );
}

function HotlinkPane({ domainId }: { domainId: number }) {
  const [enabled, setEnabled] = useState(false);
  const [allowed, setAllowed] = useState('');
  const [exts, setExts] = useState('jpg,jpeg,png,gif,bmp,svg,webp');
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const save = async () => {
    setBusy(true); setMsg(null);
    try {
      await api.put(`/domains/${domainId}/settings/hotlink-protection`, { enabled, allowedDomains: allowed || null, fileExtensions: exts });
      setMsg('Saved.');
    } catch (e) { setMsg(e instanceof Error ? e.message : 'Failed'); } finally { setBusy(false); }
  };
  return (
    <div className="col gap-3">
      <Field label="Enabled"><input type="checkbox" checked={enabled} onChange={e => setEnabled(e.target.checked)} /></Field>
      <Field label="Allowed referrer domains (comma-separated)" hint="Leave blank to only allow same-origin.">
        <input className="input mono" value={allowed} onChange={e => setAllowed(e.target.value)} placeholder="example.com,partner.com" />
      </Field>
      <Field label="Protected file extensions">
        <input className="input mono" value={exts} onChange={e => setExts(e.target.value)} />
      </Field>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <button className="btn primary" onClick={save} disabled={busy}>{busy ? 'Saving…' : 'Save'}</button>
        {msg && <span className="muted" style={{ fontSize: 12 }}>{msg}</span>}
      </div>
    </div>
  );
}

function IpDenyPane({ domainId }: { domainId: number }) {
  const [denied, setDenied] = useState('');
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const save = async () => {
    setBusy(true); setMsg(null);
    try {
      await api.put(`/domains/${domainId}/settings/ip-deny`, { deniedIps: denied });
      setMsg('Saved.');
    } catch (e) { setMsg(e instanceof Error ? e.message : 'Failed'); } finally { setBusy(false); }
  };
  return (
    <div className="col gap-3">
      <Field label="Blocked IPs / CIDRs (one per line)">
        <textarea className="input mono" rows={6} value={denied} onChange={e => setDenied(e.target.value)} placeholder="203.0.113.0/24&#10;198.51.100.42" />
      </Field>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <button className="btn primary" onClick={save} disabled={busy}>{busy ? 'Saving…' : 'Save'}</button>
        {msg && <span className="muted" style={{ fontSize: 12 }}>{msg}</span>}
      </div>
    </div>
  );
}

function PwProtectPane({ domainId }: { domainId: number }) {
  const [directory, setDirectory] = useState('/admin');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [enabled, setEnabled] = useState(true);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const save = async () => {
    setBusy(true); setMsg(null);
    try {
      await api.put(`/domains/${domainId}/settings/password-protect`, { directory, username, password, enabled });
      setMsg('Saved. The directory now requires basic auth.');
    } catch (e) { setMsg(e instanceof Error ? e.message : 'Failed'); } finally { setBusy(false); }
  };
  return (
    <div className="col gap-3">
      <Field label="Directory (relative to docroot)"><input className="input mono" value={directory} onChange={e => setDirectory(e.target.value)} /></Field>
      <Field label="Username"><input className="input mono" value={username} onChange={e => setUsername(e.target.value)} /></Field>
      <Field label="Password"><input className="input" type="password" value={password} onChange={e => setPassword(e.target.value)} /></Field>
      <Field label="Enabled"><input type="checkbox" checked={enabled} onChange={e => setEnabled(e.target.checked)} /></Field>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <button className="btn primary" onClick={save} disabled={busy || !username || !password}>{busy ? 'Saving…' : 'Save'}</button>
        {msg && <span className="muted" style={{ fontSize: 12 }}>{msg}</span>}
      </div>
    </div>
  );
}

interface AppConfigState {
  configured?: boolean;
  runtime?: string;
  port?: number;
  buildCommand?: string;
  startCommand?: string;
}

function AppPane({ domainId }: { domainId: number }) {
  const [cfg, setCfg] = useState<AppConfigState | null>(null);
  const [busy, setBusy] = useState<'load' | 'build' | 'restart' | null>(null);
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => {
    setBusy('load');
    api.get<AppConfigState>(`/domains/${domainId}/app`)
      .then(r => setCfg(r.data))
      .catch(() => setCfg({ configured: false }))
      .finally(() => setBusy(null));
  }, [domainId]);

  const run = async (action: 'build' | 'restart') => {
    setBusy(action); setMsg(null);
    try {
      await api.post(`/domains/${domainId}/app/${action}`);
      setMsg(action === 'build' ? 'Build started — check Logs for output.' : 'App restarted.');
    } catch (e) {
      const errMsg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setMsg(errMsg ?? (e instanceof Error ? e.message : 'Failed'));
    } finally { setBusy(null); }
  };

  if (cfg === null) return <div className="muted">Loading…</div>;
  if (!cfg.configured) return <div className="muted">No app configured for this domain. Configure runtime/port/build first via the API or a future App config page.</div>;

  return (
    <div className="col gap-3">
      <div className="row gap-3 mono" style={{ fontSize: 12 }}>
        <span>Runtime: <strong>{cfg.runtime}</strong></span>
        <span>Port: <strong>{cfg.port}</strong></span>
      </div>
      <Field label="Build command" hint="Read-only — set via API.">
        <input className="input mono" readOnly value={cfg.buildCommand ?? ''} />
      </Field>
      <Field label="Start command" hint="Read-only — set via API.">
        <input className="input mono" readOnly value={cfg.startCommand ?? ''} />
      </Field>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <button className="btn" onClick={() => run('build')} disabled={busy !== null}>{busy === 'build' ? 'Building…' : 'Build'}</button>
        <button className="btn primary" onClick={() => run('restart')} disabled={busy !== null}>{busy === 'restart' ? 'Restarting…' : 'Restart'}</button>
        {msg && <span className="muted" style={{ fontSize: 12 }}>{msg}</span>}
      </div>
    </div>
  );
}

/**
 * Phase C — per-domain PHP settings (Plesk-parity).
 * Pulls current version + extension flags + ini overrides from the panel,
 * plus the host's installed PHP versions + available extension catalog
 * (read off /etc/php on the agent). Save triggers php.updatePool which
 * rewrites the FPM pool config and reloads php{ver}-fpm.
 */
interface PhpState {
  currentVersion: string;
  extensions: string[];
  iniOverrides: Record<string, string>;
  host: {
    installedVersions: string[];
    availableExtensions: string[];
    currentlyEnabledExtensions: string[];
  };
}

const COMMON_INI_KEYS: { key: string; placeholder: string; hint: string }[] = [
  { key: 'memory_limit', placeholder: '256M', hint: 'Per-request memory ceiling.' },
  { key: 'upload_max_filesize', placeholder: '64M', hint: 'Largest single upload.' },
  { key: 'post_max_size', placeholder: '64M', hint: 'Total POST body size; must be >= upload_max_filesize.' },
  { key: 'max_execution_time', placeholder: '120', hint: 'Seconds before PHP kills a request.' },
  { key: 'max_input_vars', placeholder: '5000', hint: 'Cap on $_POST/$_GET keys.' },
  { key: 'display_errors', placeholder: 'Off', hint: 'On for dev; Off for prod.' },
  { key: 'date.timezone', placeholder: 'America/Los_Angeles', hint: 'Default tz when scripts don\'t set one.' },
  { key: 'opcache.enable', placeholder: '1', hint: 'OPcache is a big speedup for PHP apps.' },
  { key: 'opcache.memory_consumption', placeholder: '128', hint: 'OPcache RAM in MB.' },
];

function PhpPane({ domainId }: { domainId: number }) {
  const [state, setState] = useState<PhpState | null>(null);
  const [version, setVersion] = useState('');
  const [enabled, setEnabled] = useState<Set<string>>(new Set());
  const [ini, setIni] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);

  const load = async () => {
    setMsg(null);
    try {
      const { data } = await api.get<PhpState>(`/domains/${domainId}/php`);
      setState(data);
      setVersion(data.currentVersion);
      setEnabled(new Set(data.extensions));
      setIni({ ...data.iniOverrides });
    } catch (e) {
      setMsg({ ok: false, text: e instanceof Error ? e.message : 'Load failed' });
    }
  };
  useEffect(() => { void load(); }, [domainId]);

  const toggleExt = (ext: string) => {
    const next = new Set(enabled);
    if (next.has(ext)) next.delete(ext); else next.add(ext);
    setEnabled(next);
  };

  const save = async () => {
    setBusy(true); setMsg(null);
    try {
      const { data } = await api.put<{ success: boolean; agentMessage?: string }>(`/domains/${domainId}/php`, {
        phpVersion: version,
        extensions: Array.from(enabled),
        iniOverrides: Object.fromEntries(Object.entries(ini).filter(([, v]) => v !== ''))
      });
      setMsg({ ok: data.success, text: data.agentMessage ?? (data.success ? 'Saved — FPM pool reloaded.' : 'Save returned without success flag.') });
      void load();
    } catch (e) {
      const resp = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response : null;
      setMsg({ ok: false, text: resp?.data?.message ?? (e instanceof Error ? e.message : 'Save failed') });
    } finally { setBusy(false); }
  };

  if (state === null) return <div className="muted">Loading PHP state…</div>;

  return (
    <div className="col gap-3">
      <Field label="PHP version" hint="Pick from versions installed on the host. Install more via apt + ondrej/php PPA.">
        <select className="select" value={version} onChange={e => setVersion(e.target.value)}>
          {state.host.installedVersions.length === 0 && <option value={version}>{version} (host detection failed)</option>}
          {state.host.installedVersions.map(v => <option key={v} value={v}>PHP {v}</option>)}
        </select>
      </Field>

      <div>
        <div style={{ fontWeight: 600, marginBottom: 6 }}>Extensions</div>
        <div className="muted" style={{ fontSize: 12, marginBottom: 10 }}>
          Toggle which extensions this domain's FPM pool loads. Available on the host: {state.host.availableExtensions.length}.
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 6 }}>
          {state.host.availableExtensions.map(ext => (
            <label key={ext} style={{ display: 'flex', alignItems: 'center', gap: 6, cursor: 'pointer', fontSize: 12 }}>
              <input type="checkbox" checked={enabled.has(ext)} onChange={() => toggleExt(ext)} />
              <span className="mono">{ext}</span>
              {state.host.currentlyEnabledExtensions.includes(ext) && <Badge tone="success">on host</Badge>}
            </label>
          ))}
        </div>
      </div>

      <div>
        <div style={{ fontWeight: 600, marginBottom: 6 }}>php.ini overrides</div>
        <div className="muted" style={{ fontSize: 12, marginBottom: 10 }}>
          Leave blank to use the distro default. Only an allowlisted set of safe keys is accepted server-side.
        </div>
        <div style={{ display: 'grid', gap: 8 }}>
          {COMMON_INI_KEYS.map(({ key, placeholder, hint }) => (
            <Field key={key} label={key} hint={hint}>
              <input
                className="input mono"
                value={ini[key] ?? ''}
                placeholder={placeholder}
                onChange={e => setIni({ ...ini, [key]: e.target.value })}
              />
            </Field>
          ))}
        </div>
      </div>

      <div style={{ display: 'flex', gap: 8, alignItems: 'center', borderTop: '1px solid var(--border)', paddingTop: 12 }}>
        <button className="btn primary" onClick={save} disabled={busy || !version}>
          {busy ? 'Saving + reloading FPM…' : 'Save & reload PHP-FPM'}
        </button>
        {msg && <span style={{ color: msg.ok ? 'var(--success)' : 'var(--danger)', fontSize: 12 }}>{msg.text}</span>}
      </div>
    </div>
  );
}

function AddDomainDialog({ open, onClose, subs, onSaved }: { open: boolean; onClose: () => void; subs: PanelSubscription[]; onSaved: () => void }) {
  const [name, setName] = useState('');
  const [subscriptionId, setSubscriptionId] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setName('');
    setSubscriptionId(subs[0]?.id ?? null);
    setErr(null);
  }, [open, subs]);

  const submit = async () => {
    if (!subscriptionId || !name.trim()) return;
    setBusy(true);
    setErr(null);
    try {
      await api.post('/domains', { name: name.trim(), subscriptionId });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? (e instanceof Error ? e.message : 'Add domain failed'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Add domain"
      sub="Creates nginx vhost + docroot. SSL is issued separately on the SSL page."
      foot={
        <>
          {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
          <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn primary" onClick={submit} disabled={busy || !name.trim() || !subscriptionId}>{busy ? 'Adding…' : 'Add domain'}</button>
        </>
      }
    >
      <div className="col gap-3">
        <Field label="Domain name" hint="Don't include http:// or trailing slash.">
          <input className="input mono" autoFocus placeholder="example.com" value={name} onChange={e => setName(e.target.value.trim())} />
        </Field>
        <Field label="Subscription">
          <select className="select" value={subscriptionId ?? ''} onChange={e => setSubscriptionId(Number(e.target.value))}>
            {subs.map(s => (
              <option key={s.id} value={s.id}>
                {s.domains?.[0]?.name ?? `subscription #${s.id}`} — sub {s.id}
              </option>
            ))}
          </select>
        </Field>
      </div>
    </Dialog>
  );
}
