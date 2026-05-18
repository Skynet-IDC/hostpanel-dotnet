import { useEffect, useState } from 'react';
import api from '../api/client';
import { Card, Dialog, Empty, Field, IconBtn, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';
import { panelApi, type PanelSubscription } from '../api/panel';

interface FtpAccount {
  id: number;
  username: string;
  homeDirectory?: string;
  quotaMb?: number;
}

export function FtpPage() {
  const [subs, setSubs] = useState<PanelSubscription[]>([]);
  const [subId, setSubId] = useState<number | null>(null);
  const [accounts, setAccounts] = useState<FtpAccount[]>([]);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    panelApi.listSubscriptions()
      .then(s => { setSubs(s); if (s.length > 0) setSubId(s[0].id); })
      .catch(e => setErr(e instanceof Error ? e.message : 'Load failed'))
      .finally(() => setLoading(false));
  }, []);

  const load = async () => {
    if (!subId) return;
    try {
      const { data } = await api.get<FtpAccount[]>(`/subscriptions/${subId}/ftp`);
      setAccounts(data);
    } catch { setAccounts([]); }
  };

  useEffect(() => { void load(); }, [subId]);

  const remove = async (id: number) => {
    if (!subId || !confirm('Delete this FTP account?')) return;
    await api.delete(`/subscriptions/${subId}/ftp/${id}`);
    void load();
  };

  return (
    <>
      <PageHead
        title="FTP accounts"
        sub="Tenant-scoped FTP via vsftpd"
        actions={
          <>
            {subs.length > 0 && (
              <select className="select" value={subId ?? ''} onChange={e => setSubId(Number(e.target.value))} style={{ maxWidth: 220 }}>
                {subs.map(s => <option key={s.id} value={s.id}>{s.systemUsername || s.domains?.[0]?.name || `Sub #${s.id}`}</option>)}
              </select>
            )}
            <button className="btn primary" onClick={() => setCreateOpen(true)} disabled={!subId}><Icons.plus />New FTP account</button>
          </>
        }
      />
      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}
      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : accounts.length === 0 ? <Empty icon="user" title="No FTP accounts" sub="Create a credential for upload-only access." />
          : (
            <table className="tbl">
              <thead><tr><th>Username</th><th>Home directory</th><th>Quota</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {accounts.map(a => (
                  <tr key={a.id}>
                    <td className="mono">{a.username}</td>
                    <td className="mono muted" style={{ fontSize: 11.5 }}>{a.homeDirectory ?? '—'}</td>
                    <td className="mono">{a.quotaMb != null ? `${a.quotaMb} MB` : '∞'}</td>
                    <td><div className="row-actions"><IconBtn icon="trash" danger title="Delete" onClick={() => remove(a.id)} /></div></td>
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
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [directory, setDirectory] = useState('httpdocs');
  const [quotaMb, setQuotaMb] = useState(500);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => { if (open) { setUsername(''); setPassword(''); setDirectory('httpdocs'); setQuotaMb(500); setErr(null); } }, [open]);

  const submit = async () => {
    if (!subId) return;
    setBusy(true); setErr(null);
    try {
      await api.post(`/subscriptions/${subId}/ftp`, { username, password, directory, quotaMb });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Create failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} title="New FTP account"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !username || !password}>{busy ? 'Creating…' : 'Create'}</button>
      </>}>
      <div className="grid-2" style={{ gap: 12 }}>
        <Field label="Username"><input className="input mono" value={username} onChange={e => setUsername(e.target.value)} autoFocus /></Field>
        <Field label="Password"><input className="input mono" type="password" value={password} onChange={e => setPassword(e.target.value)} /></Field>
        <Field label="Home directory"><input className="input mono" value={directory} onChange={e => setDirectory(e.target.value)} /></Field>
        <Field label="Quota (MB)"><input className="input mono" type="number" min={0} value={quotaMb} onChange={e => setQuotaMb(Number(e.target.value))} /></Field>
      </div>
    </Dialog>
  );
}
