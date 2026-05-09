import { useEffect, useState } from 'react';
import api from '../api/client';

interface DnsRecord {
  id: number;
  type: string;
  name: string;
  value: string;
  ttl: number;
  priority: number | null;
}

const DNS_TYPES = ['A', 'AAAA', 'CNAME', 'MX', 'TXT', 'SRV'];

export function DnsPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [records, setRecords] = useState<DnsRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Form state
  const [formType, setFormType] = useState('A');
  const [formName, setFormName] = useState('');
  const [formValue, setFormValue] = useState('');
  const [formTtl, setFormTtl] = useState(3600);
  const [formPriority, setFormPriority] = useState(10);
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    api.get('/domains').then((res) => {
      setDomains(res.data);
      if (res.data.length > 0) setDomainId(res.data[0].id);
    }).finally(() => setLoading(false));
  }, []);

  const loadRecords = () => {
    if (!domainId) return;
    setLoading(true);
    setError('');
    api.get(`/domains/${domainId}/dns`)
      .then((res) => setRecords(res.data))
      .catch((err) => setError(err.response?.data?.message || 'Failed to load DNS records'))
      .finally(() => setLoading(false));
  };

  useEffect(() => { loadRecords(); }, [domainId]);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!domainId) return;
    setCreating(true);
    try {
      await api.post(`/domains/${domainId}/dns`, {
        type: formType,
        name: formName,
        value: formValue,
        ttl: formTtl,
        priority: (formType === 'MX' || formType === 'SRV') ? formPriority : null,
      });
      setFormName('');
      setFormValue('');
      setFormTtl(3600);
      setFormPriority(10);
      loadRecords();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to create DNS record');
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (id: number, name: string) => {
    if (!confirm(`Delete DNS record ${name}?`)) return;
    try {
      await api.delete(`/dns/${id}`);
      loadRecords();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to delete record');
    }
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">DNS Management</h2>
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

      {/* Add Record Form */}
      <div className="bg-white rounded-lg shadow p-6 mb-6">
        <h3 className="text-lg font-semibold text-gray-900 mb-4">Add DNS Record</h3>
        <form onSubmit={handleCreate} className="flex items-end gap-4 flex-wrap">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Type</label>
            <select
              value={formType}
              onChange={(e) => setFormType(e.target.value)}
              className="border border-gray-300 rounded px-3 py-2"
            >
              {DNS_TYPES.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
            <input
              type="text"
              value={formName}
              onChange={(e) => setFormName(e.target.value)}
              placeholder="@"
              required
              className="border border-gray-300 rounded px-3 py-2 w-40"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Value</label>
            <input
              type="text"
              value={formValue}
              onChange={(e) => setFormValue(e.target.value)}
              placeholder="192.168.1.1"
              required
              className="border border-gray-300 rounded px-3 py-2 w-56"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">TTL</label>
            <input
              type="number"
              value={formTtl}
              onChange={(e) => setFormTtl(Number(e.target.value))}
              min={60}
              className="border border-gray-300 rounded px-3 py-2 w-24"
            />
          </div>
          {(formType === 'MX' || formType === 'SRV') && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Priority</label>
              <input
                type="number"
                value={formPriority}
                onChange={(e) => setFormPriority(Number(e.target.value))}
                min={0}
                className="border border-gray-300 rounded px-3 py-2 w-20"
              />
            </div>
          )}
          <button
            type="submit"
            disabled={creating}
            className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
          >
            {creating ? 'Adding...' : 'Add Record'}
          </button>
        </form>
      </div>

      {/* Records Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        {loading ? (
          <div className="p-8 text-center text-gray-500">Loading...</div>
        ) : (
          <table className="w-full">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Type</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Name</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Value</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">TTL</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Priority</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {records.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-6 py-8 text-center text-gray-500">No DNS records.</td>
                </tr>
              ) : (
                records.map((r) => (
                  <tr key={r.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4">
                      <span className="text-xs font-mono bg-gray-100 text-gray-700 px-2 py-0.5 rounded">{r.type}</span>
                    </td>
                    <td className="px-6 py-4 font-medium text-gray-900 font-mono text-sm">{r.name}</td>
                    <td className="px-6 py-4 text-sm text-gray-500 font-mono max-w-xs truncate">{r.value}</td>
                    <td className="px-6 py-4 text-sm text-gray-500">{r.ttl}</td>
                    <td className="px-6 py-4 text-sm text-gray-500">{r.priority ?? '-'}</td>
                    <td className="px-6 py-4">
                      <button
                        onClick={() => handleDelete(r.id, r.name)}
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
