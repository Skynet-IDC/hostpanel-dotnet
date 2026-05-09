import { useEffect, useState } from 'react';
import api from '../api/client';

interface SslStatus {
  enabled: boolean;
  provider: string | null;
  expiryDate: string | null;
  daysUntilExpiry: number | null;
  autoRenew: boolean;
}

export function SslPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [ssl, setSsl] = useState<SslStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [issuing, setIssuing] = useState(false);
  const [toggling, setToggling] = useState(false);

  useEffect(() => {
    api.get('/domains').then((res) => {
      setDomains(res.data);
      if (res.data.length > 0) setDomainId(res.data[0].id);
    }).finally(() => setLoading(false));
  }, []);

  const loadSsl = () => {
    if (!domainId) return;
    setLoading(true);
    setError('');
    api.get(`/domains/${domainId}/ssl/status`)
      .then((res) => setSsl(res.data))
      .catch((err) => setError(err.response?.data?.message || 'Failed to load SSL status'))
      .finally(() => setLoading(false));
  };

  useEffect(() => { loadSsl(); }, [domainId]);

  const handleIssue = async () => {
    if (!domainId) return;
    setIssuing(true);
    try {
      await api.post(`/domains/${domainId}/ssl/issue`);
      loadSsl();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to issue certificate');
    } finally {
      setIssuing(false);
    }
  };

  const handleToggleAutoRenew = async () => {
    if (!domainId) return;
    setToggling(true);
    try {
      await api.post(`/domains/${domainId}/ssl/toggle-auto-renew`);
      loadSsl();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to toggle auto-renew');
    } finally {
      setToggling(false);
    }
  };

  const selectedDomain = domains.find((d) => d.id === domainId);

  const expiryColor = (days: number | null) => {
    if (days === null) return 'text-gray-500';
    if (days <= 7) return 'text-red-600';
    if (days <= 30) return 'text-yellow-600';
    return 'text-green-600';
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">SSL / TLS</h2>
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

      {loading ? (
        <div className="text-gray-500">Loading SSL status...</div>
      ) : ssl ? (
        <div className="bg-white rounded-lg shadow p-6">
          <div className="flex items-center justify-between mb-6">
            <h3 className="text-lg font-semibold text-gray-900">
              SSL for {selectedDomain?.name}
            </h3>
            <span className={`text-sm font-medium px-3 py-1 rounded ${
              ssl.enabled
                ? 'bg-green-100 text-green-700'
                : 'bg-gray-100 text-gray-500'
            }`}>
              {ssl.enabled ? 'Enabled' : 'Disabled'}
            </span>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-6 mb-6">
            <div>
              <p className="text-sm text-gray-500 mb-1">Provider</p>
              <p className="text-gray-900 font-medium">{ssl.provider || 'None'}</p>
            </div>
            <div>
              <p className="text-sm text-gray-500 mb-1">Expiry Date</p>
              <p className="text-gray-900 font-medium">
                {ssl.expiryDate ? new Date(ssl.expiryDate).toLocaleDateString() : 'N/A'}
              </p>
            </div>
            <div>
              <p className="text-sm text-gray-500 mb-1">Days Until Expiry</p>
              <p className={`font-medium ${expiryColor(ssl.daysUntilExpiry)}`}>
                {ssl.daysUntilExpiry !== null ? `${ssl.daysUntilExpiry} days` : 'N/A'}
              </p>
            </div>
            <div>
              <p className="text-sm text-gray-500 mb-1">Auto-Renew</p>
              <p className="text-gray-900 font-medium">{ssl.autoRenew ? 'On' : 'Off'}</p>
            </div>
          </div>

          <div className="flex gap-3">
            <button
              onClick={handleIssue}
              disabled={issuing}
              className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
            >
              {issuing ? 'Issuing...' : 'Issue Certificate'}
            </button>
            <button
              onClick={handleToggleAutoRenew}
              disabled={toggling}
              className="px-4 py-2 bg-gray-100 text-gray-700 rounded hover:bg-gray-200 disabled:opacity-50"
            >
              {toggling ? 'Toggling...' : `${ssl.autoRenew ? 'Disable' : 'Enable'} Auto-Renew`}
            </button>
          </div>
        </div>
      ) : (
        <div className="text-gray-500">No SSL data available.</div>
      )}
    </div>
  );
}
