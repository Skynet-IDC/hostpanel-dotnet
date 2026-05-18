import { useEffect, useState } from 'react';
import api from '../api/client';
import { Card, Empty, PageHead, Stat } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface ServerStats {
  loadAverage?: string;
  usedMemoryBytes?: number;
  totalMemoryBytes?: number;
  usedDiskBytes?: number;
  totalDiskBytes?: number;
  uptimeSeconds?: number;
}

interface VisitorStats {
  totalHits?: number;
  uniqueVisitors?: number;
  period?: string;
}

function fmtBytes(b?: number) {
  if (!b) return '—';
  if (b >= 1024 ** 3) return `${(b / 1024 ** 3).toFixed(1)} GB`;
  if (b >= 1024 ** 2) return `${(b / 1024 ** 2).toFixed(1)} MB`;
  return `${Math.round(b / 1024)} KB`;
}

function fmtUptime(seconds?: number) {
  if (!seconds) return '—';
  const days = Math.floor(seconds / 86400);
  if (days >= 1) return `${days}d`;
  return `${Math.floor(seconds / 3600)}h`;
}

export function AnalyticsPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [visitors, setVisitors] = useState<VisitorStats | null>(null);
  const [serverStats, setServerStats] = useState<ServerStats | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    api.get<{ id: number; name: string }[]>('/domains')
      .then(r => { setDomains(r.data); if (r.data.length > 0) setDomainId(r.data[0].id); })
      .catch(e => setErr(e instanceof Error ? e.message : 'Failed to load domains'));
    api.get<ServerStats>('/server/analytics').then(r => setServerStats(r.data)).catch(() => setServerStats(null));
  }, []);

  useEffect(() => {
    if (!domainId) return;
    api.get<VisitorStats>(`/domains/${domainId}/analytics/visitors`).then(r => setVisitors(r.data)).catch(() => setVisitors(null));
  }, [domainId]);

  return (
    <>
      <PageHead
        title="Analytics"
        sub="Server load + per-domain visitor traffic"
        actions={
          domains.length > 0 && (
            <select className="select" value={domainId ?? ''} onChange={e => setDomainId(Number(e.target.value))} style={{ maxWidth: 240 }}>
              {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          )
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {serverStats && (
        <div className="stats" style={{ marginBottom: 16 }}>
          <Stat label="Load average (1m)" icon="cpu" value={serverStats.loadAverage?.split(' ')[0] ?? '—'} />
          <Stat label="Memory" icon="cpu" value={fmtBytes(serverStats.usedMemoryBytes)} foot={<span className="muted">of {fmtBytes(serverStats.totalMemoryBytes)}</span>} />
          <Stat label="Disk" icon="archive" value={fmtBytes(serverStats.usedDiskBytes)} foot={<span className="muted">of {fmtBytes(serverStats.totalDiskBytes)}</span>} />
          <Stat label="Uptime" icon="clock" value={fmtUptime(serverStats.uptimeSeconds)} />
        </div>
      )}

      <Card title="Domain visitors" sub={visitors?.period ? `Period: ${visitors.period}` : 'Select a domain'} flush>
        {!visitors ? (
          <Empty icon="graph" title="No visitor data" sub="Visitor analytics need an access log parser. Wired to /domains/{id}/analytics/visitors." />
        ) : (
          <div className="stats" style={{ padding: 16, gridTemplateColumns: 'repeat(2, 1fr)' }}>
            <Stat label="Total hits" icon="trending" value={(visitors.totalHits ?? 0).toLocaleString()} />
            <Stat label="Unique visitors" icon="users" value={(visitors.uniqueVisitors ?? 0).toLocaleString()} />
          </div>
        )}
      </Card>
    </>
  );
}
