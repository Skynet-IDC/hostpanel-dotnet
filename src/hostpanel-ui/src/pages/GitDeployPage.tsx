import { useEffect, useState } from 'react';
import api from '../api/client';

interface GitConfig {
  repoUrl: string | null;
  branch: string | null;
  provider: string | null;
  lastDeployAt: string | null;
  lastCommitHash: string | null;
  autoDeployEnabled: boolean;
}

export function GitDeployPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [config, setConfig] = useState<GitConfig | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [deploying, setDeploying] = useState(false);

  // Configure form
  const [showForm, setShowForm] = useState(false);
  const [formRepoUrl, setFormRepoUrl] = useState('');
  const [formBranch, setFormBranch] = useState('main');
  const [formProvider, setFormProvider] = useState('github');
  const [formAutoDeploy, setFormAutoDeploy] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    api.get('/domains').then((res) => {
      setDomains(res.data);
      if (res.data.length > 0) setDomainId(res.data[0].id);
    }).finally(() => setLoading(false));
  }, []);

  const loadConfig = () => {
    if (!domainId) return;
    setLoading(true);
    setError('');
    api.get(`/domains/${domainId}/git`)
      .then((res) => {
        setConfig(res.data);
        if (res.data.repoUrl) {
          setFormRepoUrl(res.data.repoUrl);
          setFormBranch(res.data.branch || 'main');
          setFormProvider(res.data.provider || 'github');
          setFormAutoDeploy(res.data.autoDeployEnabled || false);
        }
      })
      .catch((err) => {
        setConfig(null);
        setError(err.response?.data?.message || 'Failed to load git config');
      })
      .finally(() => setLoading(false));
  };

  useEffect(() => { loadConfig(); }, [domainId]);

  const handleSaveConfig = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!domainId) return;
    setSaving(true);
    try {
      await api.post(`/domains/${domainId}/git`, {
        repoUrl: formRepoUrl,
        branch: formBranch,
        provider: formProvider,
        autoDeployEnabled: formAutoDeploy,
      });
      setShowForm(false);
      loadConfig();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to save git config');
    } finally {
      setSaving(false);
    }
  };

  const handleDeploy = async () => {
    if (!domainId) return;
    setDeploying(true);
    try {
      await api.post(`/domains/${domainId}/git/deploy`);
      loadConfig();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Deploy failed');
    } finally {
      setDeploying(false);
    }
  };

  const selectedDomain = domains.find((d) => d.id === domainId);

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Git Deployment</h2>
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
        <div className="text-gray-500">Loading...</div>
      ) : (
        <>
          {/* Current Config */}
          <div className="bg-white rounded-lg shadow p-6 mb-6">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-semibold text-gray-900">
                Configuration for {selectedDomain?.name}
              </h3>
              <div className="flex gap-2">
                <button
                  onClick={() => setShowForm(!showForm)}
                  className="px-3 py-1.5 bg-gray-100 text-gray-700 rounded hover:bg-gray-200 text-sm"
                >
                  {showForm ? 'Cancel' : 'Configure'}
                </button>
                {config?.repoUrl && (
                  <button
                    onClick={handleDeploy}
                    disabled={deploying}
                    className="px-3 py-1.5 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50 text-sm"
                  >
                    {deploying ? 'Deploying...' : 'Deploy Now'}
                  </button>
                )}
              </div>
            </div>

            {config?.repoUrl ? (
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <p className="text-sm text-gray-500 mb-1">Repository URL</p>
                  <p className="text-gray-900 font-mono text-sm">{config.repoUrl}</p>
                </div>
                <div>
                  <p className="text-sm text-gray-500 mb-1">Branch</p>
                  <p className="text-gray-900 font-medium">{config.branch}</p>
                </div>
                <div>
                  <p className="text-sm text-gray-500 mb-1">Provider</p>
                  <p className="text-gray-900 font-medium capitalize">{config.provider}</p>
                </div>
                <div>
                  <p className="text-sm text-gray-500 mb-1">Auto-Deploy</p>
                  <span className={`text-xs px-2 py-0.5 rounded ${
                    config.autoDeployEnabled ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'
                  }`}>
                    {config.autoDeployEnabled ? 'Enabled' : 'Disabled'}
                  </span>
                </div>
                <div>
                  <p className="text-sm text-gray-500 mb-1">Last Deploy</p>
                  <p className="text-gray-900 text-sm">
                    {config.lastDeployAt ? new Date(config.lastDeployAt).toLocaleString() : 'Never'}
                  </p>
                </div>
                <div>
                  <p className="text-sm text-gray-500 mb-1">Last Commit</p>
                  <p className="text-gray-900 font-mono text-sm">
                    {config.lastCommitHash ? config.lastCommitHash.substring(0, 8) : 'N/A'}
                  </p>
                </div>
              </div>
            ) : (
              <p className="text-gray-500">No git repository configured for this domain.</p>
            )}
          </div>

          {/* Configure Form */}
          {showForm && (
            <div className="bg-white rounded-lg shadow p-6">
              <h3 className="text-lg font-semibold text-gray-900 mb-4">Configure Git Repository</h3>
              <form onSubmit={handleSaveConfig} className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div className="sm:col-span-2">
                  <label className="block text-sm font-medium text-gray-700 mb-1">Repository URL</label>
                  <input
                    type="text"
                    value={formRepoUrl}
                    onChange={(e) => setFormRepoUrl(e.target.value)}
                    placeholder="https://github.com/user/repo.git"
                    required
                    className="border border-gray-300 rounded px-3 py-2 w-full"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Branch</label>
                  <input
                    type="text"
                    value={formBranch}
                    onChange={(e) => setFormBranch(e.target.value)}
                    required
                    className="border border-gray-300 rounded px-3 py-2 w-full"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Provider</label>
                  <select
                    value={formProvider}
                    onChange={(e) => setFormProvider(e.target.value)}
                    className="border border-gray-300 rounded px-3 py-2 w-full"
                  >
                    <option value="github">GitHub</option>
                    <option value="gitlab">GitLab</option>
                    <option value="bitbucket">Bitbucket</option>
                    <option value="custom">Custom</option>
                  </select>
                </div>
                <div className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    id="autoDeploy"
                    checked={formAutoDeploy}
                    onChange={(e) => setFormAutoDeploy(e.target.checked)}
                    className="rounded border-gray-300"
                  />
                  <label htmlFor="autoDeploy" className="text-sm font-medium text-gray-700">
                    Enable auto-deploy on push
                  </label>
                </div>
                <div className="flex items-end">
                  <button
                    type="submit"
                    disabled={saving}
                    className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
                  >
                    {saving ? 'Saving...' : 'Save Configuration'}
                  </button>
                </div>
              </form>
            </div>
          )}
        </>
      )}
    </div>
  );
}
