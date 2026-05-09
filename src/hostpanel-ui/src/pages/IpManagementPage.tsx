import { useEffect, useState } from 'react';
import api from '../api/client';

export function IpManagementPage() {
  const [addresses, setAddresses] = useState<any[]>([]);
  const [blacklist, setBlacklist] = useState<any[]>([]);
  const [ipForm, setIpForm] = useState({ address: '', isShared: true, note: '' });
  const [banForm, setBanForm] = useState({ address: '', reason: '', expiresInHours: '' });

  const load = () => {
    api.get('/ip/addresses').then(r => setAddresses(r.data)).catch(() => {});
    api.get('/ip/blacklist').then(r => setBlacklist(r.data)).catch(() => {});
  };
  useEffect(() => { load(); }, []);

  const addIp = async () => { await api.post('/ip/addresses', ipForm); load(); setIpForm({ address: '', isShared: true, note: '' }); };
  const removeIp = async (id: number) => { await api.delete(`/ip/addresses/${id}`); load(); };
  const ban = async () => {
    await api.post('/ip/blacklist', { ...banForm, expiresInHours: banForm.expiresInHours ? Number(banForm.expiresInHours) : null });
    load(); setBanForm({ address: '', reason: '', expiresInHours: '' });
  };
  const unban = async (id: number) => { await api.delete(`/ip/blacklist/${id}`); load(); };

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">IP Management</h2>
      <div className="grid grid-cols-2 gap-6">
        <div>
          <div className="bg-white rounded-lg shadow p-6 mb-4">
            <h3 className="text-lg font-semibold mb-3">Add IP Address</h3>
            <div className="space-y-2">
              <input placeholder="IP Address" value={ipForm.address} onChange={e => setIpForm({...ipForm, address: e.target.value})} className="w-full border rounded px-3 py-2" />
              <input placeholder="Note" value={ipForm.note} onChange={e => setIpForm({...ipForm, note: e.target.value})} className="w-full border rounded px-3 py-2" />
              <label className="flex items-center gap-2"><input type="checkbox" checked={ipForm.isShared} onChange={e => setIpForm({...ipForm, isShared: e.target.checked})} /> Shared</label>
              <button onClick={addIp} className="bg-blue-600 text-white rounded px-4 py-2 hover:bg-blue-700 w-full">Add</button>
            </div>
          </div>
          <div className="bg-white rounded-lg shadow p-6">
            <h3 className="text-lg font-semibold mb-3">IP Pool ({addresses.length})</h3>
            {addresses.map(a => (
              <div key={a.id} className="flex justify-between items-center py-2 border-b last:border-0">
                <div><span className="font-mono">{a.address}</span> <span className="text-xs text-gray-400">{a.isShared ? 'shared' : 'dedicated'}</span></div>
                <button onClick={() => removeIp(a.id)} className="text-red-600 text-sm">Remove</button>
              </div>
            ))}
          </div>
        </div>
        <div>
          <div className="bg-white rounded-lg shadow p-6 mb-4">
            <h3 className="text-lg font-semibold mb-3">Block IP</h3>
            <div className="space-y-2">
              <input placeholder="IP to block" value={banForm.address} onChange={e => setBanForm({...banForm, address: e.target.value})} className="w-full border rounded px-3 py-2" />
              <input placeholder="Reason" value={banForm.reason} onChange={e => setBanForm({...banForm, reason: e.target.value})} className="w-full border rounded px-3 py-2" />
              <input placeholder="Expires in hours (empty=permanent)" value={banForm.expiresInHours} onChange={e => setBanForm({...banForm, expiresInHours: e.target.value})} className="w-full border rounded px-3 py-2" />
              <button onClick={ban} className="bg-red-600 text-white rounded px-4 py-2 hover:bg-red-700 w-full">Block</button>
            </div>
          </div>
          <div className="bg-white rounded-lg shadow p-6">
            <h3 className="text-lg font-semibold mb-3">Blacklist ({blacklist.length})</h3>
            {blacklist.map(b => (
              <div key={b.id} className="flex justify-between items-center py-2 border-b last:border-0">
                <div><span className="font-mono">{b.address}</span> <span className="text-xs text-gray-400">{b.reason}</span></div>
                <button onClick={() => unban(b.id)} className="text-green-600 text-sm">Unblock</button>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
