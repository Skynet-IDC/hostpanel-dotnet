import { NavLink } from 'react-router-dom';
import { useBranding } from '../../contexts/BrandingContext';
import { Icons } from '../ui/Icons';
import { NAV_GROUPS } from './nav';

function BrandGlyph() {
  return (
    <svg viewBox="0 0 24 24" width={16} height={16} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 4v4M12 16v4M4 12h4M16 12h4M6.3 6.3l2.8 2.8M14.9 14.9l2.8 2.8M17.7 6.3l-2.8 2.8M9.1 14.9 6.3 17.7" />
    </svg>
  );
}

export function Rail() {
  const { panelName, role, railCollapsed, setBranding, logoUrl } = useBranding();

  return (
    <aside className="rail" data-collapsed={railCollapsed}>
      <div className="rail-head">
        <div className="rail-logo">
          {logoUrl
            ? <img src={logoUrl} alt="" style={{ width: 22, height: 22, borderRadius: 4 }} />
            : <BrandGlyph />}
        </div>
        <div className="rail-name">{panelName}</div>
        <button
          className="btn ghost icon sm"
          style={{ marginLeft: 'auto' }}
          onClick={() => setBranding({ railCollapsed: !railCollapsed })}
          title={railCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        >
          <Icons.panelLeft />
        </button>
      </div>

      <nav className="nav-scroll" aria-label="Primary">
        {NAV_GROUPS.map(g => {
          if (!g.roles.includes(role)) return null;
          const items = g.items.filter(it => !it.roles || it.roles.includes(role));
          if (items.length === 0) return null;
          return (
            <div className="nav-group" key={g.label}>
              <div className="group-label">{g.label}</div>
              {items.map(it => {
                const Icon = Icons[it.icon];
                return (
                  <NavLink
                    key={`${g.label}:${it.id}`}
                    to={it.path}
                    end={it.path === '/'}
                    className={({ isActive }) => `nav-item${isActive ? ' is-active' : ''}`}
                    title={it.label}
                  >
                    <Icon />
                    <span className="rail-label">{it.label}</span>
                    {it.badge != null && <span className="nav-badge">{it.badge}</span>}
                  </NavLink>
                );
              })}
            </div>
          );
        })}
      </nav>
    </aside>
  );
}
