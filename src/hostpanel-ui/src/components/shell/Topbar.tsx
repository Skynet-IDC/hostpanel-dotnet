import { useLocation } from 'react-router-dom';
import { useBranding } from '../../contexts/BrandingContext';
import { useAuth } from '../../contexts/AuthContext';
import { Icons } from '../ui/Icons';
import { initials } from '../ui/primitives';
import { navItemByPath } from './nav';

export function Topbar({ onMenu }: { onMenu?: () => void }) {
  const { panelName, role, theme, setBranding } = useBranding();
  const { user } = useAuth();
  const location = useLocation();

  const item = navItemByPath(location.pathname);
  const crumbs = item ? [panelName, item.label] : [panelName];

  const displayName = user
    ? [user.firstName, user.lastName].filter(Boolean).join(' ').trim() || user.email
    : 'Account';
  const avatarLabel = initials(displayName);

  const toggleTheme = () => setBranding({ theme: theme === 'dark' ? 'light' : 'dark' });

  return (
    <header className="topbar">
      {onMenu && (
        <button className="btn ghost icon sm mobile-menu-btn" onClick={onMenu} title="Menu" aria-label="Open menu">
          <Icons.menu />
        </button>
      )}
      <div className="crumbs" aria-label="Breadcrumb">
        {crumbs.map((c, i) => (
          <span key={i} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            {i > 0 && <Icons.chevR className="sep" style={{ width: 12, height: 12 }} />}
            <span className="crumb">{c}</span>
          </span>
        ))}
      </div>

      <div className="search" role="search" tabIndex={0}>
        <Icons.search style={{ width: 14, height: 14 }} />
        <span>Jump to anything…</span>
        <kbd>⌘K</kbd>
      </div>

      <button
        className="btn ghost icon sm"
        title={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
        onClick={toggleTheme}
        aria-label="Toggle theme"
      >
        {theme === 'dark' ? <Icons.zap /> : <Icons.cloud />}
      </button>
      <button className="btn ghost icon sm" title="Notifications" aria-label="Notifications"><Icons.bell /></button>

      <div className="user-pill" title={`${displayName} · ${role}`}>
        <div className="avatar">{avatarLabel}</div>
        <div style={{ lineHeight: 1.15 }}>
          <div style={{ fontWeight: 600, fontSize: 12 }}>{displayName}</div>
          <div style={{ fontSize: 10.5, color: 'var(--muted)', textTransform: 'capitalize' }}>{role}</div>
        </div>
        <Icons.chevD style={{ width: 12, height: 12, color: 'var(--muted)' }} />
      </div>
    </header>
  );
}
