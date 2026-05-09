import { useEffect, useState } from 'react';
import api from '../api/client';

interface Mailbox {
  id: number;
  localPart: string;
  email: string;
  quotaMb: number;
  usedMb: number;
  createdAt: string;
}

interface Alias {
  id: number;
  source: string;
  destination: string;
}

export function EmailPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [mailboxes, setMailboxes] = useState<Mailbox[]>([]);
  const [aliases, setAliases] = useState<Alias[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Create mailbox form
  const [localPart, setLocalPart] = useState('');
  const [password, setPassword] = useState('');
  const [quotaMb, setQuotaMb] = useState(500);
  const [creating, setCreating] = useState(false);

  // Create alias form
  const [aliasSource, setAliasSource] = useState('');
  const [aliasDestination, setAliasDestination] = useState('');
  const [creatingAlias, setCreatingAlias] = useState(false);

  useEffect(() => {
    api.get('/domains').then((res) => {
      setDomains(res.data);
      if (res.data.length > 0) setDomainId(res.data[0].id);
    }).finally(() => setLoading(false));
  }, []);

  const loadData = () => {
    if (!domainId) return;
    setLoading(true);
    setError('');
    Promise.all([
      api.get(`/domains/${domainId}/mailboxes`),
      api.get(`/domains/${domainId}/aliases`),
    ])
      .then(([mbRes, alRes]) => {
        setMailboxes(mbRes.data);
        setAliases(alRes.data);
      })
      .catch((err) => setError(err.response?.data?.message || 'Failed to load email data'))
      .finally(() => setLoading(false));
  };

  useEffect(() => { loadData(); }, [domainId]);

  const handleCreateMailbox = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!domainId) return;
    setCreating(true);
    try {
      await api.post(`/domains/${domainId}/mailboxes`, { localPart, password, quotaMb });
      setLocalPart('');
      setPassword('');
      setQuotaMb(500);
      loadData();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to create mailbox');
    } finally {
      setCreating(false);
    }
  };

  const handleDeleteMailbox = async (id: number, email: string) => {
    if (!confirm(`Delete mailbox ${email}?`)) return;
    try {
      await api.delete(`/mailboxes/${id}`);
      loadData();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to delete mailbox');
    }
  };

  const handleCreateAlias = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!domainId) return;
    setCreatingAlias(true);
    try {
      await api.post(`/domains/${domainId}/aliases`, { source: aliasSource, destination: aliasDestination });
      setAliasSource('');
      setAliasDestination('');
      loadData();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to create alias');
    } finally {
      setCreatingAlias(false);
    }
  };

  const selectedDomain = domains.find((d) => d.id === domainId);

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Email</h2>
        <select
          value={domainId ?? ''}
          onChange={(e) => setDomainId(Number(e.target.value))}
          className="border border-gray-300 rounded px-3 py-2"
        >
          {domains.map((d) => (
            <option key={d.id} value={d.id}>{d.name}</option>
          ))}
        </select>
      </div>

      {error && (
        <div className="mb-4 p-3 bg-red-50 text-red-700 rounded">{error}</div>
      )}

      {/* Create Mailbox Form */}
      <div className="bg-white rounded-lg shadow p-6 mb-6">
        <h3 className="text-lg font-semibold text-gray-900 mb-4">Create Mailbox</h3>
        <form onSubmit={handleCreateMailbox} className="flex items-end gap-4 flex-wrap">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Local Part</label>
            <div className="flex items-center gap-1">
              <input
                type="text"
                value={localPart}
                onChange={(e) => setLocalPart(e.target.value)}
                placeholder="user"
                required
                className="border border-gray-300 rounded px-3 py-2 w-40"
              />
              <span className="text-gray-500">@{selectedDomain?.name || '...'}</span>
            </div>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Password</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              className="border border-gray-300 rounded px-3 py-2 w-48"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Quota (MB)</label>
            <input
              type="number"
              value={quotaMb}
              onChange={(e) => setQuotaMb(Number(e.target.value))}
              min={1}
              className="border border-gray-300 rounded px-3 py-2 w-24"
            />
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

      {/* Mailboxes Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden mb-6">
        <div className="px-6 py-4 border-b border-gray-200">
          <h3 className="text-lg font-semibold text-gray-900">Mailboxes</h3>
        </div>
        {loading ? (
          <div className="p-8 text-center text-gray-500">Loading...</div>
        ) : (
          <table className="w-full">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Email</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Quota</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Used</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Created</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {mailboxes.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-6 py-8 text-center text-gray-500">No mailboxes yet.</td>
                </tr>
              ) : (
                mailboxes.map((mb) => (
                  <tr key={mb.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 font-medium text-gray-900">{mb.email}</td>
                    <td className="px-6 py-4 text-sm text-gray-500">{mb.quotaMb} MB</td>
                    <td className="px-6 py-4 text-sm text-gray-500">{mb.usedMb} MB</td>
                    <td className="px-6 py-4 text-sm text-gray-500">{new Date(mb.createdAt).toLocaleDateString()}</td>
                    <td className="px-6 py-4">
                      <button
                        onClick={() => handleDeleteMailbox(mb.id, mb.email)}
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

      {/* Aliases Section */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200">
          <h3 className="text-lg font-semibold text-gray-900">Aliases</h3>
        </div>
        <div className="p-6 border-b border-gray-200">
          <form onSubmit={handleCreateAlias} className="flex items-end gap-4 flex-wrap">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Source</label>
              <input
                type="text"
                value={aliasSource}
                onChange={(e) => setAliasSource(e.target.value)}
                placeholder="alias@domain.com"
                required
                className="border border-gray-300 rounded px-3 py-2 w-56"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Destination</label>
              <input
                type="text"
                value={aliasDestination}
                onChange={(e) => setAliasDestination(e.target.value)}
                placeholder="user@domain.com"
                required
                className="border border-gray-300 rounded px-3 py-2 w-56"
              />
            </div>
            <button
              type="submit"
              disabled={creatingAlias}
              className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
            >
              {creatingAlias ? 'Adding...' : 'Add Alias'}
            </button>
          </form>
        </div>
        <table className="w-full">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Source</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Destination</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {aliases.length === 0 ? (
              <tr>
                <td colSpan={2} className="px-6 py-8 text-center text-gray-500">No aliases configured.</td>
              </tr>
            ) : (
              aliases.map((a) => (
                <tr key={a.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 text-sm text-gray-900">{a.source}</td>
                  <td className="px-6 py-4 text-sm text-gray-500">{a.destination}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
