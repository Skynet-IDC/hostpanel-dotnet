import { useEffect, useState } from 'react';
import api from '../api/client';
import { Card, Empty, PageHead, Stat } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface PanelVersionInfo {
  currentVersion?: string;
  latestVersion?: string;
  updateAvailable?: boolean;
}

function PanelUpdateCard() {
  const [info, setInfo] = useState<PanelVersionInfo | null>(null);
  const [busy, setBusy] = useState<'check' | 'update' | 'rollback' | null>(null);
  const [msg, setMsg] = useState<string | null>(null);

  const refresh = async () => {
    setBusy('check'); setMsg(null);
    try {
      const r = await api.get<PanelVersionInfo>('/panel/check-update');
      setInfo(r.data);
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Check failed');
    } finally { setBusy(null); }
  };

  useEffect(() => { void refresh(); }, []);

  const update = async () => {
    if (!confirm('Update the HostPanel itself? The API will restart and may be unreachable for ~10s.')) return;
    setBusy('update'); setMsg(null);
    try {
      await api.post('/panel/update');
      setMsg('Update started — backup taken, new binaries built, services restarted. Refresh in 30s.');
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Update failed');
    } finally { setBusy(null); }
  };

  const rollback = async () => {
    if (!confirm('Roll back to the previous panel version? Files + DB restored from the last backup.')) return;
    setBusy('rollback'); setMsg(null);
    try {
      await api.post('/panel/rollback');
      setMsg('Rollback initiated.');
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Rollback failed');
    } finally { setBusy(null); }
  };

  return (
    <Card title="HostPanel update" sub="Update the panel itself (separate from OS updates above).">
      <div className="row gap-3" style={{ alignItems: 'center', flexWrap: 'wrap' }}>
        <div>
          <div className="muted" style={{ fontSize: 11 }}>Current</div>
          <div className="mono" style={{ fontWeight: 600 }}>{info?.currentVersion ?? '—'}</div>
        </div>
        <div>
          <div className="muted" style={{ fontSize: 11 }}>Latest</div>
          <div className="mono" style={{ fontWeight: 600 }}>{info?.latestVersion ?? '—'}</div>
        </div>
        <div style={{ marginLeft: 'auto', display: 'flex', gap: 8 }}>
          <button className="btn" onClick={refresh} disabled={busy !== null}><Icons.refresh />{busy === 'check' ? 'Checking…' : 'Check'}</button>
          <button className="btn primary" onClick={update} disabled={busy !== null || !info?.updateAvailable}>
            {busy === 'update' ? 'Updating…' : 'Update panel'}
          </button>
          <button className="btn" onClick={rollback} disabled={busy !== null}>
            {busy === 'rollback' ? 'Rolling back…' : 'Rollback'}
          </button>
        </div>
      </div>
      {msg && <div className="alert info" style={{ marginTop: 12, fontSize: 12 }}>{msg}</div>}
    </Card>
  );
}

interface PackageInfo {
  full?: string;
  package_name?: string;
  current_version?: string;
  available_version?: string;
}

export function ServerUpdatesPage() {
  const [packages, setPackages] = useState<PackageInfo[]>([]);
  const [count, setCount] = useState(0);
  const [checked, setChecked] = useState(false);
  const [loading, setLoading] = useState(false);
  const [installing, setInstalling] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const check = async () => {
    setLoading(true); setErr(null);
    try {
      await api.post('/server/updates/check');
      const r = await api.get<{ packages?: string; count?: number }>('/server/updates');
      const parsed: PackageInfo[] = r.data.packages ? JSON.parse(r.data.packages) : [];
      setPackages(parsed);
      setCount(r.data.count ?? parsed.length);
      setChecked(true);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Check failed');
    } finally { setLoading(false); }
  };

  const install = async (securityOnly: boolean) => {
    if (!confirm(securityOnly ? 'Install security updates? Some services may restart.' : 'Install ALL updates? Some services may restart.')) return;
    setInstalling(true); setErr(null);
    try {
      await api.post('/server/updates/install', { securityOnly });
      await check();
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Install failed');
    } finally { setInstalling(false); }
  };

  return (
    <>
      <PageHead
        title="System updates"
        sub="apt packages on the underlying OS"
        actions={
          <>
            <button className="btn" onClick={check} disabled={loading}><Icons.refresh />{loading ? 'Checking…' : 'Check for updates'}</button>
            <button className="btn" onClick={() => install(true)} disabled={installing || count === 0}><Icons.shield />Security only</button>
            <button className="btn primary" onClick={() => install(false)} disabled={installing || count === 0}>
              <Icons.download />{installing ? 'Installing…' : `Install all (${count})`}
            </button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <div className="stats" style={{ marginBottom: 16, gridTemplateColumns: 'repeat(2, 1fr)' }}>
        <Stat label="Available updates" icon="refresh" value={String(count)} foot={<span className="muted">{checked ? 'last checked just now' : 'click Check'}</span>} />
        <Stat label="Status" icon={count === 0 && checked ? 'check2' : 'info'} value={count === 0 && checked ? 'Up to date' : count > 0 ? 'Updates pending' : 'Unknown'} />
      </div>

      <div style={{ marginBottom: 16 }}><PanelUpdateCard /></div>

      <Card title="Available packages" sub={count === 0 ? 'No updates available' : `${count} package${count === 1 ? '' : 's'}`} flush>
        {!checked ? (
          <Empty icon="info" title="No data yet" sub='Click "Check for updates" to scan apt.' />
        ) : count === 0 ? (
          <Empty icon="check2" title="System is up to date" sub="Nothing to install." />
        ) : (
          <table className="tbl">
            <thead><tr><th>Package</th><th>Current</th><th>Available</th></tr></thead>
            <tbody>
              {packages.map((p, i) => (
                <tr key={`${p.package_name ?? p.full}-${i}`}>
                  <td className="mono" style={{ fontWeight: 600 }}>{p.package_name ?? p.full}</td>
                  <td className="mono muted">{p.current_version ?? '—'}</td>
                  <td className="mono">{p.available_version ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}
