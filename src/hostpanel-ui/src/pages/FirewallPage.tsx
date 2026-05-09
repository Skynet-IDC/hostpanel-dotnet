import { useEffect, useState } from 'react';
import api from '../api/client';

export function FirewallPage() {
  const [rules, setRules] = useState('');
  const [form, setForm] = useState({ action: 'allow', port: '', protocol: 'tcp', fromIp: '', comment: '' });
  const [loading, setLoading] = useState(true);

  const loadRules = () => {
    api.get('/firewall/rules').then(r => setRules(r.data.rules || '')).finally(() => setLoading(false));
  };
  useEffect(() => { loadRules(); }, []);

  const addRule = async () => {
    await api.post('/firewall/rules', form);
    loadRules();
    setForm({ action: 'allow', port: '', protocol: 'tcp', fromIp: '', comment: '' });
  };

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Firewall (UFW)</h2>

      <div className="bg-white rounded-lg shadow p-6 mb-6">
        <h3 className="text-lg font-semibold mb-4">Add Rule</h3>
        <div className="grid grid-cols-5 gap-3">
          <select value={form.action} onChange={e => setForm({...form, action: e.target.value})} className="border rounded px-3 py-2">
            <option value="allow">Allow</option>
            <option value="deny">Deny</option>
          </select>
          <input placeholder="Port" value={form.port} onChange={e => setForm({...form, port: e.target.value})} className="border rounded px-3 py-2" />
          <select value={form.protocol} onChange={e => setForm({...form, protocol: e.target.value})} className="border rounded px-3 py-2">
            <option value="tcp">TCP</option>
            <option value="udp">UDP</option>
            <option value="any">Any</option>
          </select>
          <input placeholder="From IP (optional)" value={form.fromIp} onChange={e => setForm({...form, fromIp: e.target.value})} className="border rounded px-3 py-2" />
          <button onClick={addRule} className="bg-blue-600 text-white rounded px-4 py-2 hover:bg-blue-700">Add</button>
        </div>
      </div>

      <div className="bg-white rounded-lg shadow p-6">
        <h3 className="text-lg font-semibold mb-4">Current Rules</h3>
        {loading ? <p className="text-gray-500">Loading...</p> : (
          <pre className="bg-gray-50 p-4 rounded text-sm font-mono whitespace-pre-wrap">{rules || 'No rules configured'}</pre>
        )}
      </div>
    </div>
  );
}
