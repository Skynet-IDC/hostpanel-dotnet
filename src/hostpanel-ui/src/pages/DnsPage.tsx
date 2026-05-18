import { useEffect, useState } from 'react';
import api from '../api/client';
import { Card, Dialog, Empty, Field, IconBtn, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface DnsRecord { id: number; type: string; name: string; value: string; ttl: number; priority: number | null }

const DNS_TYPES = ['A', 'AAAA', 'CNAME', 'MX', 'TXT', 'SRV'] as const;

export function DnsPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [records, setRecords] = useState<DnsRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  useEffect(() => {
    api.get<{ id: number; name: string }[]>('/domains')
      .then(r => { setDomains(r.data); if (r.data.length > 0) setDomainId(r.data[0].id); })
      .finally(() => setLoading(false));
  }, []);

  const load = async () => {
    if (!domainId) return;
    setErr(null);
    try {
      const { data } = await api.get<DnsRecord[]>(`/domains/${domainId}/dns`);
      setRecords(data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load');
    }
  };

  useEffect(() => { void load(); }, [domainId]);

  const remove = async (id: number, name: string) => {
    if (!confirm(`Delete DNS record ${name}?`)) return;
    await api.delete(`/dns/${id}`);
    void load();
  };

  const selected = domains.find(d => d.id === domainId);

  return (
    <>
      <PageHead
        title="DNS records"
        sub={selected ? `Panel-managed PowerDNS zone for ${selected.name}` : 'Pick a domain'}
        actions={
          <>
            {domains.length > 0 && (
              <select className="select" value={domainId ?? ''} onChange={e => setDomainId(Number(e.target.value))} style={{ maxWidth: 240 }}>
                {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select>
            )}
            <button className="btn primary" onClick={() => setCreateOpen(true)} disabled={!domainId}><Icons.plus />Add record</button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : records.length === 0 ? <Empty icon="dns" title="No DNS records" sub="Add an A, CNAME, MX, TXT, or SRV record." />
          : (
            <table className="tbl">
              <thead><tr><th>Type</th><th>Name</th><th>Value</th><th>TTL</th><th>Priority</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {records.map(r => (
                  <tr key={r.id}>
                    <td><span className="tag">{r.type}</span></td>
                    <td className="mono">{r.name}</td>
                    <td className="mono muted" style={{ fontSize: 11.5, maxWidth: 320, overflow: 'hidden', textOverflow: 'ellipsis' }}>{r.value}</td>
                    <td className="mono">{r.ttl}</td>
                    <td className="mono">{r.priority ?? '—'}</td>
                    <td><div className="row-actions"><IconBtn icon="trash" danger title="Delete" onClick={() => remove(r.id, r.name)} /></div></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </Card>

      <CreateDialog open={createOpen} onClose={() => setCreateOpen(false)} domainId={domainId} onSaved={() => { setCreateOpen(false); void load(); }} />
    </>
  );
}

function CreateDialog({ open, onClose, domainId, onSaved }: { open: boolean; onClose: () => void; domainId: number | null; onSaved: () => void }) {
  const [type, setType] = useState<typeof DNS_TYPES[number]>('A');
  const [name, setName] = useState('@');
  const [value, setValue] = useState('');
  const [ttl, setTtl] = useState(3600);
  const [priority, setPriority] = useState(10);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => { if (open) { setType('A'); setName('@'); setValue(''); setTtl(3600); setPriority(10); setErr(null); } }, [open]);

  const submit = async () => {
    if (!domainId) return;
    setBusy(true); setErr(null);
    try {
      await api.post(`/domains/${domainId}/dns`, {
        type, name, value, ttl,
        priority: (type === 'MX' || type === 'SRV') ? priority : null,
      });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Add failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} title="Add DNS record"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !value}>{busy ? 'Adding…' : 'Add record'}</button>
      </>}>
      <div className="grid-2" style={{ gap: 12 }}>
        <Field label="Type">
          <select className="select" value={type} onChange={e => setType(e.target.value as typeof DNS_TYPES[number])}>
            {DNS_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
          </select>
        </Field>
        <Field label="Name"><input className="input mono" value={name} onChange={e => setName(e.target.value)} placeholder="@" /></Field>
        <Field label="Value" span={2}><input className="input mono" value={value} onChange={e => setValue(e.target.value)} placeholder={type === 'A' ? '192.0.2.1' : 'see RFC'} autoFocus /></Field>
        <Field label="TTL (seconds)"><input className="input mono" type="number" min={60} value={ttl} onChange={e => setTtl(Number(e.target.value))} /></Field>
        {(type === 'MX' || type === 'SRV') && (
          <Field label="Priority"><input className="input mono" type="number" min={0} value={priority} onChange={e => setPriority(Number(e.target.value))} /></Field>
        )}
      </div>
    </Dialog>
  );
}
