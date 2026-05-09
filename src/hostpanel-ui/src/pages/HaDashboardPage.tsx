import { useEffect, useState } from 'react';
import api from '../api/client';

interface SyncState {
  serverId: number;
  serverHostname: string;
  serverIp: string;
  role: string;
  lastSyncAt?: string;
  autoFailback: boolean;
  consecutiveFailures: number;
  failoverTriggeredAt?: string;
  failbackCompletedAt?: string;
  syncFiles: boolean;
  syncDatabases: boolean;
  syncEmail: boolean;
  failoverStrategy?: number;
  isHealthy: boolean;
  lastHealthCheck?: string;
}

const strategyNames: Record<number, string> = { 0: 'DNS Swap', 1: 'Floating IP', 2: 'Cloudflare', 3: 'Per-Domain' };
const roleBadge = (role: string) => {
  const c: Record<string, string> = { primary: 'bg-blue-100 text-blue-700', standby: 'bg-yellow-100 text-yellow-700', down: 'bg-red-100 text-red-700', recovering: 'bg-purple-100 text-purple-700', decommissioned: 'bg-gray-100 text-gray-500' };
  return c[role] || 'bg-gray-100 text-gray-500';
};

export function HaDashboardPage() {
  const [states, setStates] = useState<SyncState[]>([]);
  const [loading, setLoading] = useState(true);
  const [configId, setConfigId] = useState<number | null>(null);
  const [cfg, setCfg] = useState({ autoFailback: true, syncFiles: true, syncDatabases: true, syncEmail: true, failoverStrategy: 0, floatingIp: '', cloudflareApiToken: '', cloudflareZoneId: '' });

  const load = () => { api.get('/ha/sync-state').then(r => setStates(r.data)).catch(() => api.get('/ha/status').then(r => setStates(r.data)).catch(() => {})).finally(() => setLoading(false)); };
  useEffect(() => { load(); }, []);

  const failover = async (id: number) => { if (!confirm('FAILOVER: Promote this server to primary?')) return; await api.post('/ha/failover', { promoteServerId: id }); load(); };
  const failback = async (id: number) => { if (!confirm('FAILBACK: Sync changes back and restore as primary?')) return; await api.post('/ha/failback', { recoveredServerId: id }); load(); };
  const promotePermanent = async (id: number) => {
    const decom = confirm('Also decommission the old primary?\n\nOK = Decommission old primary\nCancel = Keep old as standby');
    if (!confirm('PERMANENT PROMOTION: This server will be primary from now on. Continue?')) return;
    await api.post('/ha/promote-permanent', { serverId: id, decommissionOldPrimary: decom });
    load();
  };
  const saveConfig = async () => { if (!configId) return; await api.put(`/ha/sync-state/${configId}`, cfg); setConfigId(null); load(); };

  if (loading) return <p className="text-gray-500">Loading HA cluster...</p>;

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">High Availability</h2>
      <div className="grid grid-cols-1 gap-4 mb-6">
        {states.map(s => (
          <div key={s.serverId} className={`bg-white rounded-lg shadow p-6 border-l-4 ${s.isHealthy ? 'border-green-500' : 'border-red-500'}`}>
            <div className="flex justify-between items-start">
              <div>
                <h3 className="text-lg font-semibold">{s.serverHostname}</h3>
                <p className="text-sm text-gray-500 font-mono">{s.serverIp}</p>
                <div className="flex gap-2 mt-2 flex-wrap">
                  <span className={`text-xs px-2 py-0.5 rounded ${roleBadge(s.role)}`}>{s.role}</span>
                  <span className={`text-xs px-2 py-0.5 rounded ${s.isHealthy ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>{s.isHealthy ? 'Healthy' : 'Down'}</span>
                  {s.failoverStrategy !== undefined && <span className="text-xs px-2 py-0.5 rounded bg-indigo-100 text-indigo-700">{strategyNames[s.failoverStrategy]}</span>}
                  {s.autoFailback && <span className="text-xs px-2 py-0.5 rounded bg-teal-100 text-teal-700">Auto-failback</span>}
                </div>
                <div className="text-xs text-gray-400 mt-2 space-y-0.5">
                  {s.lastSyncAt && <p>Last sync: {new Date(s.lastSyncAt).toLocaleString()}</p>}
                  {s.lastHealthCheck && <p>Health check: {new Date(s.lastHealthCheck).toLocaleString()}</p>}
                  {s.failoverTriggeredAt && <p>Failover: {new Date(s.failoverTriggeredAt).toLocaleString()}</p>}
                  {s.failbackCompletedAt && <p>Failback: {new Date(s.failbackCompletedAt).toLocaleString()}</p>}
                  {s.consecutiveFailures > 0 && <p className="text-red-500">Failures: {s.consecutiveFailures}</p>}
                  <p>Sync: {[s.syncFiles && 'Files', s.syncDatabases && 'DB', s.syncEmail && 'Email'].filter(Boolean).join(', ') || 'None'}</p>
                </div>
              </div>
              <div className="flex flex-col gap-2">
                {s.role === 'standby' && <>
                  <button onClick={() => failover(s.serverId)} className="bg-orange-600 text-white rounded px-3 py-1 text-sm hover:bg-orange-700">Failover</button>
                  <button onClick={() => promotePermanent(s.serverId)} className="bg-red-600 text-white rounded px-3 py-1 text-sm hover:bg-red-700">Make Permanent Primary</button>
                </>}
                {s.role === 'down' && <button onClick={() => failback(s.serverId)} className="bg-green-600 text-white rounded px-3 py-1 text-sm hover:bg-green-700">Failback</button>}
                {s.role !== 'decommissioned' && <button onClick={() => { setConfigId(s.serverId); setCfg({ autoFailback: s.autoFailback, syncFiles: s.syncFiles, syncDatabases: s.syncDatabases, syncEmail: s.syncEmail, failoverStrategy: s.failoverStrategy ?? 0, floatingIp: '', cloudflareApiToken: '', cloudflareZoneId: '' }); }} className="bg-gray-200 text-gray-700 rounded px-3 py-1 text-sm hover:bg-gray-300">Configure</button>}
              </div>
            </div>
          </div>
        ))}
        {states.length === 0 && <p className="text-gray-500">No HA servers configured. Add servers in Server Management, then configure sync here.</p>}
      </div>

      {configId && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={() => setConfigId(null)}>
          <div className="bg-white rounded-lg shadow-xl p-6 w-full max-w-md" onClick={e => e.stopPropagation()}>
            <h3 className="text-lg font-semibold mb-4">HA Configuration</h3>
            <div className="space-y-3">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Failover Strategy</label>
                <select value={cfg.failoverStrategy} onChange={e => setCfg({...cfg, failoverStrategy: Number(e.target.value)})} className="w-full border rounded px-3 py-2">
                  <option value={0}>DNS Swap (free, TTL-dependent)</option>
                  <option value={1}>Floating IP (instant, same LAN)</option>
                  <option value={2}>Cloudflare (free or $5/mo LB)</option>
                  <option value={3}>Per-Domain (selective)</option>
                </select>
              </div>
              {cfg.failoverStrategy === 1 && <input placeholder="Floating IP" value={cfg.floatingIp} onChange={e => setCfg({...cfg, floatingIp: e.target.value})} className="w-full border rounded px-3 py-2" />}
              {cfg.failoverStrategy === 2 && <>
                <input placeholder="Cloudflare API Token" value={cfg.cloudflareApiToken} onChange={e => setCfg({...cfg, cloudflareApiToken: e.target.value})} className="w-full border rounded px-3 py-2" />
                <input placeholder="Cloudflare Zone ID" value={cfg.cloudflareZoneId} onChange={e => setCfg({...cfg, cloudflareZoneId: e.target.value})} className="w-full border rounded px-3 py-2" />
              </>}
              <div className="flex gap-4">
                <label className="flex items-center gap-1"><input type="checkbox" checked={cfg.syncFiles} onChange={e => setCfg({...cfg, syncFiles: e.target.checked})} /> Files</label>
                <label className="flex items-center gap-1"><input type="checkbox" checked={cfg.syncDatabases} onChange={e => setCfg({...cfg, syncDatabases: e.target.checked})} /> Databases</label>
                <label className="flex items-center gap-1"><input type="checkbox" checked={cfg.syncEmail} onChange={e => setCfg({...cfg, syncEmail: e.target.checked})} /> Email</label>
              </div>
              <label className="flex items-center gap-2"><input type="checkbox" checked={cfg.autoFailback} onChange={e => setCfg({...cfg, autoFailback: e.target.checked})} /> Auto-failback when recovered</label>
              <div className="flex gap-2 mt-4">
                <button onClick={saveConfig} className="flex-1 bg-blue-600 text-white rounded px-4 py-2 hover:bg-blue-700">Save</button>
                <button onClick={() => setConfigId(null)} className="flex-1 bg-gray-200 text-gray-700 rounded px-4 py-2 hover:bg-gray-300">Cancel</button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
