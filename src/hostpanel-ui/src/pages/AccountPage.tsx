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

  useEffect(() => {
    // Sync if Claude page changed the key
    const id = setInterval(() => setHasClaudeKey(!!localStorage.getItem('hp_claude_api_key')), 2000);
    return () => clearInterval(id);
  }, []);

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
