import { useEffect, useState } from 'react';
import api from '../api/client';

interface AppInfo { id: string; name: string; category: string; description: string; }

export function AppsPage() {
  const [apps, setApps] = useState<AppInfo[]>([]);
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [installing, setInstalling] = useState<string | null>(null);

  useEffect(() => {
    api.get('/domains/0/apps/available').catch(() =>
      // Fallback: apps endpoint doesn't need a real domain ID for listing
      ({ data: [] })
    ).then(r => setApps(r.data));
    api.get('/domains').then(r => setDomains(r.data));
  }, []);

  const install = async (appId: string, domainId: number) => {
    setInstalling(appId);
    try {
      await api.post(`/domains/${domainId}/apps/install`, { appId, adminUser: 'admin', adminPassword: '', adminEmail: '' });
      alert(`${appId} installation started!`);
    } catch (err: any) {
      alert(err.response?.data?.message || 'Install failed');
    }
    setInstalling(null);
  };

  const categories = [...new Set(apps.map(a => a.category))];

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">One-Click Apps</h2>

      {categories.map(cat => (
        <div key={cat} className="mb-8">
          <h3 className="text-lg font-semibold text-gray-700 mb-3">{cat}</h3>
          <div className="grid grid-cols-3 gap-4">
            {apps.filter(a => a.category === cat).map(app => (
              <div key={app.id} className="bg-white rounded-lg shadow p-4">
                <h4 className="font-semibold text-gray-900">{app.name}</h4>
                <p className="text-sm text-gray-500 mb-3">{app.description}</p>
                {domains.length > 0 ? (
                  <select onChange={e => e.target.value && install(app.id, Number(e.target.value))}
                    disabled={installing === app.id}
                    className="w-full border rounded px-3 py-2 text-sm" defaultValue="">
                    <option value="" disabled>{installing === app.id ? 'Installing...' : 'Install on...'}</option>
                    {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
                  </select>
                ) : <p className="text-sm text-gray-400">No domains available</p>}
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}
