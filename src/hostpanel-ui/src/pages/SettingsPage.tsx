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

      <PayPalCredentialsCard />

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

/**
 * Phase A: PayPal credentials editor. Backend (PaymentProviderConfigController)
 * stores creds in the DB with IDataProtector-encrypted secrets. We validate
 * against PayPal's /v1/oauth2/token BEFORE persisting so the operator gets
 * immediate "Connected" feedback instead of finding out at first invoice creation.
 */
interface PaymentProviderRow {
  id: number;
  provider: string;
  sandbox: boolean;
  clientId: string;
  secret: string;        // always masked from the API
  webhookId: string | null;
  enabled: boolean;
  lastValidatedAt: string | null;
  lastValidationMessage: string | null;
}

function PayPalCredentialsCard() {
  const [rows, setRows] = useState<PaymentProviderRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [editOpen, setEditOpen] = useState(false);

  const load = async () => {
    setLoading(true); setErr(null);
    try {
      const { data } = await api.get<PaymentProviderRow[]>('/settings/billing');
      setRows(data.filter(r => r.provider === 'PayPal'));
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Load failed');
    } finally { setLoading(false); }
  };
  useEffect(() => { void load(); }, []);

  const remove = async (id: number, sandbox: boolean) => {
    if (!confirm(`Delete the ${sandbox ? 'sandbox' : 'live'} PayPal credentials? Invoice creation via PayPal stops until you re-add them.`)) return;
    await api.delete(`/settings/billing/${id}`);
    void load();
  };

  return (
    <>
      <Card
        title="PayPal credentials"
        sub="Stored in DB with encrypted secret. Validates against PayPal's OAuth endpoint before saving so you know immediately if the credentials are wrong."
        flush
        actions={<button className="btn primary" onClick={() => setEditOpen(true)}>
          <Icons.plus />{rows.length > 0 ? 'Add / update' : 'Configure PayPal'}
        </button>}
      >
        {err && <div className="alert danger" style={{ margin: 12 }}><Icons.alert />{err}</div>}
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : rows.length === 0 ? <Empty icon="key" title="PayPal not configured" sub="Add credentials so invoice generation can produce real PayPal-hosted payment links." />
          : (
            <table className="tbl">
              <thead><tr><th>Mode</th><th>Client ID</th><th>Secret</th><th>Webhook ID</th><th>Status</th><th>Last validated</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {rows.map(r => (
                  <tr key={r.id}>
                    <td>{r.sandbox ? <Badge>Sandbox</Badge> : <Badge tone="primary">Live</Badge>} {r.enabled && <Badge tone="success">Active</Badge>}</td>
                    <td className="mono" style={{ fontSize: 11.5 }}>{r.clientId.slice(0, 24)}{r.clientId.length > 24 ? '…' : ''}</td>
                    <td className="muted mono" style={{ fontSize: 11.5 }}>{r.secret}</td>
                    <td className="mono muted" style={{ fontSize: 11.5 }}>{r.webhookId ?? '—'}</td>
                    <td title={r.lastValidationMessage ?? ''}>
                      {r.lastValidatedAt ? <Badge tone="success">Validated</Badge> : <Badge tone="danger">Not validated</Badge>}
                    </td>
                    <td className="muted mono" style={{ fontSize: 11.5 }}>{r.lastValidatedAt ? new Date(r.lastValidatedAt).toLocaleString() : '—'}</td>
                    <td><div className="row-actions"><IconBtn icon="trash" danger title="Delete" onClick={() => remove(r.id, r.sandbox)} /></div></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </Card>
      <PayPalCredentialsDialog open={editOpen} onClose={() => setEditOpen(false)} onSaved={() => { setEditOpen(false); void load(); }} />
    </>
  );
}

function PayPalCredentialsDialog({ open, onClose, onSaved }: { open: boolean; onClose: () => void; onSaved: () => void }) {
  const [clientId, setClientId] = useState('');
  const [clientSecret, setClientSecret] = useState('');
  const [webhookId, setWebhookId] = useState('');
  const [sandbox, setSandbox] = useState(true);
  const [enabled, setEnabled] = useState(true);
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<{ ok: boolean; msg: string; hint?: string } | null>(null);

  useEffect(() => {
    if (open) {
      setClientId(''); setClientSecret(''); setWebhookId('');
      setSandbox(true); setEnabled(true); setFeedback(null);
    }
  }, [open]);

  const submit = async (saveEvenIfInvalid: boolean) => {
    setBusy(true); setFeedback(null);
    try {
      const { data } = await api.put<{ ok: boolean; message?: string }>('/settings/billing/paypal', {
        clientId, clientSecret, sandbox,
        webhookId: webhookId || null,
        enabled, saveEvenIfInvalid
      });
      if (data.ok) {
        setFeedback({ ok: true, msg: data.message ?? 'Connected and saved.' });
        setTimeout(onSaved, 800);
      } else {
        setFeedback({ ok: false, msg: data.message ?? 'Validation failed.' });
      }
    } catch (e) {
      const resp = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string; hint?: string } } }).response : null;
      setFeedback({ ok: false, msg: resp?.data?.message ?? 'Save failed', hint: resp?.data?.hint });
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} title="PayPal credentials"
      foot={<>
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        {feedback && !feedback.ok && feedback.hint?.includes('saveEvenIfInvalid') && (
          <button className="btn" onClick={() => submit(true)} disabled={busy}>Save anyway</button>
        )}
        <button className="btn primary" onClick={() => submit(false)} disabled={busy || !clientId.trim() || !clientSecret.trim()}>
          {busy ? 'Validating…' : 'Test & save'}
        </button>
      </>}>
      <Field label="Client ID" hint="From developer.paypal.com → Apps & Credentials → your app.">
        <input className="input" autoFocus value={clientId} onChange={e => setClientId(e.target.value)} placeholder="A21AAxxx…" />
      </Field>
      <Field label="Client secret" hint="The 'Secret' shown beneath the Client ID in PayPal's dashboard.">
        <input className="input" type="password" value={clientSecret} onChange={e => setClientSecret(e.target.value)} placeholder="EHxxx…" />
      </Field>
      <Field label="Webhook ID (optional)" hint="From Apps & Credentials → your app → Webhooks. Required for webhook signature verification.">
        <input className="input" value={webhookId} onChange={e => setWebhookId(e.target.value)} placeholder="WH-xxx…" />
      </Field>
      <Field label="Environment">
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
          <input type="checkbox" checked={sandbox} onChange={e => setSandbox(e.target.checked)} />
          <span>Use sandbox API (uncheck for production)</span>
        </label>
      </Field>
      <Field label="Enable">
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
          <input type="checkbox" checked={enabled} onChange={e => setEnabled(e.target.checked)} />
          <span>Activate these credentials at runtime (only one per provider can be active)</span>
        </label>
      </Field>
      {feedback && (
        <div className={`alert ${feedback.ok ? 'success' : 'danger'}`} style={{ marginTop: 12 }}>
          {feedback.ok ? <Icons.check2 /> : <Icons.alert />}
          <div>
            <div style={{ fontWeight: 600 }}>{feedback.ok ? 'Validated' : 'Not validated'}</div>
            <div style={{ fontSize: 12, marginTop: 2 }}>{feedback.msg}</div>
            {feedback.hint && <div className="muted" style={{ fontSize: 11.5, marginTop: 4 }}>{feedback.hint}</div>}
          </div>
        </div>
      )}
    </Dialog>
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
