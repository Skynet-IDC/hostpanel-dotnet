import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import api from '../api/client';
import { useAuth } from '../contexts/AuthContext';
import { Card, Empty, Field, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

export function AccountPage() {
  const { user } = useAuth();
  const [firstName, setFirstName] = useState(user?.firstName ?? '');
  const [lastName, setLastName] = useState(user?.lastName ?? '');
  const [companyName, setCompanyName] = useState(user?.companyName ?? '');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<number | null>(null);
  const [hasClaudeKey, setHasClaudeKey] = useState<boolean>(() => !!localStorage.getItem('hp_claude_api_key'));

  // Password form
  const [currentPwd, setCurrentPwd] = useState('');
  const [newPwd, setNewPwd] = useState('');
  const [confirmPwd, setConfirmPwd] = useState('');
  const [pwdBusy, setPwdBusy] = useState(false);
  const [pwdErr, setPwdErr] = useState<string | null>(null);
  const [pwdSavedAt, setPwdSavedAt] = useState<number | null>(null);

  // Personal branding form
  const [brandLogoUrl, setBrandLogoUrl] = useState('');
  const [brandPanelName, setBrandPanelName] = useState('');
  const [brandPrimaryColor, setBrandPrimaryColor] = useState('');
  const [brandLoginMessage, setBrandLoginMessage] = useState('');
  const [brandBusy, setBrandBusy] = useState(false);
  const [brandErr, setBrandErr] = useState<string | null>(null);
  const [brandSavedAt, setBrandSavedAt] = useState<number | null>(null);
  const [brandUploading, setBrandUploading] = useState(false);

  useEffect(() => {
    // Sync if Claude page changed the key
    const id = setInterval(() => setHasClaudeKey(!!localStorage.getItem('hp_claude_api_key')), 2000);
    return () => clearInterval(id);
  }, []);

  useEffect(() => {
    if (!user) return;
    (async () => {
      try {
        const r = await api.get<{ logoUrl?: string; panelName?: string; primaryColor?: string; loginMessage?: string }>('/branding/customer/me');
        setBrandLogoUrl(r.data.logoUrl ?? '');
        setBrandPanelName(r.data.panelName ?? '');
        setBrandPrimaryColor(r.data.primaryColor ?? '');
        setBrandLoginMessage(r.data.loginMessage ?? '');
      } catch { /* no override yet, leave defaults */ }
    })();
  }, [user]);

  if (!user) {
    return (
      <>
        <PageHead title="Profile" />
        <Card><Empty icon="user" title="Not signed in" sub="Please sign in to view your account." /></Card>
      </>
    );
  }

  const saveProfile = async () => {
    setBusy(true); setErr(null);
    try {
      await api.put(`/customers/${user.id}`, { firstName, lastName, company: companyName || null });
      setSavedAt(Date.now());
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Save failed');
    } finally { setBusy(false); }
  };

  const saveBranding = async () => {
    if (!user) return;
    setBrandBusy(true); setBrandErr(null);
    try {
      await api.put(`/branding/customer/${user.id}`, {
        logoUrl: brandLogoUrl || null,
        panelName: brandPanelName || null,
        primaryColor: brandPrimaryColor || null,
        loginMessage: brandLoginMessage || null,
      });
      setBrandSavedAt(Date.now());
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setBrandErr(msg ?? 'Save failed');
    } finally { setBrandBusy(false); }
  };

  const clearBranding = async () => {
    if (!user) return;
    if (!confirm('Reset your personal branding to defaults?')) return;
    setBrandBusy(true); setBrandErr(null);
    try {
      await api.delete(`/branding/customer/${user.id}`);
      setBrandLogoUrl(''); setBrandPanelName(''); setBrandPrimaryColor(''); setBrandLoginMessage('');
      setBrandSavedAt(Date.now());
    } catch (e) {
      setBrandErr(e instanceof Error ? e.message : 'Reset failed');
    } finally { setBrandBusy(false); }
  };

  const uploadLogo = async (file: File) => {
    if (!user) return;
    if (file.size > 5 * 1024 * 1024) { setBrandErr('Logo must be under 5 MB'); return; }
    setBrandUploading(true); setBrandErr(null);
    try {
      const fd = new FormData();
      fd.append('file', file);
      const r = await api.post<{ url: string }>(`/branding/customer/${user.id}/logo`, fd, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      setBrandLogoUrl(r.data.url);
      setBrandSavedAt(Date.now());
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setBrandErr(msg ?? 'Upload failed');
    } finally { setBrandUploading(false); }
  };

  const changePassword = async () => {
    if (newPwd !== confirmPwd) { setPwdErr('Passwords do not match'); return; }
    if (newPwd.length < 12) { setPwdErr('New password must be at least 12 characters'); return; }
    setPwdBusy(true); setPwdErr(null);
    try {
      await api.post('/auth/change-password', { currentPassword: currentPwd, newPassword: newPwd });
      setCurrentPwd(''); setNewPwd(''); setConfirmPwd('');
      setPwdSavedAt(Date.now());
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setPwdErr(msg ?? 'Password change failed');
    } finally { setPwdBusy(false); }
  };

  return (
    <>
      <PageHead title="Profile" sub={`Signed in as ${user.email}`} />

      <div style={{ display: 'grid', gap: 16, gridTemplateColumns: '1fr 1fr' }}>
        <Card title="Personal info" actions={savedAt && Date.now() - savedAt < 4000 ? <span className="badge success"><span className="dot" />Saved</span> : null}>
          {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}
          <div className="col gap-3">
            <Field label="Email"><input className="input mono" type="email" value={user.email} disabled /></Field>
            <Field label="First name"><input className="input" value={firstName} onChange={e => setFirstName(e.target.value)} /></Field>
            <Field label="Last name"><input className="input" value={lastName} onChange={e => setLastName(e.target.value)} /></Field>
            <Field label="Company (optional)"><input className="input" value={companyName} onChange={e => setCompanyName(e.target.value)} /></Field>
            <button className="btn primary" onClick={saveProfile} disabled={busy} style={{ alignSelf: 'flex-start' }}>
              {busy ? 'Saving…' : 'Save profile'}
            </button>
          </div>
        </Card>

        <Card title="Change password" actions={pwdSavedAt && Date.now() - pwdSavedAt < 4000 ? <span className="badge success"><span className="dot" />Updated</span> : null}>
          {pwdErr && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{pwdErr}</div>}
          <div className="col gap-3">
            <Field label="Current password"><input className="input mono" type="password" value={currentPwd} onChange={e => setCurrentPwd(e.target.value)} autoComplete="current-password" /></Field>
            <Field label="New password" hint="At least 12 characters."><input className="input mono" type="password" value={newPwd} onChange={e => setNewPwd(e.target.value)} autoComplete="new-password" /></Field>
            <Field label="Confirm new password"><input className="input mono" type="password" value={confirmPwd} onChange={e => setConfirmPwd(e.target.value)} autoComplete="new-password" /></Field>
            <button className="btn primary" onClick={changePassword} disabled={pwdBusy || !currentPwd || !newPwd} style={{ alignSelf: 'flex-start' }}>
              {pwdBusy ? 'Updating…' : 'Update password'}
            </button>
          </div>
        </Card>
      </div>

      <Card title="Personal branding" sub="Override the panel's logo, name, colors, and login message just for your account."
        actions={brandSavedAt && Date.now() - brandSavedAt < 4000 ? <span className="badge success"><span className="dot" />Saved</span> : null}
        style={{ marginTop: 16 }}>
        {brandErr && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{brandErr}</div>}
        <div style={{ display: 'grid', gap: 12, gridTemplateColumns: '1fr 1fr' }}>
          <Field label="Logo URL" hint="Or upload a file below — 5 MB max.">
            <input className="input mono" value={brandLogoUrl} onChange={e => setBrandLogoUrl(e.target.value)} placeholder="https://…" />
          </Field>
          <Field label="Upload logo">
            <input type="file" accept="image/png,image/jpeg,image/svg+xml,image/webp" disabled={brandUploading}
              onChange={e => { const f = e.target.files?.[0]; if (f) void uploadLogo(f); }} />
            {brandUploading && <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>Uploading…</div>}
          </Field>
          <Field label="Panel name override">
            <input className="input" value={brandPanelName} onChange={e => setBrandPanelName(e.target.value)} placeholder="My Company Portal" />
          </Field>
          <Field label="Primary color">
            <input className="input mono" value={brandPrimaryColor} onChange={e => setBrandPrimaryColor(e.target.value)} placeholder="#6366f1" />
          </Field>
          <Field label="Login message" hint="Shown on the sign-in page when someone logs in with your link.">
            <textarea className="input" rows={2} value={brandLoginMessage} onChange={e => setBrandLoginMessage(e.target.value)} />
          </Field>
          {brandLogoUrl && (
            <Field label="Preview">
              <div style={{ padding: 8, border: '1px solid var(--border)', borderRadius: 6, background: '#0001' }}>
                <img src={brandLogoUrl} alt="logo preview" style={{ maxHeight: 64, maxWidth: '100%' }} />
              </div>
            </Field>
          )}
        </div>
        <div className="row gap-2" style={{ marginTop: 12 }}>
          <button className="btn primary" onClick={saveBranding} disabled={brandBusy}>{brandBusy ? 'Saving…' : 'Save branding'}</button>
          <button className="btn ghost" onClick={clearBranding} disabled={brandBusy}>Reset to defaults</button>
        </div>
      </Card>

      <div style={{ display: 'grid', gap: 16, gridTemplateColumns: '1fr 1fr', marginTop: 16 }}>
        <Link to="/2fa" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}>
              <Icons.key style={{ color: 'var(--primary-400)' }} />
              <div style={{ fontWeight: 600 }}>Two-factor authentication</div>
            </div>
            <div className="muted" style={{ fontSize: 12 }}>Add a second factor to your sign-in. TOTP via Authy / 1Password / Google Authenticator.</div>
          </div>
        </Link>

        <Link to="/claude" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}>
              <Icons.terminal style={{ color: 'var(--primary-400)' }} />
              <div style={{ fontWeight: 600 }}>Claude Code</div>
              <span className={`badge ${hasClaudeKey ? 'success' : ''}`} style={{ marginLeft: 'auto' }}>{hasClaudeKey ? <><span className="dot" />Key set</> : 'No key'}</span>
            </div>
            <div className="muted" style={{ fontSize: 12 }}>BYO Anthropic API key (stored in this browser only) and chat with Claude inside your sandbox.</div>
          </div>
        </Link>
      </div>
    </>
  );
}
