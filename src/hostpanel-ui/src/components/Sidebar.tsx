import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { useTheme } from '../contexts/ThemeContext';

const navSections = [
  {
    title: 'Overview',
    items: [
      { path: '/', label: 'Dashboard' },
      { path: '/analytics', label: 'Analytics' },
    ]
  },
  {
    title: 'Hosting',
    items: [
      { path: '/domains', label: 'Domains' },
      { path: '/files', label: 'File Manager' },
      { path: '/databases', label: 'Databases' },
      { path: '/ssl', label: 'SSL Certificates' },
      { path: '/dns', label: 'DNS Zones' },
      { path: '/ftp', label: 'FTP Accounts' },
      { path: '/backups', label: 'Backups' },
    ]
  },
  {
    title: 'Email',
    items: [
      { path: '/email', label: 'Email Accounts' },
    ]
  },
  {
    title: 'Applications',
    items: [
      { path: '/wordpress', label: 'WordPress' },
      { path: '/apps', label: 'App Installer' },
      { path: '/docker', label: 'Docker' },
      { path: '/git', label: 'Git Deploy' },
    ]
  },
  {
    title: 'Server',
    items: [
      { path: '/firewall', label: 'Firewall' },
      { path: '/cron', label: 'Cron Jobs' },
      { path: '/security', label: 'Security' },
      { path: '/updates', label: 'Updates' },
      { path: '/ip', label: 'IP Management' },
      { path: '/ha', label: 'High Availability' },
    ]
  },
  {
    title: 'Business',
    items: [
      { path: '/customers', label: 'Customers' },
      { path: '/billing', label: 'Billing' },
      { path: '/audit', label: 'Audit Log' },
      { path: '/settings', label: 'Settings' },
    ]
  },
];

export function Sidebar() {
  const location = useLocation();
  const { user, logout } = useAuth();
  const { theme, toggleTheme } = useTheme();
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <>
      {/* Mobile hamburger */}
      <button onClick={() => setMobileOpen(!mobileOpen)}
        className="md:hidden fixed top-4 left-4 z-50 bg-gray-900 text-white p-2 rounded">
        {mobileOpen ? 'X' : '='}
      </button>

      <aside className={`w-64 bg-gray-900 dark:bg-gray-950 text-white min-h-screen flex flex-col fixed md:static z-40 transition-transform ${mobileOpen ? 'translate-x-0' : '-translate-x-full md:translate-x-0'}`}>
      <div className="p-6 border-b border-gray-700">
        <h1 className="text-xl font-bold">HostPanel</h1>
        <p className="text-sm text-gray-400 mt-1">{user?.email}</p>
        <span className="text-xs bg-blue-600 px-2 py-0.5 rounded mt-1 inline-block">
          {user?.role}
        </span>
      </div>

      <nav className="flex-1 p-4 overflow-y-auto">
        {navSections.map((section) => (
          <div key={section.title} className="mb-4">
            <p className="text-xs text-gray-500 uppercase tracking-wider px-4 mb-1">{section.title}</p>
            <ul className="space-y-0.5">
              {section.items.map((item) => (
                <li key={item.path}>
                  <Link
                    to={item.path}
                    className={`block px-4 py-1.5 rounded text-sm transition-colors ${
                      location.pathname === item.path
                        ? 'bg-blue-600 text-white'
                        : 'text-gray-300 hover:bg-gray-800 hover:text-white'
                    }`}
                  >
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>

      <div className="p-4 border-t border-gray-700 space-y-2">
        <button
          onClick={toggleTheme}
          className="w-full px-4 py-2 text-sm text-gray-300 hover:bg-gray-800 hover:text-white rounded transition-colors"
        >
          {theme === 'light' ? 'Dark Mode' : 'Light Mode'}
        </button>
        <button
          onClick={logout}
          className="w-full px-4 py-2 text-sm text-gray-300 hover:bg-gray-800 hover:text-white rounded transition-colors"
        >
          Sign Out
        </button>
      </div>
    </aside>

    {/* Mobile overlay */}
    {mobileOpen && <div className="fixed inset-0 bg-black/50 z-30 md:hidden" onClick={() => setMobileOpen(false)} />}
    </>
  );
}
