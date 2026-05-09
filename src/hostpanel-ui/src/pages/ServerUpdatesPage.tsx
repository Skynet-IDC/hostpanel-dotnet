import { useState } from 'react';
import api from '../api/client';

export function ServerUpdatesPage() {
  const [updates, setUpdates] = useState<string>('');
  const [count, setCount] = useState(0);
  const [loading, setLoading] = useState(false);
  const [installing, setInstalling] = useState(false);

  const checkUpdates = async () => {
    setLoading(true);
    await api.post('/server/updates/check');
    const r = await api.get('/server/updates');
    setUpdates(r.data.packages || '[]');
    setCount(r.data.count || 0);
    setLoading(false);
  };

  const installUpdates = async (securityOnly = false) => {
    if (!confirm(securityOnly ? 'Install security updates?' : 'Install all updates?')) return;
    setInstalling(true);
    await api.post('/server/updates/install', { securityOnly });
    setInstalling(false);
    checkUpdates();
  };

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Server Updates</h2>

      <div className="flex gap-3 mb-6">
        <button onClick={checkUpdates} disabled={loading} className="bg-blue-600 text-white rounded px-4 py-2 hover:bg-blue-700 disabled:opacity-50">
          {loading ? 'Checking...' : 'Check for Updates'}
        </button>
        <button onClick={() => installUpdates(true)} disabled={installing || count === 0} className="bg-yellow-600 text-white rounded px-4 py-2 hover:bg-yellow-700 disabled:opacity-50">
          Security Only
        </button>
        <button onClick={() => installUpdates(false)} disabled={installing || count === 0} className="bg-green-600 text-white rounded px-4 py-2 hover:bg-green-700 disabled:opacity-50">
          {installing ? 'Installing...' : `Install All (${count})`}
        </button>
      </div>

      <div className="bg-white rounded-lg shadow p-6">
        <h3 className="text-lg font-semibold mb-4">Available Updates ({count})</h3>
        {updates ? (
          <pre className="bg-gray-50 p-4 rounded text-sm font-mono whitespace-pre-wrap max-h-96 overflow-y-auto">{
            typeof updates === 'string' ? JSON.parse(updates).map((p: any) => p.full || p.package_name).join('\n') : 'No updates available'
          }</pre>
        ) : (
          <p className="text-gray-500">Click "Check for Updates" to scan.</p>
        )}
      </div>
    </div>
  );
}
