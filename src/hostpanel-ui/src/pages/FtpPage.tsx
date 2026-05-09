import { useEffect, useState } from 'react';
import api from '../api/client';

export function FtpPage() {
  const [subs, setSubs] = useState<{ id: number; systemUsername: string }[]>([]);
  const [subId, setSubId] = useState<number | null>(null);
  const [accounts, setAccounts] = useState<any[]>([]);
  const [form, setForm] = useState({ username: '', password: '', directory: 'httpdocs', quotaMb: 500 });

  useEffect(() => { api.get('/subscriptions').then(r => { setSubs(r.data); if (r.data.length > 0) setSubId(r.data[0].id); }); }, []);
  useEffect(() => { if (subId) api.get(`/subscriptions/${subId}/ftp`).then(r => setAccounts(r.data)).catch(() => setAccounts([])); }, [subId]);

  const create = async () => {
    if (!subId) return;
    await api.post(`/subscriptions/${subId}/ftp`, form);
    api.get(`/subscriptions/${subId}/ftp`).then(r => setAccounts(r.data));
    setForm({ username: '', password: '', directory: 'httpdocs', quotaMb: 500 });
  };

  const remove = async (id: number) => {
    if (!confirm('Delete this FTP account?')) return;
    await api.delete(`/subscriptions/${subId}/ftp/${id}`);
    setAccounts(accounts.filter(a => a.id !== id));
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">FTP Accounts</h2>
        <select value={subId ?? ''} onChange={e => setSubId(Number(e.target.value))} className="border rounded px-3 py-2">
          {subs.map(s => <option key={s.id} value={s.id}>{s.systemUsername || `Sub #${s.id}`}</option>)}
        </select>
      </div>
      <div className="bg-white rounded-lg shadow p-6 mb-6">
        <h3 className="text-lg font-semibold mb-3">Create FTP Account</h3>
        <div className="grid grid-cols-5 gap-3">
          <input placeholder="Username" value={form.username} onChange={e => setForm({...form, username: e.target.value})} className="border rounded px-3 py-2" />
          <input placeholder="Password" type="password" value={form.password} onChange={e => setForm({...form, password: e.target.value})} className="border rounded px-3 py-2" />
          <input placeholder="Directory" value={form.directory} onChange={e => setForm({...form, directory: e.target.value})} className="border rounded px-3 py-2" />
          <input placeholder="Quota MB" type="number" value={form.quotaMb} onChange={e => setForm({...form, quotaMb: Number(e.target.value)})} className="border rounded px-3 py-2" />
          <button onClick={create} className="bg-blue-600 text-white rounded px-4 py-2 hover:bg-blue-700">Create</button>
        </div>
      </div>
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <table className="w-full">
          <thead className="bg-gray-50"><tr>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Username</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Home Dir</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Quota</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
          </tr></thead>
          <tbody className="divide-y divide-gray-200">
            {accounts.map(a => (
              <tr key={a.id}>
                <td className="px-6 py-3 font-mono text-sm">{a.username}</td>
                <td className="px-6 py-3 font-mono text-sm">{a.homeDirectory}</td>
                <td className="px-6 py-3">{a.quotaMb} MB</td>
                <td className="px-6 py-3"><button onClick={() => remove(a.id)} className="text-red-600 hover:text-red-800 text-sm">Delete</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
