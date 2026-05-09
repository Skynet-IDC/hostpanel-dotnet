import { useEffect, useState } from 'react';
import type { DomainDto } from '../types/domain';
import { getDomains, deleteDomain } from '../api/domains';

export function DomainsPage() {
  const [domains, setDomains] = useState<DomainDto[]>([]);
  const [loading, setLoading] = useState(true);

  const loadDomains = () => {
    getDomains()
      .then(setDomains)
      .catch(console.error)
      .finally(() => setLoading(false));
  };

  useEffect(() => { loadDomains(); }, []);

  const handleDelete = async (id: number, name: string) => {
    if (!confirm(`Delete domain ${name}?`)) return;
    try {
      await deleteDomain(id);
      loadDomains();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to delete domain');
    }
  };

  if (loading) {
    return <div className="text-gray-500">Loading domains...</div>;
  }

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Domains</h2>
      </div>

      <div className="bg-white rounded-lg shadow overflow-hidden">
        <table className="w-full">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Domain</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Document Root</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">PHP</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">SSL</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {domains.length === 0 ? (
              <tr>
                <td colSpan={5} className="px-6 py-8 text-center text-gray-500">
                  No domains yet. Create a subscription to get started.
                </td>
              </tr>
            ) : (
              domains.map((d) => (
                <tr key={d.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4">
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-gray-900">{d.name}</span>
                      {d.isMainDomain && (
                        <span className="text-xs bg-blue-100 text-blue-700 px-2 py-0.5 rounded">Primary</span>
                      )}
                    </div>
                  </td>
                  <td className="px-6 py-4 text-sm text-gray-500 font-mono">{d.documentRoot}</td>
                  <td className="px-6 py-4 text-sm text-gray-500">{d.phpVersion}</td>
                  <td className="px-6 py-4">
                    <span className={`text-xs px-2 py-0.5 rounded ${
                      d.sslEnabled
                        ? 'bg-green-100 text-green-700'
                        : 'bg-gray-100 text-gray-500'
                    }`}>
                      {d.sslEnabled ? 'Active' : 'None'}
                    </span>
                  </td>
                  <td className="px-6 py-4">
                    {!d.isMainDomain && (
                      <button
                        onClick={() => handleDelete(d.id, d.name)}
                        className="text-red-600 hover:text-red-800 text-sm"
                      >
                        Delete
                      </button>
                    )}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
