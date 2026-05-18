import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import api from '../api/client';
import { Badge, Card, Dialog, Empty, Field, IconBtn, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface ApiKey {
  id: number;
  name: string;
  prefix: string;
  createdAt: string;
  lastUsedAt: string | null;
}

export function SettingsPage() {
  const [keys, setKeys] = useState<ApiKey[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [revealed, setRevealed] = useState<string | null>(null);

  const load = async () => {
    setLoading(true); setErr(null);
    try {
      const { data } = await api.get<ApiKey[]>('/apikeys');
      setKeys(data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Load failed');
    } finally { setLoading(false); }
  };
  useEffect(() => { void load(); }, []);

  const revoke = async (id: number, name: string) => {
    if (!confirm(`Revoke API key "${name}"? Anything using it stops working immediately.`)) return;
    await api.delete(`/apikeys/${id}`);
    void load();
  };

  return (
    <>
      <PageHead title="Server settings" sub="Admin tools and integration tokens" />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <div style={{ display: 'grid', gap: 16, gridTemplateColumns: '1fr 1fr 1fr', marginBottom: 16 }}>
        <Link to="/branding" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}><Icons.brush style={{ color: 'var(--primary-400)' }} /><div style={{ fontWeight: 600 }}>Branding</div></div>
            <div className="muted" style={{ fontSize: 12 }}>Panel name, logo, theme color, footer text, login screen, custom CSS.</div>
          </div>
        </Link>
        <Link to="/webhooks" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}><Icons.webhook style={{ color: 'var(--primary-400)' }} /><div style={{ fontWeight: 600 }}>Webhooks</div></div>
            <div className="muted" style={{ fontSize: 12 }}>HMAC-signed event subscriptions for your external systems.</div>
          </div>
        </Link>
        <Link to="/2fa" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}><Icons.key style={{ color: 'var(--primary-400)' }} /><div style={{ fontWeight: 600 }}>Two-factor auth</div></div>
            <div className="muted" style={{ fontSize: 12 }}>TOTP setup, recovery codes, and disable flow for your admin account.</div>
          </div>
        </Link>
      </div>

      <Card
        title="API keys"
        sub="Bearer tokens for programmatic access. Use them in CI, MCPs, or webhooks."
        flush
        actions={<button className="btn primary" onClick={() => setCreateOpen(true)}><Icons.plus />New API key</button>}
      >
        {revealed && (
          <div className="alert success" style={{ margin: 12 }}>
            <Icons.check2 />
            <div>
              <div style={{ fontWeight: 600 }}>New API key — copy now, it won't be shown again.</div>
              <div className="mono" style={{ marginTop: 6, fontSize: 12, padding: 6, background: 'var(--bg)', borderRadius: 4, wordBreak: 'break-all' }}>{revealed}</div>
              <button className="btn ghost sm" style={{ marginTop: 8 }} onClick={() => setRevealed(null)}>Dismiss</button>
            </div>
          </div>
        )}
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : keys.length === 0 ? <Empty icon="key" title="No API keys" sub="Create one to access the panel from scripts, MCP clients, or CI." />
          : (
            <table className="tbl">
              <thead><tr><th>Name</th><th>Prefix</th><th>Created</th><th>Last used</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {keys.map(k => (
                  <tr key={k.id}>
                    <td style={{ fontWeight: 600 }}>{k.name}</td>
                    <td className="mono muted">{k.prefix}…</td>
                    <td className="muted mono" style={{ fontSize: 11.5 }}>{new Date(k.createdAt).toLocaleDateString()}</td>
                    <td className="muted mono" style={{ fontSize: 11.5 }}>{k.lastUsedAt ? new Date(k.lastUsedAt).toLocaleDateString() : <Badge>Never</Badge>}</td>
                    <td><div className="row-actions"><IconBtn icon="trash" danger title="Revoke" onClick={() => revoke(k.id, k.name)} /></div></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </Card>

      <CreateKeyDialog open={createOpen} onClose={() => setCreateOpen(false)} onCreated={key => { setCreateOpen(false); setRevealed(key); void load(); }} />
    </>
  );
}

function CreateKeyDialog({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (key: string) => void }) {
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  useEffect(() => { if (open) { setName(''); setErr(null); } }, [open]);
  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      const { data } = await api.post<{ key: string }>('/apikeys', { name });
      onCreated(data.key);
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Create failed');
    } finally { setBusy(false); }
  };
  return (
    <Dialog open={open} onClose={onClose} title="New API key"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !name.trim()}>{busy ? 'Creating…' : 'Create'}</button>
      </>}>
      <Field label="Key name" hint="What's this key for? You'll see this label in the keys list.">
        <input className="input" autoFocus value={name} onChange={e => setName(e.target.value)} placeholder="My CI deploy" />
      </Field>
    </Dialog>
  );
}
