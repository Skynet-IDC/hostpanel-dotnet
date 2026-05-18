import { useEffect, useState } from 'react';
import api from '../api/client';
import { useBranding } from '../contexts/BrandingContext';
import { Card, Checkbox, Empty, Field, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';
import { hueToHex } from '../styles/colorUtils';

interface BrandingApi {
  panelName: string;
  logoUrl: string | null;
  faviconUrl: string | null;
  primaryColor: string;
  sidebarColor: string;
  accentColor: string | null;
  loginBackgroundUrl: string | null;
  loginMessage: string | null;
  footerText: string | null;
  supportUrl: string | null;
  supportEmail: string | null;
  customCss: string | null;
  hidePoweredBy: boolean;
}

export function BrandingPage() {
  const ctx = useBranding();
  const [state, setState] = useState<BrandingApi | null>(null);
  const [hue, setHue] = useState(ctx.primaryHue);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<number | null>(null);

  const load = async () => {
    setLoading(true);
    setErr(null);
    try {
      const { data } = await api.get<BrandingApi>('/branding');
      setState(data);
      setHue(ctx.primaryHue);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Load failed');
    } finally {
      setLoading(false);
    }
  };
  useEffect(() => { void load(); }, []);

  const set = <K extends keyof BrandingApi>(k: K, v: BrandingApi[K]) => setState(s => s ? { ...s, [k]: v } : s);

  const save = async () => {
    if (!state) return;
    setBusy(true); setErr(null);
    try {
      const primaryColor = hueToHex(hue);
      await api.put('/branding', { ...state, primaryColor });
      await ctx.save({ primaryHue: hue, panelName: state.panelName, logoUrl: state.logoUrl, supportEmail: state.supportEmail ?? '' });
      setSavedAt(Date.now());
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Save failed');
    } finally { setBusy(false); }
  };

  const reset = async () => {
    if (!confirm('Reset branding to defaults? This clears your logo, name, colors, and custom CSS.')) return;
    setBusy(true); setErr(null);
    try {
      await api.delete('/branding');
      await ctx.refresh();
      await load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Reset failed');
    } finally { setBusy(false); }
  };

  if (loading || !state) {
    return (
      <>
        <PageHead title="Branding" />
        <Card><div className="muted" style={{ padding: 8 }}>Loading…</div></Card>
      </>
    );
  }

  return (
    <>
      <PageHead
        title="Branding"
        sub="Make the panel look like yours. Changes apply instantly across all customers."
        actions={
          <>
            <button className="btn" onClick={reset} disabled={busy}>Reset to defaults</button>
            <button className="btn primary" onClick={save} disabled={busy}>
              <Icons.check />{busy ? 'Saving…' : 'Save'}
            </button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}
      {savedAt && Date.now() - savedAt < 4000 && (
        <div className="alert success" style={{ marginBottom: 12 }}><Icons.check2 />Saved.</div>
      )}

      <div style={{ display: 'grid', gap: 16, gridTemplateColumns: '2fr 1fr' }}>
        <div className="col gap-4">
          <Card title="Identity" sub="What customers see in the sidebar, browser tab, and welcome emails.">
            <div className="grid-2" style={{ gap: 12 }}>
              <Field label="Panel name"><input className="input" value={state.panelName} onChange={e => set('panelName', e.target.value)} /></Field>
              <Field label="Support email"><input className="input" type="email" value={state.supportEmail ?? ''} onChange={e => set('supportEmail', e.target.value)} /></Field>
              <Field label="Logo" span={2} hint="Upload a PNG, SVG, JPEG, or WebP (max 5 MB). Or paste an external URL.">
                <div className="row gap-2" style={{ alignItems: 'flex-start' }}>
                  {state.logoUrl && (
                    <img src={state.logoUrl} alt="" style={{ width: 48, height: 48, borderRadius: 6, border: '1px solid var(--border)', objectFit: 'contain', background: 'var(--bg-2)' }} />
                  )}
                  <div className="col gap-2" style={{ flex: 1 }}>
                    <input className="input mono" value={state.logoUrl ?? ''} onChange={e => set('logoUrl', e.target.value)} placeholder="https://example.com/logo.svg" />
                    <div className="row gap-2">
                      <label className="btn" style={{ cursor: 'pointer' }}>
                        <Icons.upload />Upload file
                        <input type="file" style={{ display: 'none' }} accept="image/png,image/jpeg,image/svg+xml,image/webp" onChange={async e => {
                          const file = e.target.files?.[0];
                          if (!file) return;
                          const fd = new FormData();
                          fd.append('file', file);
                          try {
                            const { data } = await api.post<{ url: string }>('/branding/logo', fd, { headers: { 'Content-Type': 'multipart/form-data' } });
                            set('logoUrl', data.url);
                          } catch (err) {
                            const msg = err && typeof err === 'object' && 'response' in err ? (err as { response?: { data?: { message?: string } } }).response?.data?.message : null;
                            alert(msg ?? 'Upload failed');
                          }
                          e.target.value = '';
                        }} />
                      </label>
                      {state.logoUrl && <button className="btn ghost" onClick={() => set('logoUrl', null)}>Remove</button>}
                    </div>
                  </div>
                </div>
              </Field>
              <Field label="Favicon URL" span={2}><input className="input mono" value={state.faviconUrl ?? ''} onChange={e => set('faviconUrl', e.target.value)} placeholder="https://example.com/favicon.ico" /></Field>
              <Field label="Footer text" span={2}><input className="input" value={state.footerText ?? ''} onChange={e => set('footerText', e.target.value)} placeholder="© Your Company" /></Field>
              <Field label="Support URL" span={2}><input className="input mono" value={state.supportUrl ?? ''} onChange={e => set('supportUrl', e.target.value)} placeholder="https://support.example.com" /></Field>
            </div>
          </Card>

          <Card title="Login screen">
            <div className="grid-2" style={{ gap: 12 }}>
              <Field label="Background image URL" span={2}><input className="input mono" value={state.loginBackgroundUrl ?? ''} onChange={e => set('loginBackgroundUrl', e.target.value)} placeholder="https://example.com/hero.jpg" /></Field>
              <Field label="Welcome message" span={2}><input className="input" value={state.loginMessage ?? ''} onChange={e => set('loginMessage', e.target.value)} placeholder="Welcome back to your panel." /></Field>
            </div>
          </Card>

          <Card title="Advanced">
            <div className="col gap-3">
              <Field label="Custom CSS" hint="Injected into the panel. Use sparingly."><textarea className="textarea" rows={6} value={state.customCss ?? ''} onChange={e => set('customCss', e.target.value)} placeholder="/* .nav-item { letter-spacing: 0.5px; } */" /></Field>
              <Checkbox checked={state.hidePoweredBy} onChange={v => set('hidePoweredBy', v)} label='Hide "Powered by HostPanel" footer' />
            </div>
          </Card>
        </div>

        <Card title="Theme color">
          <div className="col gap-4">
            <div>
              <div className="row between" style={{ marginBottom: 6 }}>
                <span className="muted" style={{ fontSize: 12 }}>Primary hue</span>
                <span className="mono" style={{ fontSize: 12 }}>{hue}°</span>
              </div>
              <input type="range" min={0} max={360} value={hue} onChange={e => setHue(Number(e.target.value))} style={{ width: '100%' }} />
              <div className="row gap-2" style={{ marginTop: 8 }}>
                <div style={{ flex: 1, height: 32, borderRadius: 8, background: hueToHex(hue), border: '1px solid var(--border)' }} />
                <span className="mono muted" style={{ alignSelf: 'center', fontSize: 11 }}>{hueToHex(hue)}</span>
              </div>
            </div>

            <div style={{ borderTop: '1px solid var(--border)', paddingTop: 12 }}>
              <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>Preview</div>
              <div className="col gap-2">
                <button className="btn primary" type="button">Primary button</button>
                <button className="btn" type="button">Secondary</button>
                <span className="badge primary"><span className="dot" />Primary badge</span>
              </div>
            </div>

            <div style={{ borderTop: '1px solid var(--border)', paddingTop: 12 }}>
              <Empty
                icon="info"
                title="Preview color presets"
                sub="Click any hue to try it on for size. The panel re-themes instantly; click Save to persist."
              />
              <div className="row gap-2" style={{ flexWrap: 'wrap', justifyContent: 'center' }}>
                {[268, 220, 175, 145, 60, 30, 0, 320].map(h => (
                  <button
                    key={h}
                    type="button"
                    onClick={() => setHue(h)}
                    title={`hue ${h}°`}
                    style={{
                      width: 26, height: 26, borderRadius: 999,
                      background: hueToHex(h), border: hue === h ? '2px solid var(--text)' : '1px solid var(--border)',
                      cursor: 'pointer', padding: 0,
                    }}
                  />
                ))}
              </div>
            </div>
          </div>
        </Card>
      </div>
    </>
  );
}
