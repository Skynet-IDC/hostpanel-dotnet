import { useEffect, useState } from 'react';
import api from '../api/client';

export function AnalyticsPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [visitors, setVisitors] = useState<any>(null);
  const [serverStats, setServerStats] = useState<any>(null);

  useEffect(() => {
    api.get('/domains').then(r => { setDomains(r.data); if (r.data.length > 0) setDomainId(r.data[0].id); });
    api.get('/server/analytics').then(r => setServerStats(r.data)).catch(() => {});
  }, []);

  useEffect(() => {
    if (!domainId) return;
    api.get(`/domains/${domainId}/analytics/visitors`).then(r => setVisitors(r.data)).catch(() => setVisitors(null));
  }, [domainId]);

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Analytics</h2>

      {serverStats && (
        <div className="grid grid-cols-4 gap-4 mb-6">
          <div className="bg-white rounded-lg shadow p-4">
            <p className="text-sm text-gray-500">Load Average</p>
            <p className="text-xl font-bold">{serverStats.loadAverage?.split(' ')[0] || '-'}</p>
          </div>
          <div className="bg-white rounded-lg shadow p-4">
            <p className="text-sm text-gray-500">Memory</p>
            <p className="text-xl font-bold">{serverStats.usedMemoryBytes ? `${Math.round(serverStats.usedMemoryBytes / 1024 / 1024)}MB` : '-'}</p>
          </div>
          <div className="bg-white rounded-lg shadow p-4">
            <p className="text-sm text-gray-500">Disk Used</p>
            <p className="text-xl font-bold">{serverStats.usedDiskBytes ? `${Math.round(serverStats.usedDiskBytes / 1024 / 1024 / 1024)}GB` : '-'}</p>
          </div>
          <div className="bg-white rounded-lg shadow p-4">
            <p className="text-sm text-gray-500">Uptime</p>
            <p className="text-xl font-bold">{serverStats.uptimeSeconds ? `${Math.round(serverStats.uptimeSeconds / 3600)}h` : '-'}</p>
          </div>
        </div>
      )}

      <div className="bg-white rounded-lg shadow p-6">
        <div className="flex justify-between items-center mb-4">
          <h3 className="text-lg font-semibold">Domain Visitors</h3>
          <select value={domainId ?? ''} onChange={e => setDomainId(Number(e.target.value))} className="border rounded px-3 py-2">
            {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
        </div>
        {visitors ? (
          <div className="grid grid-cols-3 gap-4">
            <div className="bg-gray-50 p-4 rounded">
              <p className="text-sm text-gray-500">Total Hits</p>
              <p className="text-2xl font-bold">{visitors.totalHits?.toLocaleString() ?? 0}</p>
            </div>
            <div className="bg-gray-50 p-4 rounded">
              <p className="text-sm text-gray-500">Unique Visitors</p>
              <p className="text-2xl font-bold">{visitors.uniqueVisitors?.toLocaleString() ?? 0}</p>
            </div>
            <div className="bg-gray-50 p-4 rounded">
              <p className="text-sm text-gray-500">Period</p>
              <p className="text-2xl font-bold">{visitors.period ?? 'today'}</p>
            </div>
          </div>
        ) : <p className="text-gray-500">Select a domain to view analytics.</p>}
      </div>
    </div>
  );
}
