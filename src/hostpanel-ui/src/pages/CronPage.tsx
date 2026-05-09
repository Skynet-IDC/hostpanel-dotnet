import { useEffect, useState } from 'react';
import api from '../api/client';

interface CronJob { id: number; command: string; schedule: string; enabled: boolean; description?: string; }

export function CronPage() {
  const [subscriptionId, setSubscriptionId] = useState<number | null>(null);
  const [subs, setSubs] = useState<{ id: number; systemUsername: string }[]>([]);
  const [jobs, setJobs] = useState<CronJob[]>([]);
  const [form, setForm] = useState({ command: '', schedule: '', description: '' });

  useEffect(() => {
    api.get('/subscriptions').then(r => {
      setSubs(r.data);
      if (r.data.length > 0) setSubscriptionId(r.data[0].id);
    });
  }, []);

  useEffect(() => {
    if (!subscriptionId) return;
    api.get(`/subscriptions/${subscriptionId}/cron`).then(r => setJobs(r.data));
  }, [subscriptionId]);

  const create = async () => {
    await api.post(`/subscriptions/${subscriptionId}/cron`, form);
    api.get(`/subscriptions/${subscriptionId}/cron`).then(r => setJobs(r.data));
    setForm({ command: '', schedule: '', description: '' });
  };

  const remove = async (id: number) => {
    if (!confirm('Delete this cron job?')) return;
    await api.delete(`/subscriptions/${subscriptionId}/cron/${id}`);
    setJobs(jobs.filter(j => j.id !== id));
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Cron Jobs</h2>
        <select value={subscriptionId ?? ''} onChange={e => setSubscriptionId(Number(e.target.value))} className="border rounded px-3 py-2">
          {subs.map(s => <option key={s.id} value={s.id}>{s.systemUsername || `Sub #${s.id}`}</option>)}
        </select>
      </div>

      <div className="bg-white rounded-lg shadow p-6 mb-6">
        <h3 className="text-lg font-semibold mb-3">Add Cron Job</h3>
        <div className="grid grid-cols-4 gap-3">
          <input placeholder="Schedule (e.g. */5 * * * *)" value={form.schedule} onChange={e => setForm({...form, schedule: e.target.value})} className="border rounded px-3 py-2" />
          <input placeholder="Command" value={form.command} onChange={e => setForm({...form, command: e.target.value})} className="border rounded px-3 py-2 col-span-2" />
          <button onClick={create} className="bg-blue-600 text-white rounded px-4 py-2 hover:bg-blue-700">Add</button>
        </div>
      </div>

      <div className="bg-white rounded-lg shadow overflow-hidden">
        <table className="w-full">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Schedule</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Command</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {jobs.map(j => (
              <tr key={j.id}>
                <td className="px-6 py-3 font-mono text-sm">{j.schedule}</td>
                <td className="px-6 py-3 font-mono text-sm">{j.command}</td>
                <td className="px-6 py-3"><span className={`text-xs px-2 py-0.5 rounded ${j.enabled ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>{j.enabled ? 'Active' : 'Disabled'}</span></td>
                <td className="px-6 py-3"><button onClick={() => remove(j.id)} className="text-red-600 hover:text-red-800 text-sm">Delete</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
