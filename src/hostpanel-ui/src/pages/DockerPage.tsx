import { useEffect, useState } from 'react';
import api from '../api/client';

interface Container {
  id: number;
  name: string;
  image: string;
  status: string;
  ports: string;
}

const STATUS_COLORS: Record<string, string> = {
  running: 'bg-green-100 text-green-700',
  stopped: 'bg-gray-100 text-gray-500',
  exited: 'bg-red-100 text-red-700',
  restarting: 'bg-yellow-100 text-yellow-700',
  created: 'bg-blue-100 text-blue-700',
};

export function DockerPage() {
  const [subscriptions, setSubscriptions] = useState<{ id: number; name: string }[]>([]);
  const [subId, setSubId] = useState<number | null>(null);
  const [containers, setContainers] = useState<Container[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Create form
  const [showForm, setShowForm] = useState(false);
  const [formName, setFormName] = useState('');
  const [formImage, setFormImage] = useState('');
  const [formPorts, setFormPorts] = useState('');
  const [formEnv, setFormEnv] = useState('');
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    api.get('/subscriptions').then((res) => {
      setSubscriptions(res.data);
      if (res.data.length > 0) setSubId(res.data[0].id);
    }).finally(() => setLoading(false));
  }, []);

  const loadContainers = () => {
    if (!subId) return;
    setLoading(true);
    setError('');
    api.get(`/subscriptions/${subId}/docker`)
      .then((res) => setContainers(res.data))
      .catch((err) => setError(err.response?.data?.message || 'Failed to load containers'))
      .finally(() => setLoading(false));
  };

  useEffect(() => { loadContainers(); }, [subId]);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!subId) return;
    setCreating(true);
    try {
      const envVars: Record<string, string> = {};
      if (formEnv.trim()) {
        formEnv.split('\n').forEach((line) => {
          const idx = line.indexOf('=');
          if (idx > 0) {
            envVars[line.substring(0, idx).trim()] = line.substring(idx + 1).trim();
          }
        });
      }
      await api.post(`/subscriptions/${subId}/docker`, {
        name: formName,
        image: formImage,
        ports: formPorts,
        environmentVariables: envVars,
      });
      setFormName('');
      setFormImage('');
      setFormPorts('');
      setFormEnv('');
      setShowForm(false);
      loadContainers();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to create container');
    } finally {
      setCreating(false);
    }
  };

  const handleAction = async (containerId: number, action: string) => {
    if (!subId) return;
    try {
      if (action === 'delete') {
        if (!confirm('Delete this container?')) return;
        await api.delete(`/subscriptions/${subId}/docker/${containerId}`);
      } else {
        await api.post(`/subscriptions/${subId}/docker/${containerId}/${action}`);
      }
      loadContainers();
    } catch (err: any) {
      alert(err.response?.data?.message || `Failed to ${action} container`);
    }
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Docker</h2>
        <div className="flex items-center gap-3">
          <select
            value={subId ?? ''}
            onChange={(e) => setSubId(Number(e.target.value))}
            className="border border-gray-300 rounded px-3 py-2"
          >
            {subscriptions.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
          <button
            onClick={() => setShowForm(!showForm)}
            className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700"
          >
            {showForm ? 'Cancel' : 'New Container'}
          </button>
        </div>
      </div>

      {error && (
        <div className="mb-4 p-3 bg-red-50 text-red-700 rounded">{error}</div>
      )}

      {/* Create Container Form */}
      {showForm && (
        <div className="bg-white rounded-lg shadow p-6 mb-6">
          <h3 className="text-lg font-semibold text-gray-900 mb-4">Create Container</h3>
          <form onSubmit={handleCreate} className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
              <input
                type="text"
                value={formName}
                onChange={(e) => setFormName(e.target.value)}
                placeholder="my-container"
                required
                className="border border-gray-300 rounded px-3 py-2 w-full"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Image</label>
              <input
                type="text"
                value={formImage}
                onChange={(e) => setFormImage(e.target.value)}
                placeholder="nginx:latest"
                required
                className="border border-gray-300 rounded px-3 py-2 w-full"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Ports</label>
              <input
                type="text"
                value={formPorts}
                onChange={(e) => setFormPorts(e.target.value)}
                placeholder="8080:80, 443:443"
                className="border border-gray-300 rounded px-3 py-2 w-full"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Environment Variables</label>
              <textarea
                value={formEnv}
                onChange={(e) => setFormEnv(e.target.value)}
                placeholder={"KEY=value\nANOTHER=value"}
                rows={3}
                className="border border-gray-300 rounded px-3 py-2 w-full font-mono text-sm"
              />
            </div>
            <div className="sm:col-span-2">
              <button
                type="submit"
                disabled={creating}
                className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
              >
                {creating ? 'Creating...' : 'Create'}
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Containers Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        {loading ? (
          <div className="p-8 text-center text-gray-500">Loading...</div>
        ) : (
          <table className="w-full">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Name</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Image</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Ports</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {containers.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-6 py-8 text-center text-gray-500">No containers.</td>
                </tr>
              ) : (
                containers.map((c) => (
                  <tr key={c.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 font-medium text-gray-900">{c.name}</td>
                    <td className="px-6 py-4 text-sm text-gray-500 font-mono">{c.image}</td>
                    <td className="px-6 py-4">
                      <span className={`text-xs px-2 py-0.5 rounded font-medium ${STATUS_COLORS[c.status] || 'bg-gray-100 text-gray-500'}`}>
                        {c.status}
                      </span>
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-500 font-mono">{c.ports || '-'}</td>
                    <td className="px-6 py-4">
                      <div className="flex gap-2">
                        <button
                          onClick={() => handleAction(c.id, 'start')}
                          className="text-green-600 hover:text-green-800 text-sm"
                        >
                          Start
                        </button>
                        <button
                          onClick={() => handleAction(c.id, 'stop')}
                          className="text-yellow-600 hover:text-yellow-800 text-sm"
                        >
                          Stop
                        </button>
                        <button
                          onClick={() => handleAction(c.id, 'restart')}
                          className="text-blue-600 hover:text-blue-800 text-sm"
                        >
                          Restart
                        </button>
                        <button
                          onClick={() => handleAction(c.id, 'delete')}
                          className="text-red-600 hover:text-red-800 text-sm"
                        >
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
