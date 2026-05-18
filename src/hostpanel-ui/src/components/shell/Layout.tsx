import { useState, type ReactNode } from 'react';
import { useBranding } from '../../contexts/BrandingContext';
import { Rail } from './Rail';
import { Topbar } from './Topbar';

export function Layout({ children }: { children: ReactNode }) {
  const { railCollapsed } = useBranding();
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <div
      className="app"
      data-rail={railCollapsed ? 'collapsed' : 'expanded'}
      data-mobile-drawer={mobileOpen ? 'open' : 'closed'}
    >
      <div className="rail-scrim" onClick={() => setMobileOpen(false)} aria-hidden />
      <Rail />
      <main className="main">
        <Topbar onMenu={() => setMobileOpen(true)} />
        <div className="page">{children}</div>
      </main>
    </div>
  );
}
