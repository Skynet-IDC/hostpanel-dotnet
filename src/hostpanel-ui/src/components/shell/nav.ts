import type { IconName } from '../ui/Icons';
import type { Role } from '../../contexts/BrandingContext';

export interface NavItem {
  id: string;
  label: string;
  icon: IconName;
  path: string;
  badge?: number;
  /** Restrict to specific roles. Omit = visible to all roles allowed by the group. */
  roles?: ReadonlyArray<Role>;
}

export interface NavGroup {
  label: string;
  roles: ReadonlyArray<Role>;
  items: ReadonlyArray<NavItem>;
}

/** Sidebar nav structure, mapped to react-router paths. */
export const NAV_GROUPS: ReadonlyArray<NavGroup> = [
  {
    label: 'Server', roles: ['admin'],
    items: [
      { id: 'dashboard',     label: 'Dashboard',     icon: 'dashboard', path: '/' },
      { id: 'customers',     label: 'Customers',     icon: 'users',     path: '/customers' },
      { id: 'plans',         label: 'Service plans', icon: 'package',   path: '/plans' },
      { id: 'subscriptions', label: 'Subscriptions', icon: 'layers',    path: '/subscriptions' },
    ],
  },
  {
    label: 'Reseller', roles: ['reseller'],
    items: [
      { id: 'dashboard',     label: 'Dashboard',     icon: 'dashboard', path: '/' },
      { id: 'customers',     label: 'My customers',  icon: 'users',     path: '/customers' },
      { id: 'plans',         label: 'My plans',      icon: 'package',   path: '/plans' },
      { id: 'subscriptions', label: 'Subscriptions', icon: 'layers',    path: '/subscriptions' },
    ],
  },
  {
    label: 'My site', roles: ['customer'],
    items: [
      { id: 'dashboard', label: 'Overview', icon: 'dashboard', path: '/' },
    ],
  },
  {
    label: 'This subscription', roles: ['admin', 'reseller', 'customer'],
    items: [
      { id: 'domains',    label: 'Domains',      icon: 'globe',     path: '/domains' },
      { id: 'files',      label: 'File manager', icon: 'folder',    path: '/files' },
      { id: 'ftp',        label: 'FTP accounts', icon: 'user',      path: '/ftp' },
      { id: 'email',      label: 'Email',        icon: 'mail',      path: '/email' },
      { id: 'databases',  label: 'Databases',    icon: 'database',  path: '/databases' },
      { id: 'ssl',        label: 'SSL / TLS',    icon: 'lock',      path: '/ssl' },
      { id: 'dns',        label: 'DNS records',  icon: 'dns',       path: '/dns' },
      { id: 'backups',    label: 'Backups',      icon: 'archive',   path: '/backups' },
      { id: 'wordpress',  label: 'WordPress',    icon: 'wordpress', path: '/wordpress' },
      { id: 'containers', label: 'Docker',       icon: 'docker',    path: '/docker' },
      { id: 'git',        label: 'Git deploys',  icon: 'git',       path: '/git' },
      { id: 'cron',       label: 'Cron jobs',    icon: 'clock',     path: '/cron' },
      { id: 'logs',       label: 'Domain logs',  icon: 'log',       path: '/logs' },
      { id: 'claude',     label: 'Claude Code',  icon: 'terminal',  path: '/claude' },
    ],
  },
  {
    label: 'Security', roles: ['admin'],
    items: [
      { id: 'firewall', label: 'Firewall',      icon: 'shield',      path: '/firewall' },
      { id: 'ipaccess', label: 'IP access',     icon: 'network',     path: '/ip' },
      { id: 'fail2ban', label: 'Fail2ban',      icon: 'shieldCheck', path: '/fail2ban' },
      { id: 'mfa',      label: 'Two-factor',    icon: 'key',         path: '/2fa' },
      { id: 'malware',  label: 'Malware scans', icon: 'bug',         path: '/malware' },
    ],
  },
  {
    label: 'System', roles: ['admin'],
    items: [
      { id: 'audit',     label: 'Audit log',       icon: 'log',      path: '/audit' },
      { id: 'webhooks',  label: 'Webhooks',        icon: 'webhook',  path: '/webhooks' },
      { id: 'updates',   label: 'Updates',         icon: 'refresh',  path: '/updates' },
      { id: 'servers',   label: 'Server fleet',    icon: 'network',  path: '/servers' },
      { id: 'migration', label: 'Plesk migration', icon: 'package',  path: '/migration' },
      { id: 'branding',  label: 'Branding',        icon: 'brush',    path: '/branding' },
      { id: 'settings',  label: 'Server settings', icon: 'settings', path: '/settings' },
    ],
  },
  {
    label: 'Account', roles: ['customer', 'reseller'],
    items: [
      { id: 'account', label: 'Profile',     icon: 'user',    path: '/account' },
      { id: 'mfa',     label: 'Security',    icon: 'key',     path: '/2fa' },
      { id: 'billing', label: 'Billing',     icon: 'package', path: '/billing' },
      { id: 'audit',   label: 'My activity', icon: 'log',     path: '/audit' },
    ],
  },
];

export const NAV_FLAT = NAV_GROUPS.flatMap(g =>
  g.items.map(i => ({ ...i, group: g.label } as NavItem & { group: string })),
);

export function navItemByPath(path: string) {
  return NAV_FLAT.find(n => n.path === path);
}

export function navItemById(id: string) {
  return NAV_FLAT.find(n => n.id === id);
}
