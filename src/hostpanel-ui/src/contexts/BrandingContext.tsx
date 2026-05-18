import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import api from '../api/client';
import { hexToHue, hueToHex } from '../styles/colorUtils';

export type Theme = 'light' | 'dark';
export type Density = 'compact' | 'comfy' | 'spacious';
export type Role = 'admin' | 'reseller' | 'customer';

export interface Branding {
  panelName: string;
  panelTagline: string;
  primaryHue: number;
  logoUrl: string | null;
  supportEmail: string;
  theme: Theme;
  density: Density;
  role: Role;
  railCollapsed: boolean;
  /** True once admin has finished the first-launch wizard. */
  configured: boolean;
}

const DEFAULT_HUE = 268;

const DEFAULTS: Branding = {
  panelName: 'Hosting Panel',
  panelTagline: '',
  primaryHue: DEFAULT_HUE,
  logoUrl: null,
  supportEmail: '',
  theme: 'dark',
  density: 'compact',
  role: 'admin',
  railCollapsed: false,
  configured: false,
};

interface BrandingContextValue extends Branding {
  setBranding: (patch: Partial<Branding>) => void;
  /** Persist server-side fields and mark configured=true. Throws on API failure. */
  save: (patch: Partial<Branding>) => Promise<void>;
  refresh: () => Promise<void>;
  /** Allow user to dismiss wizard without finishing it. */
  dismissWizard: () => void;
  loading: boolean;
}

const BrandingCtx = createContext<BrandingContextValue | null>(null);

const STORAGE_KEY = 'hp_branding';
const WIZARD_DISMISSED_KEY = 'hp_wizard_dismissed';
const LOCAL_KEYS: ReadonlyArray<keyof Branding> = ['theme', 'density', 'role', 'railCollapsed', 'panelTagline'];

function readLocal(): Partial<Branding> {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw) as Partial<Branding>;
    const out: Partial<Branding> = {};
    for (const k of LOCAL_KEYS) {
      if (k in parsed) (out as Record<string, unknown>)[k] = parsed[k as keyof typeof parsed];
    }
    return out;
  } catch {
    return {};
  }
}

function writeLocal(b: Branding) {
  const subset: Partial<Branding> = {};
  for (const k of LOCAL_KEYS) (subset as Record<string, unknown>)[k] = b[k];
  localStorage.setItem(STORAGE_KEY, JSON.stringify(subset));
}

export function BrandingProvider({ children }: { children: ReactNode }) {
  const [branding, setLocalBranding] = useState<Branding>(() => ({ ...DEFAULTS, ...readLocal() }));
  const [loading, setLoading] = useState(true);

  const refresh = async () => {
    try {
      // Build optional customer-override query param. Two cases:
      //   pre-auth — URL `?customer=...` (resellers send customers a personalized link)
      //   post-auth — logged-in user's own id (their override layers on top of global)
      const params = new URLSearchParams();
      const urlCust = new URLSearchParams(window.location.search).get('customer');
      if (urlCust) {
        params.set(urlCust.includes('@') ? 'customerEmail' : 'customerId', urlCust);
      } else {
        const stored = localStorage.getItem('hp_user');
        if (stored) {
          try { params.set('customerId', JSON.parse(stored).id); } catch { /* ignore */ }
        }
      }
      const qs = params.toString();
      const { data } = await api.get(qs ? `/branding?${qs}` : '/branding');
      const fetchedHue = data?.primaryColor ? hexToHue(data.primaryColor) ?? DEFAULT_HUE : DEFAULT_HUE;
      const wasDismissed = localStorage.getItem(WIZARD_DISMISSED_KEY) === '1';
      const looksConfigured =
        !!data?.panelName && data.panelName !== DEFAULTS.panelName && data.panelName !== '';
      setLocalBranding(prev => {
        const next: Branding = {
          ...prev,
          panelName: data?.panelName || DEFAULTS.panelName,
          logoUrl: data?.logoUrl || null,
          supportEmail: data?.supportEmail || '',
          primaryHue: fetchedHue,
          configured: looksConfigured || wasDismissed,
        };
        writeLocal(next);
        return next;
      });
    } catch {
      // Server unreachable — assume not configured so wizard offers.
      setLocalBranding(prev => ({ ...prev, configured: localStorage.getItem(WIZARD_DISMISSED_KEY) === '1' }));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void refresh(); }, []);

  useEffect(() => {
    const root = document.documentElement;
    root.dataset.theme = branding.theme;
    root.dataset.density = branding.density;
    root.style.setProperty('--p-h', String(branding.primaryHue));
    root.classList.toggle('dark', branding.theme === 'dark');
  }, [branding.theme, branding.density, branding.primaryHue]);

  const setBranding = (patch: Partial<Branding>) => {
    setLocalBranding(prev => {
      const next = { ...prev, ...patch };
      writeLocal(next);
      return next;
    });
  };

  const save = async (patch: Partial<Branding>) => {
    const serverPatch: Record<string, unknown> = {};
    if (patch.panelName !== undefined) serverPatch.panelName = patch.panelName;
    if (patch.logoUrl !== undefined) serverPatch.logoUrl = patch.logoUrl;
    if (patch.supportEmail !== undefined) serverPatch.supportEmail = patch.supportEmail;
    if (patch.primaryHue !== undefined) serverPatch.primaryColor = hueToHex(patch.primaryHue);
    if (Object.keys(serverPatch).length > 0) {
      await api.put('/branding', serverPatch);
    }
    localStorage.setItem(WIZARD_DISMISSED_KEY, '1');
    setBranding({ ...patch, configured: true });
  };

  const dismissWizard = () => {
    localStorage.setItem(WIZARD_DISMISSED_KEY, '1');
    setBranding({ configured: true });
  };

  const value = useMemo<BrandingContextValue>(
    () => ({ ...branding, setBranding, save, refresh, dismissWizard, loading }),
    [branding, loading],
  );

  return <BrandingCtx.Provider value={value}>{children}</BrandingCtx.Provider>;
}

export function useBranding(): BrandingContextValue {
  const ctx = useContext(BrandingCtx);
  if (!ctx) throw new Error('useBranding must be used within BrandingProvider');
  return ctx;
}

/** Back-compat shim for any legacy `useTheme()` callers during the wave 2-4 port. */
export function useTheme() {
  const b = useBranding();
  return {
    theme: b.theme,
    toggleTheme: () => b.setBranding({ theme: b.theme === 'dark' ? 'light' : 'dark' }),
  };
}
