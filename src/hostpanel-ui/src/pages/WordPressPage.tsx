import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Dialog, Empty, Field, IconBtn, PageHead, StatusBadge } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface WpSite {
  id: number;
  domainName: string;
  siteUrl: string;
  url?: string;
  version: string;
  autoUpdate: boolean;
  maintenanceMode: boolean;
  securityStatus: string;
  isStaging?: boolean;
  productionSiteId?: number | null;
}

export function WordPressPage() {
  const [sites, setSites] = useState<WpSite[]>([]);
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [installOpen, setInstallOpen] = useState(false);

  const load = async () => {
    setErr(null);
    try {
      const [wp, dom] = await Promise.all([api.get<WpSite[]>('/wordpress'), api.get<{ id: number; name: string }[]>('/domains')]);
      setSites(wp.data);
      setDomains(dom.data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Load failed');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const post = async (id: number, action: string, msg?: string) => {
    try {
      await api.post(`/wordpress/${id}/${action}`, {});
      if (msg) alert(msg);
      void load();
    } catch (e) {
      const m = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(m ?? `${action} failed`);
    }
  };

  const toggleMaintenance = async (s: WpSite) => {
    const next = !s.maintenanceMode;
    try { await api.put(`/wordpress/${s.id}/maintenance`, { enabled: next }); void load(); }
    catch (e) {
      const m = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(m ?? 'Toggle failed');
    }
  };

  const toggleAutoUpdate = async (s: WpSite) => {
    const next = !s.autoUpdate;
    try { await api.put(`/wordpress/${s.id}/auto-update`, { enabled: next }); void load(); }
    catch (e) {
      const m = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(m ?? 'Toggle failed');
    }
  };

  const updateAll = async () => {
    if (!confirm('Run wp-cli core update on every WordPress site? Sites briefly go into maintenance mode during update.')) return;
    try {
      await api.post('/wordpress/update-all', {});
      alert('Update initiated for all sites. Run a security scan after to verify.');
      void load();
    } catch (e) {
      const m = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(m ?? 'Update all failed');
    }
  };

  const createStaging = async (id: number) => {
    if (!confirm('Create a staging copy of this site? Adds *-staging.* domain + cloned DB.')) return;
    await post(id, 'staging', 'Staging site created.');
  };

  const pushStaging = async (id: number) => {
    if (!confirm('Push this staging site → production? Files + DB will overwrite the prod copy.')) return;
    await post(id, 'push-staging', 'Pushed to production.');
  };

  return (
    <>
      <PageHead
        title="WordPress"
        sub={`${sites.length} WP site${sites.length === 1 ? '' : 's'}`}
        actions={
          <>
            <button className="btn" onClick={updateAll} disabled={sites.length === 0} title="Run wp-cli core update on every site">
              <Icons.refresh />Update all sites
            </button>
            <button className="btn primary" onClick={() => setInstallOpen(true)} disabled={domains.length === 0}>
              <Icons.wordpress />Install WordPress
            </button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : sites.length === 0 ? <Empty icon="wordpress" title="No WordPress sites" sub="Install one on any of your domains." />
          : (
            <table className="tbl">
              <thead><tr><th>Domain</th><th>URL</th><th>Version</th><th>Auto-update</th><th>Maintenance</th><th>Security</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {sites.map(s => (
                  <tr key={s.id}>
                    <td className="mono" style={{ fontWeight: 600 }}>{s.domainName}</td>
                    <td><a className="mono muted" href={s.siteUrl ?? s.url} target="_blank" rel="noreferrer">{s.siteUrl ?? s.url}</a></td>
                    <td className="mono">
                      {s.version}
                      {s.isStaging && <span style={{ marginLeft: 6 }}><Badge tone="info">Staging</Badge></span>}
                    </td>
                    <td>
                      <button
                        className="btn ghost sm"
                        style={{ padding: '2px 8px' }}
                        onClick={() => toggleAutoUpdate(s)}
                        title="Toggle automatic WordPress core updates"
                      >
                        {s.autoUpdate ? <Badge tone="success" dot>On</Badge> : <Badge>Off</Badge>}
                      </button>
                    </td>
                    <td>
                      <button
                        className="btn ghost sm"
                        style={{ padding: '2px 8px' }}
                        onClick={() => toggleMaintenance(s)}
                        title="Toggle WP maintenance mode (.maintenance file)"
                      >
                        {s.maintenanceMode ? <Badge tone="warn" dot>On</Badge> : <Badge>Off</Badge>}
                      </button>
                    </td>
                    <td><StatusBadge status={s.securityStatus} /></td>
                    <td>
                      <div className="row-actions">
                        <IconBtn icon="bug" title="Security scan" onClick={() => post(s.id, 'security-scan', 'Scan triggered. Check status shortly.')} />
                        {!s.isStaging && (
                          <IconBtn icon="copy" title="Create staging copy" onClick={() => createStaging(s.id)} />
                        )}
                        {s.isStaging && s.productionSiteId && (
                          <IconBtn icon="upload" title="Push staging → production" onClick={() => pushStaging(s.id)} />
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </Card>

      <InstallDialog open={installOpen} onClose={() => setInstallOpen(false)} domains={domains} onSaved={() => { setInstallOpen(false); void load(); }} />
    </>
  );
}

function InstallDialog({ open, onClose, domains, onSaved }: { open: boolean; onClose: () => void; domains: { id: number; name: string }[]; onSaved: () => void }) {
  const [domainId, setDomainId] = useState<number | null>(null);
  const [title, setTitle] = useState('');
  const [adminUser, setAdminUser] = useState('admin');
  const [adminEmail, setAdminEmail] = useState('');
  const [adminPassword, setAdminPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setDomainId(domains[0]?.id ?? null);
      setTitle(''); setAdminUser('admin'); setAdminEmail(''); setAdminPassword(''); setErr(null);
    }
  }, [open, domains]);

  const submit = async () => {
    if (!domainId) return;
    setBusy(true); setErr(null);
    try {
      await api.post('/wordpress/install', { domainId, siteTitle: title, adminUser, adminEmail, adminPassword });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Install failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} size="lg" title="Install WordPress" sub="Runs wp-cli core install. Takes ~30 seconds."
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !domainId || !title || !adminEmail || !adminPassword}>{busy ? 'Installing…' : 'Install'}</button>
      </>}>
      <div className="grid-2" style={{ gap: 12 }}>
        <Field label="Domain"><select className="select" value={domainId ?? ''} onChange={e => setDomainId(Number(e.target.value))}>{domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}</select></Field>
        <Field label="Site title"><input className="input" value={title} onChange={e => setTitle(e.target.value)} /></Field>
        <Field label="Admin username"><input className="input mono" value={adminUser} onChange={e => setAdminUser(e.target.value)} /></Field>
        <Field label="Admin email"><input className="input mono" type="email" value={adminEmail} onChange={e => setAdminEmail(e.target.value)} /></Field>
        <Field label="Admin password" span={2}><input className="input mono" type="password" value={adminPassword} onChange={e => setAdminPassword(e.target.value)} /></Field>
      </div>
    </Dialog>
  );
}
