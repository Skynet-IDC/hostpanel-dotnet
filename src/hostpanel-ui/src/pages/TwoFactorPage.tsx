import { useEffect, useState } from 'react';
import api from '../api/client';
import { Card, Empty, Field, PageHead, Stat } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface TwoFaStatus {
  enabled: boolean;
  enrolledAt?: string | null;
  recoveryCodesIssued?: number;
}

interface SetupResponse {
  secret: string;
  qrCodeUri: string;
  recoveryCodes?: string[];
}

export function TwoFactorPage() {
  const [status, setStatus] = useState<TwoFaStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [setupData, setSetupData] = useState<SetupResponse | null>(null);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);

  const load = async () => {
    setErr(null);
    try {
      const { data } = await api.get<TwoFaStatus>('/auth/2fa');
      setStatus(data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load 2FA status');
    } finally { setLoading(false); }
  };

  useEffect(() => { void load(); }, []);

  const beginSetup = async () => {
    setBusy(true); setErr(null);
    try {
      const { data } = await api.post<SetupResponse>('/auth/2fa/setup');
      setSetupData(data);
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Setup failed');
    } finally { setBusy(false); }
  };

  const verify = async () => {
    setBusy(true); setErr(null);
    try {
      await api.post('/auth/2fa/verify', { code });
      setSetupData(null);
      setCode('');
      await load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Verification failed');
    } finally { setBusy(false); }
  };

  const disable = async () => {
    if (!confirm('Disable two-factor authentication? Your account will rely on password only.')) return;
    setBusy(true); setErr(null);
    try {
      await api.delete('/auth/2fa');
      await load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Disable failed');
    } finally { setBusy(false); }
  };

  return (
    <>
      <PageHead title="Two-factor authentication" sub="TOTP via any authenticator app (Authy, 1Password, Google Authenticator)" />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {loading ? (
        <Card><div className="muted" style={{ padding: 8 }}>Loading…</div></Card>
      ) : setupData ? (
        <Card title="Set up 2FA" sub="Scan the QR or enter the secret in your authenticator, then enter the 6-digit code it generates.">
          <div className="col gap-4">
            <div className="alert info">
              <Icons.info />
              <div>
                <div style={{ fontWeight: 600 }}>Manual entry secret:</div>
                <div className="mono" style={{ marginTop: 4, fontSize: 13 }}>{setupData.secret}</div>
              </div>
            </div>
            {setupData.recoveryCodes && setupData.recoveryCodes.length > 0 && (
              <Card title="Recovery codes" sub="Save these somewhere safe. Each works once if you lose your authenticator.">
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 6, fontFamily: 'var(--font-mono)', fontSize: 12 }}>
                  {setupData.recoveryCodes.map(c => <div key={c} style={{ padding: '4px 8px', background: 'var(--bg-2)', borderRadius: 4 }}>{c}</div>)}
                </div>
              </Card>
            )}
            <Field label="6-digit code from your authenticator">
              <input className="input mono" inputMode="numeric" maxLength={6} value={code} onChange={e => setCode(e.target.value.replace(/\D/g, ''))} autoFocus placeholder="000000" style={{ maxWidth: 160 }} />
            </Field>
            <div className="row gap-2">
              <button className="btn ghost" onClick={() => { setSetupData(null); setCode(''); }}>Cancel setup</button>
              <button className="btn primary" onClick={verify} disabled={busy || code.length !== 6}>{busy ? 'Verifying…' : 'Verify + enable'}</button>
            </div>
          </div>
        </Card>
      ) : status?.enabled ? (
        <>
          <div className="stats" style={{ marginBottom: 16 }}>
            <Stat label="Status" icon="shieldCheck" value="Enabled" foot={<span className="muted">required at every sign-in</span>} />
            <Stat label="Enrolled" icon="history" value={status.enrolledAt ? new Date(status.enrolledAt).toLocaleDateString() : '—'} />
            <Stat label="Recovery codes" icon="key" value={String(status.recoveryCodesIssued ?? 0)} foot={<span className="muted">remaining</span>} />
          </div>
          <Card title="Disable" sub="Removing 2FA leaves your account password-only. Don't do this on a shared machine.">
            <button className="btn danger" onClick={disable} disabled={busy}><Icons.lockOpen />Disable 2FA</button>
          </Card>
        </>
      ) : (
        <Card>
          <Empty
            icon="key"
            title="2FA isn't on yet"
            sub="Add a second factor to protect your account. Takes 30 seconds — scan a QR with your phone, enter the code, done."
            action={<button className="btn primary" style={{ marginTop: 4 }} onClick={beginSetup} disabled={busy}><Icons.shield />{busy ? 'Generating…' : 'Set up 2FA'}</button>}
          />
        </Card>
      )}
    </>
  );
}
