import { useEffect, useState } from 'react';
import api from '../api/client';

interface Invoice {
  id: number;
  subscriptionId: number;
  subscriptionName: string;
  amount: number;
  currency: string;
  status: string;
  dueDate: string;
  paidDate: string | null;
}

const STATUS_COLORS: Record<string, string> = {
  paid: 'bg-green-100 text-green-700',
  pending: 'bg-yellow-100 text-yellow-700',
  overdue: 'bg-red-100 text-red-700',
  cancelled: 'bg-gray-100 text-gray-500',
  refunded: 'bg-purple-100 text-purple-700',
};

export function BillingPage() {
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [subscriptions, setSubscriptions] = useState<{ id: number; name: string }[]>([]);
  const [filterSubId, setFilterSubId] = useState<number | ''>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    Promise.all([
      api.get('/billing/invoices'),
      api.get('/subscriptions'),
    ]).then(([invRes, subRes]) => {
      setInvoices(invRes.data);
      setSubscriptions(subRes.data);
    }).catch((err) => setError(err.response?.data?.message || 'Failed to load billing data'))
      .finally(() => setLoading(false));
  }, []);

  const filtered = filterSubId
    ? invoices.filter((inv) => inv.subscriptionId === filterSubId)
    : invoices;

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Billing</h2>
        <select
          value={filterSubId}
          onChange={(e) => setFilterSubId(e.target.value ? Number(e.target.value) : '')}
          className="border border-gray-300 rounded px-3 py-2"
        >
          <option value="">All Subscriptions</option>
          {subscriptions.map((s) => (
            <option key={s.id} value={s.id}>{s.name}</option>
          ))}
        </select>
      </div>

      {error && (
        <div className="mb-4 p-3 bg-red-50 text-red-700 rounded">{error}</div>
      )}

      <div className="bg-white rounded-lg shadow overflow-hidden">
        {loading ? (
          <div className="p-8 text-center text-gray-500">Loading...</div>
        ) : (
          <table className="w-full">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">ID</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Subscription</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Amount</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Currency</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Due Date</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Paid Date</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {filtered.length === 0 ? (
                <tr>
                  <td colSpan={7} className="px-6 py-8 text-center text-gray-500">No invoices found.</td>
                </tr>
              ) : (
                filtered.map((inv) => (
                  <tr key={inv.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 text-sm font-medium text-gray-900">#{inv.id}</td>
                    <td className="px-6 py-4 text-sm text-gray-500">{inv.subscriptionName}</td>
                    <td className="px-6 py-4 text-sm text-gray-900 font-medium">{inv.amount.toFixed(2)}</td>
                    <td className="px-6 py-4 text-sm text-gray-500">{inv.currency}</td>
                    <td className="px-6 py-4">
                      <span className={`text-xs px-2 py-0.5 rounded font-medium ${STATUS_COLORS[inv.status] || 'bg-gray-100 text-gray-500'}`}>
                        {inv.status}
                      </span>
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-500">{new Date(inv.dueDate).toLocaleDateString()}</td>
                    <td className="px-6 py-4 text-sm text-gray-500">
                      {inv.paidDate ? new Date(inv.paidDate).toLocaleDateString() : '-'}
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
