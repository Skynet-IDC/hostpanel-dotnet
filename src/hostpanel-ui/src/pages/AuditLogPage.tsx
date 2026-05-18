import { useEffect, useState } from 'react';
import api from '../api/client';
import { Card, Empty, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface AuditEntry {
  id: number;
  userId?: string;
  action: string;
  entityType: string;
  entityId?: number;
  details?: string;
  ipAddress?: string;
  traceId?: string;
  createdAt: string;
}

export function AuditLogPage() {
  const [logs, setLogs] = useState<AuditEntry[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [action, setAction] = useState('');
  const [entityType, setEntityType] = useState('');
  const [loading, setLoading] = useState(true);

  const load = async () => {
    setLoading(true);
    const params = new URLSearchParams({ page: String(page), pageSize: '50' });
    if (action) params.set('action', action);
    if (entityType) params.set('entityType', entityType);
    try {
      const { data } = await api.get(`/audit?${params}`);
      setLogs(data.items ?? []);
      setTotalPages(data.totalPages ?? 1);
    } finally { setLoading(false); }
  };

  useEffect(() => { void load(); }, [page]);

  return (
    <>
      <PageHead title="Audit log" sub="Every privileged action against the panel" />

      <div className="row gap-2" style={{ marginBottom: 12 }}>
        <input className="input mono" placeholder="action filter (e.g. Domains.Create)" value={action} onChange={e => setAction(e.target.value)} style={{ flex: 1, maxWidth: 280 }} />
        <input className="input mono" placeholder="entity type filter" value={entityType} onChange={e => setEntityType(e.target.value)} style={{ flex: 1, maxWidth: 220 }} />
        <button className="btn" onClick={() => { setPage(1); void load(); }}><Icons.filter />Filter</button>
      </div>

      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : logs.length === 0 ? <Empty icon="log" title="No audit entries" sub="They'll appear here as you (or automated jobs) do things." />
          : (
            <table className="tbl">
              <thead><tr><th>Time</th><th>Action</th><th>Entity</th><th>User</th><th>IP</th><th>Details</th></tr></thead>
              <tbody>
                {logs.map(l => (
                  <tr key={l.id}>
                    <td className="mono muted" style={{ fontSize: 11.5 }}>{new Date(l.createdAt).toLocaleString()}</td>
                    <td className="mono">{l.action}</td>
                    <td>{l.entityType}{l.entityId ? ` #${l.entityId}` : ''}</td>
                    <td className="mono muted" style={{ fontSize: 11 }}>{l.userId?.slice(0, 8) ?? '—'}</td>
                    <td className="mono muted" style={{ fontSize: 11 }}>{l.ipAddress ?? '—'}</td>
                    <td className="muted" style={{ fontSize: 11, maxWidth: 280, overflow: 'hidden', textOverflow: 'ellipsis' }}>{l.details ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </Card>

      {totalPages > 1 && (
        <div className="row center gap-2" style={{ marginTop: 12 }}>
          <button className="btn" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}><Icons.chevL />Prev</button>
          <span className="muted" style={{ fontSize: 12 }}>Page {page} of {totalPages}</span>
          <button className="btn" onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages}>Next<Icons.chevR /></button>
        </div>
      )}
    </>
  );
}
