import { useEffect, useState } from 'react';
import api from '../api/client';
import { Card, Dialog, Empty, Field, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

export function FirewallPage() {
  const [rules, setRules] = useState('');
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);

  const load = async () => {
    setLoading(true); setErr(null);
    try {
      const { data } = await api.get<{ rules?: string }>('/firewall/rules');
      setRules(data.rules ?? '');
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Load failed');
    } finally { setLoading(false); }
  };
  useEffect(() => { void load(); }, []);

  return (
    <>
      <PageHead
        title="Firewall"
        sub="UFW rules. Ports are open or closed at the host level."
        actions={
          <>
            <button className="btn" onClick={load}><Icons.refresh />Refresh</button>
            <button className="btn primary" onClick={() => setAddOpen(true)}><Icons.plus />Add rule</button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card title="Active rules" sub="ufw status verbose">
        {loading ? <div className="muted">Loading…</div>
          : !rules.trim() ? <Empty icon="shield" title="No firewall rules" sub="Add a rule to allow or deny a port." />
          : (
            <pre style={{ background: 'var(--bg)', border: '1px solid var(--border)', borderRadius: 8, padding: 12, fontFamily: 'var(--font-mono)', fontSize: 12, whiteSpace: 'pre-wrap', overflow: 'auto', margin: 0 }}>
              {rules}
            </pre>
          )}
      </Card>

      <AddDialog open={addOpen} onClose={() => setAddOpen(false)} onSaved={() => { setAddOpen(false); void load(); }} />
    </>
  );
}

function AddDialog({ open, onClose, onSaved }: { open: boolean; onClose: () => void; onSaved: () => void }) {
  const [action, setAction] = useState<'allow' | 'deny'>('allow');
  const [port, setPort] = useState('');
  const [protocol, setProtocol] = useState<'tcp' | 'udp' | 'any'>('tcp');
  const [fromIp, setFromIp] = useState('');
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => { if (open) { setAction('allow'); setPort(''); setProtocol('tcp'); setFromIp(''); setComment(''); setErr(null); } }, [open]);

  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      await api.post('/firewall/rules', { action, port, protocol, fromIp, comment });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Add failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} size="lg" title="Add firewall rule"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !port}>{busy ? 'Adding…' : 'Add'}</button>
      </>}>
      <div className="grid-2" style={{ gap: 12 }}>
        <Field label="Action">
          <select className="select" value={action} onChange={e => setAction(e.target.value as 'allow' | 'deny')}>
            <option value="allow">Allow</option>
            <option value="deny">Deny</option>
          </select>
        </Field>
        <Field label="Port"><input className="input mono" value={port} onChange={e => setPort(e.target.value)} placeholder="22, 80, 443, or 8000:9000" autoFocus /></Field>
        <Field label="Protocol">
          <select className="select" value={protocol} onChange={e => setProtocol(e.target.value as 'tcp' | 'udp' | 'any')}>
            <option value="tcp">TCP</option>
            <option value="udp">UDP</option>
            <option value="any">Any</option>
          </select>
        </Field>
        <Field label="From IP (optional)"><input className="input mono" value={fromIp} onChange={e => setFromIp(e.target.value)} placeholder="0.0.0.0/0" /></Field>
        <Field label="Comment" span={2}><input className="input" value={comment} onChange={e => setComment(e.target.value)} placeholder="e.g. CI deploy webhook" /></Field>
      </div>
    </Dialog>
  );
}
