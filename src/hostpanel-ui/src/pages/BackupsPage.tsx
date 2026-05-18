import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Empty, IconBtn, PageHead, StatusBadge } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';
import { panelApi, type PanelSubscription } from '../api/panel';

interface BackupRow {
  id: number;
  type: string;
  sizeBytes: number;
  isCompleted: boolean;
  createdAt: string;
  status?: string;
}

export function BackupsPage() {
  const [subs, setSubs] = useState<PanelSubscription[]>([]);
  const [subId, setSubId] = useState<number | null>(null);
  const [backups, setBackups] = useState<BackupRow[]>([]);
  const [creating, setCreating] = useState(false);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    panelApi.listSubscriptions()
      .then(s => {
        setSubs(s);
        if (s.length > 0) setSubId(s[0].id);
      })
      .catch(e => setErr(e instanceof Error ? e.message : 'Failed to load subscriptions'))
      .finally(() => setLoading(false));
  }, []);

  const load = async () => {
    if (!subId) return;
    setErr(null);
    try {
      const { data } = await api.get<BackupRow[]>(`/subscriptions/${subId}/backups`);
      setBackups(data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load backups');
    }
  };

  useEffect(() => { void load(); }, [subId]);

  const create = async () => {
    if (!subId) return;
    setCreating(true);
    try {
      await api.post(`/subscriptions/${subId}/backups`);
      await load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Create failed');
    } finally {
      setCreating(false);
    }
  };

  const restore = async (id: number) => {
    if (!confirm('Restore this backup? Files will be replaced from the snapshot.')) return;
    try {
      await api.post(`/backups/${id}/restore`);
      alert('Restore initiated');
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Restore failed');
    }
  };

  return (
    <>
      <PageHead
        title="Backups"
        sub={subId ? `${backups.length} backup${backups.length === 1 ? '' : 's'} for this subscription` : 'Pick a subscription'}
        actions={
          <>
            {subs.length > 0 && (
              <select className="select" value={subId ?? ''} onChange={e => setSubId(Number(e.target.value))} style={{ maxWidth: 220 }}>
                {subs.map(s => (
                  <option key={s.id} value={s.id}>{s.systemUsername || s.domains?.[0]?.name || `Sub #${s.id}`}</option>
                ))}
              </select>
            )}
            <button className="btn primary" onClick={create} disabled={creating || !subId}>
              <Icons.archive />{creating ? 'Creating…' : 'Create backup'}
            </button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card flush>
        {loading ? (
          <div className="muted" style={{ padding: 16 }}>Loading…</div>
        ) : backups.length === 0 ? (
          <Empty
            icon="archive"
            title="No backups yet"
            sub="Create one now, or wait for the scheduled nightly job."
            action={
              <button className="btn primary" onClick={create} style={{ marginTop: 4 }} disabled={creating || !subId}>
                <Icons.archive />Create backup
              </button>
            }
          />
        ) : (
          <table className="tbl">
            <thead>
              <tr><th>ID</th><th>Type</th><th>Size</th><th>Status</th><th>Created</th><th style={{ textAlign: 'right' }} /></tr>
            </thead>
            <tbody>
              {backups.map(b => (
                <tr key={b.id}>
                  <td className="mono">{b.id}</td>
                  <td><Badge>{b.type}</Badge></td>
                  <td className="mono">{(b.sizeBytes / 1024 / 1024).toFixed(1)} MB</td>
                  <td><StatusBadge status={b.isCompleted ? 'success' : (b.status ?? 'pending')} /></td>
                  <td className="muted mono" style={{ fontSize: 11.5 }}>{new Date(b.createdAt).toLocaleString()}</td>
                  <td>
                    <div className="row-actions">
                      <IconBtn icon="refresh" title="Restore" onClick={() => restore(b.id)} />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}
