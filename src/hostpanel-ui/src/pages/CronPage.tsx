import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Dialog, Empty, Field, IconBtn, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';
import { panelApi, type PanelSubscription } from '../api/panel';

interface CronJob { id: number; command: string; schedule: string; enabled: boolean; description?: string }

export function CronPage() {
  const [subs, setSubs] = useState<PanelSubscription[]>([]);
  const [subId, setSubId] = useState<number | null>(null);
  const [jobs, setJobs] = useState<CronJob[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  useEffect(() => {
    panelApi.listSubscriptions()
      .then(s => { setSubs(s); if (s.length > 0) setSubId(s[0].id); })
      .finally(() => setLoading(false));
  }, []);

  const load = async () => {
    if (!subId) return;
    setErr(null);
    try {
      const { data } = await api.get<CronJob[]>(`/subscriptions/${subId}/cron`);
      setJobs(data);
    } catch (e) { setErr(e instanceof Error ? e.message : 'Load failed'); }
  };
  useEffect(() => { void load(); }, [subId]);

  const remove = async (id: number) => {
    if (!subId || !confirm('Delete this cron job?')) return;
    await api.delete(`/subscriptions/${subId}/cron/${id}`);
    void load();
  };

  return (
    <>
      <PageHead
        title="Cron jobs"
        sub="Scheduled tasks run by the tenant's crontab"
        actions={
          <>
            {subs.length > 0 && (
              <select className="select" value={subId ?? ''} onChange={e => setSubId(Number(e.target.value))} style={{ maxWidth: 220 }}>
                {subs.map(s => <option key={s.id} value={s.id}>{s.systemUsername || s.domains?.[0]?.name || `Sub #${s.id}`}</option>)}
              </select>
            )}
            <button className="btn primary" onClick={() => setCreateOpen(true)} disabled={!subId}><Icons.plus />New cron job</button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : jobs.length === 0 ? <Empty icon="clock" title="No cron jobs" sub="Schedule a script or curl on a recurring interval." />
          : (
            <table className="tbl">
              <thead><tr><th>Schedule</th><th>Command</th><th>Description</th><th>Status</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {jobs.map(j => (
                  <tr key={j.id}>
                    <td className="mono">{j.schedule}</td>
                    <td className="mono muted" style={{ fontSize: 11.5, maxWidth: 320, overflow: 'hidden', textOverflow: 'ellipsis' }}>{j.command}</td>
                    <td className="muted" style={{ fontSize: 12 }}>{j.description ?? '—'}</td>
                    <td>{j.enabled ? <Badge tone="success" dot>Active</Badge> : <Badge>Disabled</Badge>}</td>
                    <td><div className="row-actions"><IconBtn icon="trash" danger title="Delete" onClick={() => remove(j.id)} /></div></td>
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
  const [schedule, setSchedule] = useState('*/15 * * * *');
  const [command, setCommand] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => { if (open) { setSchedule('*/15 * * * *'); setCommand(''); setDescription(''); setErr(null); } }, [open]);

  const submit = async () => {
    if (!subId) return;
    setBusy(true); setErr(null);
    try {
      await api.post(`/subscriptions/${subId}/cron`, { schedule, command, description });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Add failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} size="lg" title="New cron job"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !schedule || !command}>{busy ? 'Adding…' : 'Add'}</button>
      </>}>
      <div className="col gap-3">
        <Field label="Schedule" hint="Standard 5-field cron: min hour dom mon dow"><input className="input mono" value={schedule} onChange={e => setSchedule(e.target.value)} autoFocus /></Field>
        <Field label="Command"><input className="input mono" value={command} onChange={e => setCommand(e.target.value)} placeholder="/usr/bin/php /var/www/example/cron.php" /></Field>
        <Field label="Description (optional)"><input className="input" value={description} onChange={e => setDescription(e.target.value)} placeholder="e.g. Refresh search index" /></Field>
      </div>
    </Dialog>
  );
}
