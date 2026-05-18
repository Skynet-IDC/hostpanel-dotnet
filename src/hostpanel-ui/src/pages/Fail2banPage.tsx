import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Empty, IconBtn, PageHead, Stat } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface Jail {
  name: string;
  currentlyFailed: number;
  totalFailed: number;
  currentlyBanned: number;
  totalBanned: number;
  bannedIps: string[];
}

interface Status {
  enabled: boolean;
  jails: Jail[];
}

export function Fail2banPage() {
  const [status, setStatus] = useState<Status | null>(null);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);

  const load = async () => {
    setErr(null);
    try {
      const { data } = await api.get<Status>('/security/fail2ban');
      setStatus(data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Load failed');
    } finally { setLoading(false); }
  };
  useEffect(() => { void load(); }, []);

  const unban = async (jail: string, ip: string) => {
    if (!confirm(`Unban ${ip} from ${jail}?`)) return;
    try {
      await api.post('/security/fail2ban/unban', { jail, ipAddress: ip });
      await load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Unban failed');
    }
  };

  const totalBanned = status?.jails.reduce((s, j) => s + j.currentlyBanned, 0) ?? 0;
  const totalJails = status?.jails.length ?? 0;

  return (
    <>
      <PageHead
        title="Fail2ban"
        sub="Automatic IP bans on repeated auth failures"
        actions={<button className="btn" onClick={load}><Icons.refresh />Refresh</button>}
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {loading ? <Card><div className="muted" style={{ padding: 8 }}>Loading…</div></Card>
        : !status?.enabled ? <Card><Empty icon="shieldCheck" title="Fail2ban not running" sub="Install + enable fail2ban on the host to activate auto-banning." /></Card>
        : (
          <>
            <div className="stats" style={{ marginBottom: 16, gridTemplateColumns: 'repeat(3, 1fr)' }}>
              <Stat label="Status" icon="shieldCheck" value="Active" foot={<Badge tone="success" dot>Running</Badge>} />
              <Stat label="Jails" icon="lock" value={String(totalJails)} foot={<span className="muted">configured</span>} />
              <Stat label="Currently banned" icon="bug" value={String(totalBanned)} foot={<span className="muted">across all jails</span>} />
            </div>

            {status.jails.map(j => (
              <Card key={j.name} title={j.name} sub={`${j.totalFailed} total failures · ${j.totalBanned} total bans`} flush style={{ marginBottom: 12 }}>
                {j.bannedIps.length === 0 ? (
                  <div className="muted" style={{ padding: 16, textAlign: 'center', fontSize: 12 }}>No active bans</div>
                ) : (
                  <table className="tbl">
                    <thead><tr><th>Banned IP</th><th style={{ textAlign: 'right' }} /></tr></thead>
                    <tbody>
                      {j.bannedIps.map(ip => (
                        <tr key={ip}>
                          <td className="mono">{ip}</td>
                          <td><div className="row-actions"><IconBtn icon="check" title="Unban" onClick={() => unban(j.name, ip)} /></div></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </Card>
            ))}
          </>
        )}
    </>
  );
}
