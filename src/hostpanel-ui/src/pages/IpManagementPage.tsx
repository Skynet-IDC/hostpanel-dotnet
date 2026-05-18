import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Dialog, Empty, Field, IconBtn, PageHead, Tabs } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface IpAddr { id: number; address: string; isShared: boolean; note?: string }
interface BlockedIp { id: number; address: string; reason?: string; expiresAt?: string | null }

type TabId = 'pool' | 'blocked';

export function IpManagementPage() {
  const [tab, setTab] = useState<TabId>('pool');
  const [pool, setPool] = useState<IpAddr[]>([]);
  const [blocked, setBlocked] = useState<BlockedIp[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [addPoolOpen, setAddPoolOpen] = useState(false);
  const [addBlockOpen, setAddBlockOpen] = useState(false);

  const load = async () => {
    setErr(null);
    try {
      const [a, b] = await Promise.all([
        api.get<IpAddr[]>('/ip/addresses').catch(() => ({ data: [] as IpAddr[] })),
        api.get<BlockedIp[]>('/ip/blacklist').catch(() => ({ data: [] as BlockedIp[] })),
      ]);
      setPool(a.data);
      setBlocked(b.data);
    } catch (e) { setErr(e instanceof Error ? e.message : 'Load failed'); }
    finally { setLoading(false); }
  };
  useEffect(() => { void load(); }, []);

  const removePool = async (id: number) => {
    if (!confirm('Remove this IP from the pool?')) return;
    await api.delete(`/ip/addresses/${id}`);
    void load();
  };
  const unblock = async (id: number) => {
    await api.delete(`/ip/blacklist/${id}`);
    void load();
  };

  return (
    <>
      <PageHead
        title="IP management"
        sub={`${pool.length} in pool · ${blocked.length} blocked`}
        actions={
          <button className="btn primary" onClick={() => tab === 'pool' ? setAddPoolOpen(true) : setAddBlockOpen(true)}>
            <Icons.plus />{tab === 'pool' ? 'Add IP' : 'Block IP'}
          </button>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Tabs active={tab} onChange={id => setTab(id as TabId)} tabs={[
        { id: 'pool', label: 'IP pool', count: pool.length },
        { id: 'blocked', label: 'Blocked IPs', count: blocked.length },
      ]} />

      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : tab === 'pool' ? (
            pool.length === 0 ? <Empty icon="network" title="No IPs in pool" sub="Add an IP that this server can serve from." />
              : (
                <table className="tbl">
                  <thead><tr><th>Address</th><th>Type</th><th>Note</th><th style={{ textAlign: 'right' }} /></tr></thead>
                  <tbody>
                    {pool.map(a => (
                      <tr key={a.id}>
                        <td className="mono" style={{ fontWeight: 600 }}>{a.address}</td>
                        <td>{a.isShared ? <Badge>Shared</Badge> : <Badge tone="primary">Dedicated</Badge>}</td>
                        <td className="muted">{a.note ?? '—'}</td>
                        <td><div className="row-actions"><IconBtn icon="trash" danger title="Remove" onClick={() => removePool(a.id)} /></div></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )
          ) : (
            blocked.length === 0 ? <Empty icon="shield" title="No blocked IPs" sub="Block abusers by IP at the panel level." />
              : (
                <table className="tbl">
                  <thead><tr><th>Address</th><th>Reason</th><th>Expires</th><th style={{ textAlign: 'right' }} /></tr></thead>
                  <tbody>
                    {blocked.map(b => (
                      <tr key={b.id}>
                        <td className="mono" style={{ fontWeight: 600 }}>{b.address}</td>
                        <td className="muted">{b.reason ?? '—'}</td>
                        <td className="muted mono" style={{ fontSize: 11.5 }}>{b.expiresAt ? new Date(b.expiresAt).toLocaleString() : 'Permanent'}</td>
                        <td><div className="row-actions"><IconBtn icon="check" title="Unblock" onClick={() => unblock(b.id)} /></div></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )
          )
        }
      </Card>

      <AddPool open={addPoolOpen} onClose={() => setAddPoolOpen(false)} onSaved={() => { setAddPoolOpen(false); void load(); }} />
      <BlockIp open={addBlockOpen} onClose={() => setAddBlockOpen(false)} onSaved={() => { setAddBlockOpen(false); void load(); }} />
    </>
  );
}

function AddPool({ open, onClose, onSaved }: { open: boolean; onClose: () => void; onSaved: () => void }) {
  const [address, setAddress] = useState('');
  const [isShared, setIsShared] = useState(true);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  useEffect(() => { if (open) { setAddress(''); setIsShared(true); setNote(''); setErr(null); } }, [open]);
  const submit = async () => {
    setBusy(true); setErr(null);
    try { await api.post('/ip/addresses', { address, isShared, note }); onSaved(); }
    catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Add failed');
    } finally { setBusy(false); }
  };
  return (
    <Dialog open={open} onClose={onClose} title="Add IP to pool"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !address}>{busy ? 'Adding…' : 'Add'}</button>
      </>}>
      <div className="col gap-3">
        <Field label="IP address"><input className="input mono" value={address} onChange={e => setAddress(e.target.value)} placeholder="192.0.2.10" autoFocus /></Field>
        <Field><label className="row gap-2" style={{ cursor: 'pointer' }}><input type="checkbox" checked={isShared} onChange={e => setIsShared(e.target.checked)} />Shared between subscriptions</label></Field>
        <Field label="Note"><input className="input" value={note} onChange={e => setNote(e.target.value)} placeholder="optional" /></Field>
      </div>
    </Dialog>
  );
}

function BlockIp({ open, onClose, onSaved }: { open: boolean; onClose: () => void; onSaved: () => void }) {
  const [address, setAddress] = useState('');
  const [reason, setReason] = useState('');
  const [expiresInHours, setExpires] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  useEffect(() => { if (open) { setAddress(''); setReason(''); setExpires(''); setErr(null); } }, [open]);
  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      await api.post('/ip/blacklist', { address, reason, expiresInHours: expiresInHours ? Number(expiresInHours) : null });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Block failed');
    } finally { setBusy(false); }
  };
  return (
    <Dialog open={open} onClose={onClose} title="Block IP"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn danger" onClick={submit} disabled={busy || !address}>{busy ? 'Blocking…' : 'Block'}</button>
      </>}>
      <div className="col gap-3">
        <Field label="IP / CIDR"><input className="input mono" value={address} onChange={e => setAddress(e.target.value)} placeholder="203.0.113.42" autoFocus /></Field>
        <Field label="Reason"><input className="input" value={reason} onChange={e => setReason(e.target.value)} placeholder="brute-force attempts" /></Field>
        <Field label="Expires in (hours)" hint="Leave blank for permanent."><input className="input mono" value={expiresInHours} onChange={e => setExpires(e.target.value)} placeholder="24" /></Field>
      </div>
    </Dialog>
  );
}
