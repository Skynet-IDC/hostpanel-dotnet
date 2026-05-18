import { useState } from 'react';
import api from '../../api/client';
import { useBranding } from '../../contexts/BrandingContext';
import { Icons } from '../ui/Icons';
import { hueToHex } from '../../styles/colorUtils';

type StepId = 'branding' | 'smtp' | 'plan' | 'dns' | 'cloudflare' | 'done';

const STEPS: ReadonlyArray<{ id: StepId; label: string; optional?: boolean }> = [
  { id: 'branding', label: 'Brand the panel' },
  { id: 'smtp', label: 'Mail (optional)', optional: true },
  { id: 'plan', label: 'Default service plan' },
  { id: 'dns', label: 'DNS resolver (optional)', optional: true },
  { id: 'cloudflare', label: 'Cloudflare (optional)', optional: true },
  { id: 'done', label: 'Done' },
];

interface SetupState {
  panelName: string;
  panelTagline: string;
  supportEmail: string;
  primaryHue: number;
  smtpHost: string;
  smtpPort: number;
  smtpUser: string;
  smtpPassword: string;
  smtpFromAddress: string;
  planName: string;
  planPrice: number;
  planDiskGb: number;
  planBandwidthGb: number;
  planMaxDomains: number;
  planMaxMailboxes: number;
  planMaxDatabases: number;
  dnsResolver: string;
  cloudflareApiToken: string;
}

const DEFAULT_STATE: SetupState = {
  panelName: '',
  panelTagline: '',
  supportEmail: '',
  primaryHue: 268,
  smtpHost: '',
  smtpPort: 587,
  smtpUser: '',
  smtpPassword: '',
  smtpFromAddress: '',
  planName: 'Starter',
  planPrice: 9,
  planDiskGb: 5,
  planBandwidthGb: 50,
  planMaxDomains: 1,
  planMaxMailboxes: 5,
  planMaxDatabases: 1,
  dnsResolver: '1.1.1.1',
  cloudflareApiToken: '',
};

export function SetupWizard() {
  const branding = useBranding();
  const [stepIdx, setStepIdx] = useState(0);
  const [state, setState] = useState<SetupState>(() => ({ ...DEFAULT_STATE, supportEmail: branding.supportEmail }));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const current = STEPS[stepIdx];
  const isLast = current.id === 'done';
  const set = <K extends keyof SetupState>(key: K, value: SetupState[K]) =>
    setState(s => ({ ...s, [key]: value }));

  const saveCurrentStep = async (): Promise<boolean> => {
    setError(null);
    setSaving(true);
    try {
      if (current.id === 'branding') {
        await branding.save({
          panelName: state.panelName.trim() || 'Hosting Panel',
          panelTagline: state.panelTagline.trim(),
          supportEmail: state.supportEmail.trim(),
          primaryHue: state.primaryHue,
        });
      } else if (current.id === 'smtp' && state.smtpHost.trim()) {
        await api.post('/setup/smtp', {
          host: state.smtpHost.trim(),
          port: state.smtpPort,
          username: state.smtpUser.trim() || null,
          password: state.smtpPassword || null,
          fromAddress: state.smtpFromAddress.trim() || state.supportEmail,
        });
      } else if (current.id === 'plan') {
        await api.post('/plans', {
          name: state.planName.trim(),
          pricePerPeriod: state.planPrice,
          diskQuotaMb: state.planDiskGb * 1024,
          bandwidthLimitMb: state.planBandwidthGb * 1024,
          maxDomains: state.planMaxDomains,
          maxEmailAccounts: state.planMaxMailboxes,
          maxDatabases: state.planMaxDatabases,
        });
      } else if (current.id === 'dns' && state.dnsResolver.trim()) {
        await api.put('/setup/dns', { resolver: state.dnsResolver.trim() });
      } else if (current.id === 'cloudflare' && state.cloudflareApiToken.trim()) {
        await api.put('/setup/cloudflare', { apiToken: state.cloudflareApiToken.trim() });
      }
      return true;
    } catch (err: unknown) {
      const detail = err instanceof Error ? err.message : 'Save failed';
      // Don't block on missing optional endpoints — log + continue.
      if (current.optional) {
        console.warn(`Optional step ${current.id} failed (continuing):`, detail);
        return true;
      }
      setError(detail);
      return false;
    } finally {
      setSaving(false);
    }
  };

  const next = async () => {
    const ok = await saveCurrentStep();
    if (!ok) return;
    setStepIdx(i => Math.min(STEPS.length - 1, i + 1));
  };

  const back = () => setStepIdx(i => Math.max(0, i - 1));

  const finish = async () => {
    branding.dismissWizard();
  };

  const swatchColor = hueToHex(state.primaryHue);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-labelledby="wizard-title">
      <div className="dialog lg" style={{ display: 'flex', flexDirection: 'column' }}>
        <div className="dialog-head" style={{ flexDirection: 'column', alignItems: 'stretch', gap: 12 }}>
          <div className="between">
            <div>
              <div id="wizard-title" style={{ fontWeight: 700, fontSize: 16 }}>Welcome to {state.panelName || 'your hosting panel'}</div>
              <div style={{ fontSize: 12, color: 'var(--muted)', marginTop: 2 }}>
                A quick setup so the panel feels like yours. Skip optional steps if you're not ready.
              </div>
            </div>
            <button className="btn ghost sm" onClick={finish} title="Skip setup">Skip for now</button>
          </div>
          <ol className="row gap-2" style={{ listStyle: 'none', padding: 0, margin: 0, flexWrap: 'wrap' }}>
            {STEPS.filter(s => s.id !== 'done').map((s, i) => {
              const status = i < stepIdx ? 'done' : i === stepIdx ? 'current' : 'pending';
              return (
                <li
                  key={s.id}
                  className="row gap-2"
                  style={{
                    fontSize: 11,
                    padding: '4px 10px',
                    borderRadius: 999,
                    border: '1px solid var(--border)',
                    background: status === 'current' ? 'color-mix(in oklch, var(--primary-500) 18%, transparent)' : 'var(--surface)',
                    color: status === 'pending' ? 'var(--muted)' : 'var(--text)',
                    fontWeight: status === 'current' ? 600 : 500,
                  }}
                >
                  <span style={{ width: 14, height: 14, display: 'grid', placeItems: 'center' }}>
                    {status === 'done' ? <Icons.check style={{ color: 'var(--success)' }} /> : i + 1}
                  </span>
                  {s.label}
                </li>
              );
            })}
          </ol>
        </div>

        <div className="dialog-body">
          {current.id === 'branding' && (
            <div className="col gap-4">
              <div className="grid-2" style={{ gap: 12 }}>
                <div className="field">
                  <label>Panel name</label>
                  <input className="input" autoFocus placeholder="Hosting Panel" value={state.panelName} onChange={e => set('panelName', e.target.value)} />
                  <div className="hint">Shown in the sidebar, browser tab, and welcome emails.</div>
                </div>
                <div className="field">
                  <label>Tagline (optional)</label>
                  <input className="input" placeholder="self-hosted on your own iron" value={state.panelTagline} onChange={e => set('panelTagline', e.target.value)} />
                </div>
                <div className="field">
                  <label>Support email</label>
                  <input className="input" type="email" placeholder="admin@example.com" value={state.supportEmail} onChange={e => set('supportEmail', e.target.value)} />
                  <div className="hint">Where customers can reach you. Shown in the panel footer.</div>
                </div>
                <div className="field">
                  <label>Primary color hue</label>
                  <div className="row gap-3">
                    <input type="range" min={0} max={360} value={state.primaryHue} onChange={e => set('primaryHue', Number(e.target.value))} style={{ flex: 1 }} />
                    <div style={{ width: 28, height: 28, borderRadius: 8, background: swatchColor, border: '1px solid var(--border)' }} />
                    <span className="mono" style={{ fontSize: 12, color: 'var(--muted)' }}>{state.primaryHue}°</span>
                  </div>
                </div>
              </div>
              <div className="alert info">
                <Icons.info />
                <div>Themes everything in the panel — buttons, links, status accents. You can change it any time in Branding.</div>
              </div>
            </div>
          )}

          {current.id === 'smtp' && (
            <div className="col gap-4">
              <div className="alert">
                <Icons.info />
                <div>Skip if you'll use the server's local Postfix relay. Configure SMTP here only if you want the panel to send via an external provider (SES, SendGrid, Mailgun, etc).</div>
              </div>
              <div className="grid-2" style={{ gap: 12 }}>
                <div className="field"><label>SMTP host</label><input className="input" placeholder="smtp.example.com" value={state.smtpHost} onChange={e => set('smtpHost', e.target.value)} /></div>
                <div className="field"><label>Port</label><input className="input" type="number" value={state.smtpPort} onChange={e => set('smtpPort', Number(e.target.value))} /></div>
                <div className="field"><label>Username</label><input className="input" value={state.smtpUser} onChange={e => set('smtpUser', e.target.value)} /></div>
                <div className="field"><label>Password</label><input className="input" type="password" value={state.smtpPassword} onChange={e => set('smtpPassword', e.target.value)} /></div>
                <div className="field" style={{ gridColumn: 'span 2' }}><label>From address</label><input className="input" type="email" placeholder="no-reply@example.com" value={state.smtpFromAddress} onChange={e => set('smtpFromAddress', e.target.value)} /></div>
              </div>
            </div>
          )}

          {current.id === 'plan' && (
            <div className="col gap-4">
              <div className="alert info">
                <Icons.info />
                <div>You need at least one service plan before you can create customer subscriptions. This becomes your default — you can add more in Service Plans.</div>
              </div>
              <div className="grid-2" style={{ gap: 12 }}>
                <div className="field" style={{ gridColumn: 'span 2' }}><label>Plan name</label><input className="input" value={state.planName} onChange={e => set('planName', e.target.value)} /></div>
                <div className="field"><label>Price / month (USD)</label><input className="input" type="number" min={0} value={state.planPrice} onChange={e => set('planPrice', Number(e.target.value))} /></div>
                <div className="field"><label>Disk quota (GB)</label><input className="input" type="number" min={1} value={state.planDiskGb} onChange={e => set('planDiskGb', Number(e.target.value))} /></div>
                <div className="field"><label>Bandwidth / month (GB)</label><input className="input" type="number" min={1} value={state.planBandwidthGb} onChange={e => set('planBandwidthGb', Number(e.target.value))} /></div>
                <div className="field"><label>Max domains</label><input className="input" type="number" min={1} value={state.planMaxDomains} onChange={e => set('planMaxDomains', Number(e.target.value))} /></div>
                <div className="field"><label>Max mailboxes</label><input className="input" type="number" min={0} value={state.planMaxMailboxes} onChange={e => set('planMaxMailboxes', Number(e.target.value))} /></div>
                <div className="field"><label>Max databases</label><input className="input" type="number" min={0} value={state.planMaxDatabases} onChange={e => set('planMaxDatabases', Number(e.target.value))} /></div>
              </div>
            </div>
          )}

          {current.id === 'dns' && (
            <div className="col gap-4">
              <div className="alert">
                <Icons.info />
                <div>Optional. The panel-managed PowerDNS uses this resolver for upstream lookups. Defaults to Cloudflare's 1.1.1.1 if left blank.</div>
              </div>
              <div className="field">
                <label>Upstream DNS resolver</label>
                <input className="input" placeholder="1.1.1.1" value={state.dnsResolver} onChange={e => set('dnsResolver', e.target.value)} />
                <div className="hint">Use 8.8.8.8 for Google, 9.9.9.9 for Quad9, or your own internal resolver.</div>
              </div>
            </div>
          )}

          {current.id === 'cloudflare' && (
            <div className="col gap-4">
              <div className="alert">
                <Icons.info />
                <div>Optional. Add a Cloudflare API token to enable DNS-01 wildcard certs and auto-DNS for sites whose zone is on Cloudflare. Skip if you'll manage Cloudflare manually or don't use it.</div>
              </div>
              <div className="field">
                <label>Cloudflare API token</label>
                <input className="input mono" type="password" autoComplete="off" spellCheck={false} value={state.cloudflareApiToken} onChange={e => set('cloudflareApiToken', e.target.value)} />
                <div className="hint">Create at dash.cloudflare.com → Profile → API Tokens. Needs <span className="mono">Zone.DNS Edit</span> on the zones you'll host.</div>
              </div>
            </div>
          )}

          {current.id === 'done' && (
            <div className="col gap-4 center" style={{ textAlign: 'center', padding: '24px 16px' }}>
              <div style={{ width: 48, height: 48, borderRadius: 999, background: 'var(--success-bg)', color: 'var(--success)', display: 'grid', placeItems: 'center' }}>
                <Icons.check style={{ width: 22, height: 22 }} />
              </div>
              <div style={{ fontWeight: 700, fontSize: 17 }}>You're all set.</div>
              <div className="muted" style={{ fontSize: 13, maxWidth: 360 }}>
                {state.panelName} is configured. Next steps: create your first customer, then add a domain for them.
              </div>
            </div>
          )}
        </div>

        <div className="dialog-foot">
          {error && <div style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{error}</div>}
          {stepIdx > 0 && !isLast && (
            <button className="btn ghost" onClick={back} disabled={saving}>Back</button>
          )}
          {!isLast ? (
            <button className="btn primary" onClick={next} disabled={saving}>
              {saving ? 'Saving…' : current.optional ? 'Skip / Continue' : 'Continue'}
            </button>
          ) : (
            <button className="btn primary" onClick={finish}>Get started</button>
          )}
        </div>
      </div>
    </div>
  );
}
