import { useEffect, useState } from 'react';
import api from '../api/client';

interface WpSite {
  id: number;
  domainName: string;
  url: string;
  version: string;
  autoUpdate: boolean;
  maintenanceMode: boolean;
  securityStatus: string;
}

const SECURITY_COLORS: Record<string, string> = {
  secure: 'bg-green-100 text-green-700',
  warning: 'bg-yellow-100 text-yellow-700',
  vulnerable: 'bg-red-100 text-red-700',
  unknown: 'bg-gray-100 text-gray-500',
};

export function WordPressPage() {
  const [sites, setSites] = useState<WpSite[]>([]);
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Install form
  const [showInstall, setShowInstall] = useState(false);
  const [installDomainId, setInstallDomainId] = useState<number | ''>('');
  const [installTitle, setInstallTitle] = useState('');
  const [installAdminUser, setInstallAdminUser] = useState('admin');
  const [installAdminEmail, setInstallAdminEmail] = useState('');
  const [installAdminPass, setInstallAdminPass] = useState('');
  const [installing, setInstalling] = useState(false);

  const loadSites = () => {
    setLoading(true);
    setError('');
    api.get('/wordpress')
      .then((res) => setSites(res.data))
      .catch((err) => setError(err.response?.data?.message || 'Failed to load WordPress sites'))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    Promise.all([
      api.get('/wordpress'),
      api.get('/domains'),
    ]).then(([wpRes, domRes]) => {
      setSites(wpRes.data);
      setDomains(domRes.data);
      if (domRes.data.length > 0) setInstallDomainId(domRes.data[0].id);
    }).catch((err) => setError(err.response?.data?.message || 'Failed to load data'))
      .finally(() => setLoading(false));
  }, []);

  const handleInstall = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!installDomainId) return;
    setInstalling(true);
    try {
      await api.post('/wordpress/install', {
        domainId: installDomainId,
        siteTitle: installTitle,
        adminUser: installAdminUser,
        adminEmail: installAdminEmail,
        adminPassword: installAdminPass,
      });
      setShowInstall(false);
      setInstallTitle('');
      setInstallAdminUser('admin');
      setInstallAdminEmail('');
      setInstallAdminPass('');
      loadSites();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to install WordPress');
    } finally {
      setInstalling(false);
    }
  };

  const handleAction = async (id: number, action: string) => {
    try {
      await api.post(`/wordpress/${id}/${action}`);
      loadSites();
    } catch (err: any) {
      alert(err.response?.data?.message || `Failed to ${action}`);
    }
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">WordPress</h2>
        <button
          onClick={() => setShowInstall(!showInstall)}
          className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700"
        >
          {showInstall ? 'Cancel' : 'Install WordPress'}
        </button>
      </div>

      {error && (
        <div className="mb-4 p-3 bg-red-50 text-red-700 rounded">{error}</div>
      )}

      {/* Install Form */}
      {showInstall && (
        <div className="bg-white rounded-lg shadow p-6 mb-6">
          <h3 className="text-lg font-semibold text-gray-900 mb-4">Install WordPress</h3>
          <form onSubmit={handleInstall} className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Domain</label>
              <select
                value={installDomainId}
                onChange={(e) => setInstallDomainId(Number(e.target.value))}
                required
                className="border border-gray-300 rounded px-3 py-2 w-full"
              >
                {domains.map((d) => (
                  <option key={d.id} value={d.id}>{d.name}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Site Title</label>
              <input
                type="text"
                value={installTitle}
                onChange={(e) => setInstallTitle(e.target.value)}
                required
                className="border border-gray-300 rounded px-3 py-2 w-full"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Admin Username</label>
              <input
                type="text"
                value={installAdminUser}
                onChange={(e) => setInstallAdminUser(e.target.value)}
                required
                className="border border-gray-300 rounded px-3 py-2 w-full"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Admin Email</label>
              <input
                type="email"
                value={installAdminEmail}
                onChange={(e) => setInstallAdminEmail(e.target.value)}
                required
                className="border border-gray-300 rounded px-3 py-2 w-full"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Admin Password</label>
              <input
                type="password"
                value={installAdminPass}
                onChange={(e) => setInstallAdminPass(e.target.value)}
                required
                className="border border-gray-300 rounded px-3 py-2 w-full"
              />
            </div>
            <div className="flex items-end">
              <button
                type="submit"
                disabled={installing}
                className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
              >
                {installing ? 'Installing...' : 'Install'}
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Sites Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        {loading ? (
          <div className="p-8 text-center text-gray-500">Loading...</div>
        ) : (
          <table className="w-full">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Domain</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">URL</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Version</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Auto-Update</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Maintenance</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Security</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {sites.length === 0 ? (
                <tr>
                  <td colSpan={7} className="px-6 py-8 text-center text-gray-500">No WordPress sites installed.</td>
                </tr>
              ) : (
                sites.map((site) => (
                  <tr key={site.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 font-medium text-gray-900">{site.domainName}</td>
                    <td className="px-6 py-4 text-sm">
                      <a href={site.url} target="_blank" rel="noopener noreferrer" className="text-blue-600 hover:text-blue-800">
                        {site.url}
                      </a>
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-500">{site.version}</td>
                    <td className="px-6 py-4">
                      <span className={`text-xs px-2 py-0.5 rounded ${
                        site.autoUpdate ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'
                      }`}>
                        {site.autoUpdate ? 'On' : 'Off'}
                      </span>
                    </td>
                    <td className="px-6 py-4">
                      <span className={`text-xs px-2 py-0.5 rounded ${
                        site.maintenanceMode ? 'bg-yellow-100 text-yellow-700' : 'bg-gray-100 text-gray-500'
                      }`}>
                        {site.maintenanceMode ? 'On' : 'Off'}
                      </span>
                    </td>
                    <td className="px-6 py-4">
                      <span className={`text-xs px-2 py-0.5 rounded font-medium ${SECURITY_COLORS[site.securityStatus] || SECURITY_COLORS.unknown}`}>
                        {site.securityStatus}
                      </span>
                    </td>
                    <td className="px-6 py-4">
                      <div className="flex gap-2">
                        <button
                          onClick={() => handleAction(site.id, 'staging')}
                          className="text-blue-600 hover:text-blue-800 text-sm"
                        >
                          Staging
                        </button>
                        <button
                          onClick={() => handleAction(site.id, 'security-scan')}
                          className="text-yellow-600 hover:text-yellow-800 text-sm"
                        >
                          Scan
                        </button>
                      </div>
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
