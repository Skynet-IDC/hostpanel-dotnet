import { useEffect, useState } from 'react';
import api from '../api/client';

interface Database {
  id: number;
  name: string;
  type: string;
  username: string;
  createdAt: string;
  subscriptionId: number;
  subscriptionName: string;
}

interface Subscription {
  id: number;
  name: string;
}

const DB_TYPES = ['MSSQL', 'MySQL', 'MariaDB', 'PostgreSQL', 'SQLite'];

export function DatabasesPage() {
  const [databases, setDatabases] = useState<Database[]>([]);
  const [subscriptions, setSubscriptions] = useState<Subscription[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Form state
  const [formSubId, setFormSubId] = useState<number | ''>('');
  const [formName, setFormName] = useState('');
  const [formType, setFormType] = useState('MySQL');
  const [creating, setCreating] = useState(false);

  const loadDatabases = () => {
    setLoading(true);
    setError('');
    api.get('/databases')
      .then((res) => setDatabases(res.data))
      .catch((err) => setError(err.response?.data?.message || 'Failed to load databases'))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    Promise.all([
      api.get('/databases'),
      api.get('/subscriptions'),
    ]).then(([dbRes, subRes]) => {
      setDatabases(dbRes.data);
      setSubscriptions(subRes.data);
      if (subRes.data.length > 0) setFormSubId(subRes.data[0].id);
    }).catch((err) => setError(err.response?.data?.message || 'Failed to load data'))
      .finally(() => setLoading(false));
  }, []);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formSubId) return;
    setCreating(true);
    try {
      await api.post('/databases', {
        subscriptionId: formSubId,
        name: formName,
        type: formType,
      });
      setFormName('');
      loadDatabases();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to create database');
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (id: number, name: string) => {
    if (!confirm(`Delete database ${name}? This cannot be undone.`)) return;
    try {
      await api.delete(`/databases/${id}`);
      loadDatabases();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to delete database');
    }
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Databases</h2>
      </div>

      {error && (
        <div className="mb-4 p-3 bg-red-50 text-red-700 rounded">{error}</div>
      )}

      {/* Create Database Form */}
      <div className="bg-white rounded-lg shadow p-6 mb-6">
        <h3 className="text-lg font-semibold text-gray-900 mb-4">Create Database</h3>
        <form onSubmit={handleCreate} className="flex items-end gap-4 flex-wrap">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Subscription</label>
            <select
              value={formSubId}
              onChange={(e) => setFormSubId(Number(e.target.value))}
              required
              className="border border-gray-300 rounded px-3 py-2"
            >
              {subscriptions.map((s) => (
                <option key={s.id} value={s.id}>{s.name}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
            <input
              type="text"
              value={formName}
              onChange={(e) => setFormName(e.target.value)}
              placeholder="my_database"
              required
              className="border border-gray-300 rounded px-3 py-2 w-48"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Type</label>
            <select
              value={formType}
              onChange={(e) => setFormType(e.target.value)}
              className="border border-gray-300 rounded px-3 py-2"
            >
              {DB_TYPES.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
          </div>
          <button
            type="submit"
            disabled={creating}
            className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
          >
            {creating ? 'Creating...' : 'Create'}
          </button>
        </form>
      </div>

      {/* Databases Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        {loading ? (
          <div className="p-8 text-center text-gray-500">Loading...</div>
        ) : (
          <table className="w-full">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Name</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Type</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Username</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Subscription</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Created</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {databases.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-6 py-8 text-center text-gray-500">No databases yet.</td>
                </tr>
              ) : (
                databases.map((db) => (
                  <tr key={db.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 font-medium text-gray-900 font-mono">{db.name}</td>
                    <td className="px-6 py-4">
                      <span className="text-xs bg-purple-100 text-purple-700 px-2 py-0.5 rounded">{db.type}</span>
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-500 font-mono">{db.username}</td>
                    <td className="px-6 py-4 text-sm text-gray-500">{db.subscriptionName}</td>
                    <td className="px-6 py-4 text-sm text-gray-500">{new Date(db.createdAt).toLocaleDateString()}</td>
                    <td className="px-6 py-4">
                      <button
                        onClick={() => handleDelete(db.id, db.name)}
                        className="text-red-600 hover:text-red-800 text-sm"
                      >
                        Delete
                      </button>
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
