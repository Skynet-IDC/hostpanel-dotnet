import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import api from '../api/client';
import { Card, Empty, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface ScanResult {
  infectedCount?: number;
  scanPath?: string;
  output?: string;
}

export function SecurityPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [scanning, setScanning] = useState(false);
  const [scanResult, setScanResult] = useState<ScanResult | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    api.get<{ id: number; name: string }[]>('/domains').then(r => setDomains(r.data)).catch(() => {});
  }, []);

  const scanDomain = async (id: number) => {
    setScanning(true); setErr(null);
    try {
      const r = await api.post<ScanResult>(`/domains/${id}/scan`);
      setScanResult(r.data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Scan failed');
    } finally { setScanning(false); }
  };

  const scanServer = async () => {
    setScanning(true); setErr(null);
    try {
      const r = await api.post<ScanResult>('/security/scan-server');
      setScanResult(r.data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Scan failed');
    } finally { setScanning(false); }
  };

  return (
    <>
      <PageHead title="Security" sub="Overview of all the panel's defenses" />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <div style={{ display: 'grid', gap: 16, gridTemplateColumns: 'repeat(3, 1fr)', marginBottom: 16 }}>
        <Link to="/firewall" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}><Icons.shield style={{ color: 'var(--primary-400)' }} /><div style={{ fontWeight: 600 }}>Firewall</div></div>
            <div className="muted" style={{ fontSize: 12 }}>UFW rules — open/close ports at the host level.</div>
          </div>
        </Link>
        <Link to="/ip" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}><Icons.network style={{ color: 'var(--primary-400)' }} /><div style={{ fontWeight: 600 }}>IP management</div></div>
            <div className="muted" style={{ fontSize: 12 }}>IP pool + manual blacklist with expiry.</div>
          </div>
        </Link>
        <Link to="/fail2ban" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}><Icons.shieldCheck style={{ color: 'var(--primary-400)' }} /><div style={{ fontWeight: 600 }}>Fail2ban</div></div>
            <div className="muted" style={{ fontSize: 12 }}>Auto-bans on repeated auth failures.</div>
          </div>
        </Link>
        <Link to="/malware" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}><Icons.bug style={{ color: 'var(--primary-400)' }} /><div style={{ fontWeight: 600 }}>Malware scans</div></div>
            <div className="muted" style={{ fontSize: 12 }}>ClamAV scans + quarantine for tenants.</div>
          </div>
        </Link>
        <Link to="/2fa" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}><Icons.key style={{ color: 'var(--primary-400)' }} /><div style={{ fontWeight: 600 }}>Two-factor auth</div></div>
            <div className="muted" style={{ fontSize: 12 }}>TOTP enrollment for your admin account.</div>
          </div>
        </Link>
        <Link to="/audit" style={{ textDecoration: 'none' }}>
          <div className="card" style={{ padding: 16, cursor: 'pointer', height: '100%' }}>
            <div className="row gap-3" style={{ marginBottom: 8 }}><Icons.log style={{ color: 'var(--primary-400)' }} /><div style={{ fontWeight: 600 }}>Audit log</div></div>
            <div className="muted" style={{ fontSize: 12 }}>Every privileged action against the panel.</div>
          </div>
        </Link>
      </div>

      <Card title="Quick malware scan" sub="Trigger a ClamAV scan on a single domain or the whole server. Long scans may time out — for thorough scans, use the dedicated Malware page.">
        <div className="col gap-3">
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 8 }}>
            {domains.map(d => (
              <button key={d.id} className="btn" onClick={() => scanDomain(d.id)} disabled={scanning}>
                <Icons.bug />Scan {d.name}
              </button>
            ))}
          </div>
          <div className="row gap-2">
            <button className="btn danger" onClick={scanServer} disabled={scanning}>
              <Icons.bug />{scanning ? 'Scanning…' : 'Full server scan'}
            </button>
          </div>

          {scanResult && (
            <div style={{ marginTop: 8 }}>
              <div className="row gap-3" style={{ marginBottom: 8 }}>
                <span className="badge">Path: <span className="mono" style={{ marginLeft: 4 }}>{scanResult.scanPath ?? '—'}</span></span>
                <span className={`badge ${(scanResult.infectedCount ?? 0) > 0 ? 'danger' : 'success'}`}>
                  <span className="dot" />{scanResult.infectedCount ?? 0} infected
                </span>
              </div>
              {scanResult.output && (
                <pre style={{ background: 'var(--bg)', border: '1px solid var(--border)', borderRadius: 8, padding: 12, fontFamily: 'var(--font-mono)', fontSize: 12, whiteSpace: 'pre-wrap', maxHeight: 240, overflow: 'auto', margin: 0 }}>
                  {scanResult.output}
                </pre>
              )}
            </div>
          )}

          {domains.length === 0 && !scanResult && (
            <Empty icon="globe" title="No domains" sub="Add a domain to scan its docroot." />
          )}
        </div>
      </Card>
    </>
  );
}
