import { useEffect, useState } from 'react';
import api from '../api/client';

interface AuditEntry { id: number; userId?: string; action: string; entityType: string; entityId?: number; details?: string; ipAddress?: string; traceId?: string; createdAt: string; }

export function AuditLogPage() {
  const [logs, setLogs] = useState<AuditEntry[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [filter, setFilter] = useState({ action: '', entityType: '' });
  const [loading, setLoading] = useState(true);

  const load = () => {
    setLoading(true);
    const params = new URLSearchParams({ page: page.toString(), pageSize: '50' });
    if (filter.action) params.set('action', filter.action);
    if (filter.entityType) params.set('entityType', filter.entityType);

    api.get(`/audit?${params}`).then(r => {
      setLogs(r.data.items);
      setTotalPages(r.data.totalPages);
    }).finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, [page]);

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Audit Log</h2>

      <div className="flex gap-3 mb-4">
        <input placeholder="Filter by action..." value={filter.action} onChange={e => setFilter({...filter, action: e.target.value})}
          className="border rounded px-3 py-2 text-sm" />
        <input placeholder="Filter by entity type..." value={filter.entityType} onChange={e => setFilter({...filter, entityType: e.target.value})}
          className="border rounded px-3 py-2 text-sm" />
        <button onClick={() => { setPage(1); load(); }} className="bg-blue-600 text-white rounded px-4 py-2 text-sm hover:bg-blue-700">Filter</button>
      </div>

      <div className="bg-white rounded-lg shadow overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Time</th>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Action</th>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Entity</th>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">User</th>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">IP</th>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Details</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {loading ? (
              <tr><td colSpan={6} className="px-4 py-8 text-center text-gray-500">Loading...</td></tr>
            ) : logs.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-8 text-center text-gray-500">No audit entries found.</td></tr>
            ) : logs.map(l => (
              <tr key={l.id} className="hover:bg-gray-50">
                <td className="px-4 py-2 whitespace-nowrap">{new Date(l.createdAt).toLocaleString()}</td>
                <td className="px-4 py-2 font-mono">{l.action}</td>
                <td className="px-4 py-2">{l.entityType}{l.entityId ? ` #${l.entityId}` : ''}</td>
                <td className="px-4 py-2 font-mono text-xs">{l.userId?.slice(0, 8) ?? '-'}</td>
                <td className="px-4 py-2 font-mono text-xs">{l.ipAddress ?? '-'}</td>
                <td className="px-4 py-2 text-xs text-gray-500 max-w-xs truncate">{l.details ?? '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {totalPages > 1 && (
        <div className="flex justify-center gap-2 mt-4">
          <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}
            className="px-3 py-1 border rounded disabled:opacity-50">Prev</button>
          <span className="px-3 py-1">Page {page} of {totalPages}</span>
          <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages}
            className="px-3 py-1 border rounded disabled:opacity-50">Next</button>
        </div>
      )}
    </div>
  );
}
