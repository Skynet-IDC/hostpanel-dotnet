import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Dialog, Empty, Field, IconBtn, PageHead, StatusBadge } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface Webhook {
  id: number;
  url: string;
  events: string[];
  secret?: string;
  active: boolean;
  lastTriggered?: string | null;
  failureCount?: number;
  lastFailure?: string | null;
}

const FALLBACK_EVENTS = [
  'domain.created', 'domain.deleted',
  'subscription.created', 'subscription.suspended', 'subscription.resumed', 'subscription.cancelled',
  'ssl.issued', 'ssl.renewed',
  'backup.completed', 'backup.failed',
  'invoice.paid', 'invoice.overdue',
  'mailbox.created', 'mailbox.deleted',
];

interface Delivery {
  id: number;
  event: string;
  url?: string;
  statusCode?: number;
  responseTimeMs?: number;
  attempts?: number;
  succeeded?: boolean;
  payload?: string;
  response?: string;
  createdAt: string;
}

export function WebhooksPage() {
  const [webhooks, setWebhooks] = useState<Webhook[]>([]);
  const [availableEvents, setAvailableEvents] = useState<string[]>(FALLBACK_EVENTS);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<Webhook | null>(null);
  const [deliveriesFor, setDeliveriesFor] = useState<Webhook | null>(null);
  const [deliveries, setDeliveries] = useState<Delivery[]>([]);
  const [deliveriesBusy, setDeliveriesBusy] = useState(false);

  const openDeliveries = async (w: Webhook) => {
    setDeliveriesFor(w);
    setDeliveries([]);
    setDeliveriesBusy(true);
    try {
      const r = await api.get<Delivery[]>(`/webhooks/${w.id}/deliveries`);
      setDeliveries(Array.isArray(r.data) ? r.data : []);
    } catch (e) {
      console.error('deliveries load failed', e);
    } finally { setDeliveriesBusy(false); }
  };

  const load = async () => {
    setErr(null);
    try {
      const { data } = await api.get<Webhook[]>('/webhooks');
      setWebhooks(data);
    } catch (e) { setErr(e instanceof Error ? e.message : 'Load failed'); }
    finally { setLoading(false); }
  };

  // Fetch the live list of event names from the backend so the create form stays in sync
  // when new events are added on the server side. The endpoint returns either:
  //   - string[]                          (older shape)
  //   - { events: string[] }              (wrapped)
  //   - [{ name, category, action }, …]   (newer shape — extract `name`)
  useEffect(() => {
    api.get<unknown>('/webhooks/events')
      .then(r => {
        const raw = r.data;
        let names: string[] = [];
        if (Array.isArray(raw)) {
          names = raw.map((x: unknown) =>
            typeof x === 'string'
              ? x
              : (x && typeof x === 'object' && 'name' in (x as Record<string, unknown>))
                ? String((x as { name: unknown }).name)
                : ''
          ).filter(Boolean);
        } else if (raw && typeof raw === 'object' && 'events' in (raw as Record<string, unknown>)) {
          const list = (raw as { events: unknown }).events;
          if (Array.isArray(list)) names = list.filter((x): x is string => typeof x === 'string');
        }
        if (names.length > 0) setAvailableEvents(names);
      })
      .catch(() => { /* fall back to hard-coded list */ });
  }, []);

  useEffect(() => { void load(); }, []);

  const resetCounter = async (id: number) => {
    if (!confirm('Reset the failure counter for this webhook? Useful after fixing endpoint issues.')) return;
    try {
      await api.post(`/webhooks/${id}/reset`, {});
      void load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Reset failed');
    }
  };

  const remove = async (id: number) => {
    if (!confirm('Delete this webhook?')) return;
    await api.delete(`/webhooks/${id}`);
    void load();
  };

  const test = async (id: number) => {
    try {
      await api.post(`/webhooks/${id}/test`, {});
      alert('Test event sent. Check your endpoint logs.');
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Test failed');
    }
  };

  return (
    <>
      <PageHead
        title="Webhooks"
        sub={`${webhooks.length} endpoint${webhooks.length === 1 ? '' : 's'} · HMAC-signed deliveries`}
        actions={<button className="btn primary" onClick={() => setCreateOpen(true)}><Icons.plus />New webhook</button>}
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : webhooks.length === 0 ? <Empty icon="webhook" title="No webhooks" sub="Subscribe to panel events from your own systems." />
          : (
            <table className="tbl">
              <thead><tr><th>URL</th><th>Events</th><th>Last triggered</th><th>Status</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {webhooks.map(w => (
                  <tr key={w.id}>
                    <td className="mono" style={{ fontSize: 11.5, maxWidth: 320, overflow: 'hidden', textOverflow: 'ellipsis' }}>{w.url}</td>
                    <td>
                      <div className="row gap-1" style={{ flexWrap: 'wrap' }}>
                        {w.events.slice(0, 3).map(e => <Badge key={e}>{e}</Badge>)}
                        {w.events.length > 3 && <Badge>+{w.events.length - 3}</Badge>}
                      </div>
                    </td>
                    <td className="muted mono" style={{ fontSize: 11.5 }}>{w.lastTriggered ? new Date(w.lastTriggered).toLocaleString() : 'Never'}</td>
                    <td>
                      <div className="row gap-1" style={{ flexWrap: 'wrap' }}>
                        <StatusBadge status={w.active ? (w.failureCount && w.failureCount > 0 ? 'warn' : 'active') : 'disabled'} />
                        {w.failureCount !== undefined && w.failureCount > 0 && (
                          <Badge tone="danger">{w.failureCount} fail{w.failureCount === 1 ? '' : 's'}</Badge>
                        )}
                      </div>
                    </td>
                    <td>
                      <div className="row-actions">
                        <IconBtn icon="list" title="Delivery history" onClick={() => void openDeliveries(w)} />
                        <IconBtn icon="zap" title="Send test event" onClick={() => test(w.id)} />
                        {w.failureCount !== undefined && w.failureCount > 0 && (
                          <IconBtn icon="refresh" title="Reset failure counter" onClick={() => resetCounter(w.id)} />
                        )}
                        <IconBtn icon="edit" title="Edit" onClick={() => setEditing(w)} />
                        <IconBtn icon="trash" danger title="Delete" onClick={() => remove(w.id)} />
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </Card>

      <Form open={createOpen} availableEvents={availableEvents} onClose={() => setCreateOpen(false)} onSaved={() => { setCreateOpen(false); void load(); }} />
      <Form open={!!editing} availableEvents={availableEvents} initial={editing ?? undefined} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); void load(); }} />

      <Dialog
        open={deliveriesFor !== null}
        onClose={() => { setDeliveriesFor(null); setDeliveries([]); }}
        size="lg"
        title={`Deliveries — ${deliveriesFor?.url ?? ''}`}
        sub={`${deliveries.length} record${deliveries.length === 1 ? '' : 's'} · HMAC-signed`}
        foot={<button className="btn ghost" onClick={() => { setDeliveriesFor(null); setDeliveries([]); }}>Close</button>}
      >
        {deliveriesBusy ? <div className="muted">Loading…</div>
          : deliveries.length === 0 ? <Empty icon="webhook" title="No deliveries yet" sub="The endpoint has not received any events." />
          : (
            <table className="tbl">
              <thead><tr><th>When</th><th>Event</th><th>Status</th><th>Latency</th><th>Attempts</th></tr></thead>
              <tbody>
                {deliveries.slice(0, 50).map(d => (
                  <tr key={d.id}>
                    <td className="mono muted" style={{ fontSize: 11.5 }}>{new Date(d.createdAt).toLocaleString()}</td>
                    <td className="mono">{d.event}</td>
                    <td>
                      <Badge tone={d.succeeded ? 'success' : 'danger'} dot>{d.statusCode ?? (d.succeeded ? 'OK' : 'fail')}</Badge>
                    </td>
                    <td className="mono muted">{d.responseTimeMs ? `${d.responseTimeMs} ms` : '—'}</td>
                    <td className="mono">{d.attempts ?? 1}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </Dialog>
    </>
  );
}

function Form({ open, onClose, initial, onSaved, availableEvents }: { open: boolean; onClose: () => void; initial?: Webhook; onSaved: () => void; availableEvents: string[] }) {
  const [url, setUrl] = useState('');
  const [events, setEvents] = useState<string[]>([]);
  const [secret, setSecret] = useState('');
  const [active, setActive] = useState(true);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setUrl(initial?.url ?? '');
    setEvents(initial?.events ?? ['domain.created']);
    setSecret(initial?.secret ?? '');
    setActive(initial?.active ?? true);
    setErr(null);
  }, [open, initial]);

  const toggle = (ev: string) => setEvents(es => es.includes(ev) ? es.filter(e => e !== ev) : [...es, ev]);

  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      const body = { url, events, secret: secret || undefined, active };
      if (initial) await api.put(`/webhooks/${initial.id}`, body);
      else await api.post('/webhooks', body);
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Save failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} size="lg" title={initial ? 'Edit webhook' : 'New webhook'}
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !url || events.length === 0}>{busy ? 'Saving…' : 'Save'}</button>
      </>}>
      <div className="col gap-4">
        <Field label="Endpoint URL" hint="Your server, plus an optional path. Must accept POST + verify HMAC.">
          <input className="input mono" value={url} onChange={e => setUrl(e.target.value)} placeholder="https://example.com/hooks/hostpanel" autoFocus />
        </Field>
        <Field label="HMAC secret" hint="Optional. Used to sign the X-HostPanel-Signature header.">
          <input className="input mono" type="password" value={secret} onChange={e => setSecret(e.target.value)} placeholder="(leave blank to keep existing)" />
        </Field>
        <Field label="Events">
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 6 }}>
            {availableEvents.map(ev => (
              <label key={ev} className="row gap-2" style={{ fontSize: 12, cursor: 'pointer', padding: '4px 8px', borderRadius: 4, background: events.includes(ev) ? 'color-mix(in oklch, var(--primary-500) 12%, transparent)' : 'transparent' }}>
                <input type="checkbox" checked={events.includes(ev)} onChange={() => toggle(ev)} />
                <span className="mono">{ev}</span>
              </label>
            ))}
          </div>
        </Field>
      </div>
    </Dialog>
  );
}
