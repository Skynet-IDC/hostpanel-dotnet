import { useEffect, useState } from 'react';
import api from '../api/client';
import { Card, Dialog, Empty, Field, IconBtn, PageHead, StatusBadge } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface Container { id: number; name: string; image: string; status: string; ports: string }
interface Sub { id: number; name: string }

export function DockerPage() {
  const [subs, setSubs] = useState<Sub[]>([]);
  const [subId, setSubId] = useState<number | null>(null);
  const [containers, setContainers] = useState<Container[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  useEffect(() => {
    api.get<Sub[]>('/subscriptions').then(r => { setSubs(r.data); if (r.data.length > 0) setSubId(r.data[0].id); }).finally(() => setLoading(false));
  }, []);

  const load = async () => {
    if (!subId) return;
    setErr(null);
    try {
      const { data } = await api.get<Container[]>(`/subscriptions/${subId}/docker`);
      setContainers(data);
    } catch (e) { setErr(e instanceof Error ? e.message : 'Load failed'); }
  };
  useEffect(() => { void load(); }, [subId]);

  const act = async (id: number, action: 'start' | 'stop' | 'restart' | 'delete') => {
    if (!subId) return;
    if (action === 'delete' && !confirm('Delete this container?')) return;
    try {
      if (action === 'delete') await api.delete(`/subscriptions/${subId}/docker/${id}`);
      else await api.post(`/subscriptions/${subId}/docker/${id}/${action}`);
      void load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? `${action} failed`);
    }
  };

  return (
    <>
      <PageHead title="Docker" sub={`${containers.length} container${containers.length === 1 ? '' : 's'}`}
        actions={
          <>
            {subs.length > 0 && (
              <select className="select" value={subId ?? ''} onChange={e => setSubId(Number(e.target.value))} style={{ maxWidth: 220 }}>
                {subs.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
            )}
            <button className="btn primary" onClick={() => setCreateOpen(true)} disabled={!subId}><Icons.plus />New container</button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : containers.length === 0 ? <Empty icon="docker" title="No containers" sub="Run a container — nginx, node app, custom image, anything." />
          : (
            <table className="tbl">
              <thead><tr><th>Name</th><th>Image</th><th>Status</th><th>Ports</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {containers.map(c => (
                  <tr key={c.id}>
                    <td style={{ fontWeight: 600 }}>{c.name}</td>
                    <td className="mono muted">{c.image}</td>
                    <td><StatusBadge status={c.status} /></td>
                    <td className="mono muted">{c.ports || '—'}</td>
                    <td>
                      <div className="row-actions">
                        <IconBtn icon="play" title="Start" onClick={() => act(c.id, 'start')} />
                        <IconBtn icon="pause" title="Stop" onClick={() => act(c.id, 'stop')} />
                        <IconBtn icon="refresh" title="Restart" onClick={() => act(c.id, 'restart')} />
                        <IconBtn icon="trash" danger title="Delete" onClick={() => act(c.id, 'delete')} />
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </Card>

      <CreateDialog open={createOpen} onClose={() => setCreateOpen(false)} subId={subId} onSaved={() => { setCreateOpen(false); void load(); }} />
    </>
  );
}

function CreateDialog({ open, onClose, subId, onSaved }: { open: boolean; onClose: () => void; subId: number | null; onSaved: () => void }) {
  const [name, setName] = useState('');
  const [image, setImage] = useState('');
  const [ports, setPorts] = useState('');
  const [envText, setEnvText] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => { if (open) { setName(''); setImage(''); setPorts(''); setEnvText(''); setErr(null); } }, [open]);

  const submit = async () => {
    if (!subId) return;
    setBusy(true); setErr(null);
    try {
      const environmentVariables: Record<string, string> = {};
      envText.split('\n').forEach(line => {
        const idx = line.indexOf('=');
        if (idx > 0) environmentVariables[line.slice(0, idx).trim()] = line.slice(idx + 1).trim();
      });
      await api.post(`/subscriptions/${subId}/docker`, { name, image, ports, environmentVariables });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Create failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} size="lg" title="New container"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !name || !image}>{busy ? 'Creating…' : 'Create'}</button>
      </>}>
      <div className="grid-2" style={{ gap: 12 }}>
        <Field label="Name"><input className="input mono" value={name} onChange={e => setName(e.target.value)} placeholder="my-app" autoFocus /></Field>
        <Field label="Image"><input className="input mono" value={image} onChange={e => setImage(e.target.value)} placeholder="nginx:alpine" /></Field>
        <Field label="Ports" span={2} hint="Host:container, comma-separated."><input className="input mono" value={ports} onChange={e => setPorts(e.target.value)} placeholder="8080:80, 443:443" /></Field>
        <Field label="Environment variables" span={2} hint="One per line, KEY=value."><textarea className="textarea" rows={4} value={envText} onChange={e => setEnvText(e.target.value)} placeholder={'KEY=value\nANOTHER=value'} /></Field>
      </div>
    </Dialog>
  );
}
