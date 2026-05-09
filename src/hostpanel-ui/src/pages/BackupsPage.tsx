import { useEffect, useState } from 'react';
import api from '../api/client';

export function BackupsPage() {
  const [subs, setSubs] = useState<{ id: number; systemUsername: string }[]>([]);
  const [subId, setSubId] = useState<number | null>(null);
  const [backups, setBackups] = useState<any[]>([]);
  const [creating, setCreating] = useState(false);

  useEffect(() => { api.get('/subscriptions').then(r => { setSubs(r.data); if (r.data.length > 0) setSubId(r.data[0].id); }); }, []);
  useEffect(() => { if (subId) api.get(`/subscriptions/${subId}/backups`).then(r => setBackups(r.data)); }, [subId]);

  const create = async () => {
    if (!subId) return;
    setCreating(true);
    await api.post(`/subscriptions/${subId}/backups`);
    api.get(`/subscriptions/${subId}/backups`).then(r => setBackups(r.data));
    setCreating(false);
  };

  const restore = async (id: number) => {
    if (!confirm('Restore this backup? This will overwrite current files.')) return;
    await api.post(`/backups/${id}/restore`);
    alert('Restore initiated');
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Backups</h2>
        <div className="flex gap-3">
          <select value={subId ?? ''} onChange={e => setSubId(Number(e.target.value))} className="border rounded px-3 py-2">
            {subs.map(s => <option key={s.id} value={s.id}>{s.systemUsername || `Sub #${s.id}`}</option>)}
          </select>
          <button onClick={create} disabled={creating} className="bg-blue-600 text-white rounded px-4 py-2 hover:bg-blue-700 disabled:opacity-50">
            {creating ? 'Creating...' : 'Create Backup'}
          </button>
        </div>
      </div>
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <table className="w-full">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">ID</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Type</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Size</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Created</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {backups.map(b => (
              <tr key={b.id}>
                <td className="px-6 py-3">{b.id}</td>
                <td className="px-6 py-3">{b.type}</td>
                <td className="px-6 py-3">{(b.sizeBytes / 1024 / 1024).toFixed(1)} MB</td>
                <td className="px-6 py-3"><span className={`text-xs px-2 py-0.5 rounded ${b.isCompleted ? 'bg-green-100 text-green-700' : 'bg-yellow-100 text-yellow-700'}`}>{b.isCompleted ? 'Complete' : 'In Progress'}</span></td>
                <td className="px-6 py-3 text-sm">{new Date(b.createdAt).toLocaleString()}</td>
                <td className="px-6 py-3"><button onClick={() => restore(b.id)} className="text-blue-600 hover:text-blue-800 text-sm">Restore</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
