import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Empty, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface AppInfo { id: string; name: string; category: string; description: string }

export function AppsPage() {
  const [apps, setApps] = useState<AppInfo[]>([]);
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [installing, setInstalling] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      api.get<AppInfo[]>('/domains/0/apps/available').catch(() => ({ data: [] as AppInfo[] })),
      api.get<{ id: number; name: string }[]>('/domains').catch(() => ({ data: [] as { id: number; name: string }[] })),
    ]).then(([a, d]) => {
      setApps(a.data); setDomains(d.data);
    }).catch(e => setErr(e instanceof Error ? e.message : 'Load failed'))
      .finally(() => setLoading(false));
  }, []);

  const install = async (appId: string, domainId: number) => {
    setInstalling(appId);
    try {
      await api.post(`/domains/${domainId}/apps/install`, { appId, adminUser: 'admin', adminPassword: '', adminEmail: '' });
      alert(`${appId} installation started.`);
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Install failed');
    } finally { setInstalling(null); }
  };

  const categories = [...new Set(apps.map(a => a.category))];

  return (
    <>
      <PageHead title="One-click apps" sub={`${apps.length} app${apps.length === 1 ? '' : 's'} ready to install`} />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {loading ? <Card><div className="muted" style={{ padding: 8 }}>Loading…</div></Card>
        : apps.length === 0 ? <Card><Empty icon="package" title="No apps available" sub="The app catalog endpoint returned empty. Wired to /domains/0/apps/available." /></Card>
        : categories.map(cat => (
          <div key={cat} style={{ marginBottom: 20 }}>
            <h3 style={{ fontSize: 13, fontWeight: 700, color: 'var(--muted)', textTransform: 'uppercase', letterSpacing: '0.08em', marginBottom: 12 }}>{cat}</h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 12 }}>
              {apps.filter(a => a.category === cat).map(app => (
                <div key={app.id} className="card" style={{ padding: 14 }}>
                  <div className="row between" style={{ marginBottom: 8 }}>
                    <div style={{ fontWeight: 600 }}>{app.name}</div>
                    <Badge>{app.id}</Badge>
                  </div>
                  <div className="muted" style={{ fontSize: 12, marginBottom: 12, minHeight: 32 }}>{app.description}</div>
                  {domains.length === 0 ? (
                    <div className="muted" style={{ fontSize: 11 }}>No domains available</div>
                  ) : (
                    <select
                      className="select"
                      defaultValue=""
                      disabled={installing === app.id}
                      onChange={e => e.target.value && install(app.id, Number(e.target.value))}
                    >
                      <option value="" disabled>{installing === app.id ? 'Installing…' : 'Install on…'}</option>
                      {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
                    </select>
                  )}
                </div>
              ))}
            </div>
          </div>
        ))}
    </>
  );
}
