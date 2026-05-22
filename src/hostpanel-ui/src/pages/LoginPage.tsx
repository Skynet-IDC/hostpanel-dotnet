import { useEffect, useState, type FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { useBranding } from '../contexts/BrandingContext';
import { login } from '../api/auth';
import api from '../api/client';
import { Icons } from '../components/ui/Icons';

interface SsoConfig { enabled: boolean; provider: string; loginUrl: string | null; }
interface BootstrapStatus { adminExists: boolean; brandingConfigured: boolean; }

export function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [passwordConfirm, setPasswordConfirm] = useState('');
  const [totp, setTotp] = useState('');
  const [requires2fa, setRequires2fa] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [sso, setSso] = useState<SsoConfig | null>(null);
  const [bootstrap, setBootstrap] = useState<BootstrapStatus | null>(null);
  const { login: setAuth } = useAuth();
  const { panelName, panelTagline, logoUrl } = useBranding();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  // SSO error surfacing — Authentik callback redirects here with ?error=
  useEffect(() => {
    const err = searchParams.get('error');
    if (err === 'sso_failed') setError('SSO sign-in failed. Try again or use email + password.');
    else if (err === 'no_email') setError('Your SSO provider did not return an email address.');
    else if (err === 'account_disabled') setError('Your account is disabled. Contact the administrator.');
    else if (err === 'provision_failed') setError('Could not provision your account from SSO. Contact the administrator.');
  }, [searchParams]);

  useEffect(() => {
    let alive = true;
    api.get<SsoConfig>('/sso/config').then(r => { if (alive) setSso(r.data); }).catch(() => { if (alive) setSso({ enabled: false, provider: '', loginUrl: null }); });
    api.get<BootstrapStatus>('/setup/bootstrap-status').then(r => { if (alive) setBootstrap(r.data); }).catch(() => { if (alive) setBootstrap({ adminExists: true, brandingConfigured: true }); });
    return () => { alive = false; };
  }, []);

  const isBootstrap = bootstrap?.adminExists === false;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      if (isBootstrap) {
        if (password.length < 10) { setError('Password must be at least 10 characters'); setLoading(false); return; }
        if (password !== passwordConfirm) { setError('Passwords do not match'); setLoading(false); return; }
        const r = await api.post<{ token: string; user: import('../types/auth').User }>('/setup/bootstrap-admin', { email, password });
        setAuth(r.data.token, r.data.user);
        // Local refresh so subsequent /setup/bootstrap-status reflects new state on next visit
        setBootstrap({ adminExists: true, brandingConfigured: bootstrap?.brandingConfigured ?? false });
        navigate('/');
        return;
      }
      const res = await login(requires2fa ? { email, password, totp } : { email, password });
      setAuth(res.token, res.user);
      navigate('/');
    } catch (e) {
      const data = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { error?: string; errors?: string[]; message?: string; requires2fa?: boolean } } }).response?.data
        : null;
      // UI-BUG-014: backend signals "show the TOTP prompt" via requires2fa=true.
      if (data?.requires2fa) {
        setRequires2fa(true);
        // If the user already submitted a code that failed, surface that; otherwise
        // suppress the misleading "Invalid email or password" message.
        setError(totp ? (data.message || 'Invalid TOTP code') : '');
      } else {
        setError(
          (data?.errors && data.errors[0]) ||
          data?.error ||
          data?.message ||
          (isBootstrap ? 'Admin creation failed — try a stronger password' : 'Invalid email or password')
        );
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div
      style={{
        minHeight: '100vh',
        background: 'var(--bg)',
        display: 'grid',
        placeItems: 'center',
        padding: 24,
        backgroundImage: 'radial-gradient(circle at 20% 0%, color-mix(in oklch, var(--primary-700) 30%, transparent), transparent 60%)',
      }}
    >
      <div
        className="card"
        style={{ width: '100%', maxWidth: 400, padding: 0, boxShadow: 'var(--shadow-lg)' }}
      >
        <div style={{ padding: 32, textAlign: 'center', borderBottom: '1px solid var(--border)' }}>
          <div
            style={{
              width: 56,
              height: 56,
              borderRadius: 14,
              background: 'linear-gradient(135deg, var(--primary-400), var(--primary-700))',
              display: 'grid',
              placeItems: 'center',
              margin: '0 auto 16px',
              color: 'white',
              boxShadow: '0 8px 24px color-mix(in oklch, var(--primary-500) 50%, transparent)',
            }}
          >
            {logoUrl
              ? <img src={logoUrl} alt="" style={{ width: 36, height: 36, borderRadius: 6 }} />
              : <svg viewBox="0 0 24 24" width={28} height={28} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                <circle cx="12" cy="12" r="4" />
                <path d="M12 4v4M12 16v4M4 12h4M16 12h4M6.3 6.3l2.8 2.8M14.9 14.9l2.8 2.8M17.7 6.3l-2.8 2.8M9.1 14.9 6.3 17.7" />
              </svg>}
          </div>
          <div style={{ fontSize: 22, fontWeight: 700, letterSpacing: '-0.01em' }}>{panelName}</div>
          {panelTagline && <div className="muted" style={{ fontSize: 13, marginTop: 4 }}>{panelTagline}</div>}
          {isBootstrap && (
            <div className="muted" style={{ fontSize: 12, marginTop: 12, padding: 10, background: 'color-mix(in oklch, var(--primary-500) 8%, transparent)', borderRadius: 8 }}>
              <strong>First-time setup.</strong> Create your admin account to take ownership of this panel.
            </div>
          )}
        </div>

        <form onSubmit={submit} style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 14 }}>
          {error && (
            <div className="alert danger"><Icons.alert />{error}</div>
          )}
          <div className="field">
            <label htmlFor="email">Email</label>
            <input
              id="email"
              className="input mono"
              type="email"
              autoComplete={isBootstrap ? 'email' : 'username'}
              autoFocus
              value={email}
              onChange={e => setEmail(e.target.value)}
              required
              placeholder="you@example.com"
            />
          </div>
          <div className="field">
            <label htmlFor="password">{isBootstrap ? 'Choose a password (min 10 chars)' : 'Password'}</label>
            <input
              id="password"
              className="input"
              type="password"
              autoComplete={isBootstrap ? 'new-password' : 'current-password'}
              value={password}
              onChange={e => setPassword(e.target.value)}
              required
              minLength={isBootstrap ? 10 : undefined}
            />
          </div>
          {isBootstrap && (
            <div className="field">
              <label htmlFor="password-confirm">Confirm password</label>
              <input
                id="password-confirm"
                className="input"
                type="password"
                autoComplete="new-password"
                value={passwordConfirm}
                onChange={e => setPasswordConfirm(e.target.value)}
                required
              />
            </div>
          )}
          {requires2fa && !isBootstrap && (
            <div className="field">
              <label htmlFor="totp">Two-factor code</label>
              <input
                id="totp"
                className="input mono"
                type="text"
                inputMode="numeric"
                autoComplete="one-time-code"
                autoFocus
                value={totp}
                onChange={e => setTotp(e.target.value.trim())}
                required
                placeholder="6-digit code or recovery code"
              />
            </div>
          )}
          <button type="submit" className="btn primary" disabled={loading} style={{ marginTop: 4 }}>
            {loading
              ? (isBootstrap ? 'Creating…' : 'Signing in…')
              : (isBootstrap ? 'Create admin account' : (requires2fa ? 'Verify and sign in' : 'Sign in'))}
          </button>

          {!isBootstrap && sso?.enabled && sso.loginUrl && (
            <>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, margin: '6px 0', color: 'var(--muted)', fontSize: 11 }}>
                <div style={{ flex: 1, height: 1, background: 'var(--border)' }} />
                or
                <div style={{ flex: 1, height: 1, background: 'var(--border)' }} />
              </div>
              <a
                className="btn ghost"
                href={sso.loginUrl}
                style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 8, textDecoration: 'none' }}
              >
                <Icons.key /> Sign in with {sso.provider}
              </a>
            </>
          )}
        </form>

        <div style={{ padding: '12px 24px 24px', textAlign: 'center', fontSize: 11, color: 'var(--muted)' }}>
          By signing in you agree to the panel's terms of service.
        </div>
      </div>
    </div>
  );
}
