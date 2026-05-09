import { useEffect, useState } from 'react';
import api from '../api/client';

interface BrandingSettings {
  panelName: string;
  primaryColor: string;
  sidebarColor: string;
  logoUrl: string | null;
  customCss: string;
  supportUrl: string;
  supportEmail: string;
}

interface TwoFaStatus {
  enabled: boolean;
  qrCodeUrl: string | null;
}

interface ApiKey {
  id: number;
  name: string;
  prefix: string;
  createdAt: string;
  lastUsedAt: string | null;
}

export function SettingsPage() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Branding
  const [branding, setBranding] = useState<BrandingSettings>({
    panelName: '',
    primaryColor: '#3b82f6',
    sidebarColor: '#1e293b',
    logoUrl: null,
    customCss: '',
    supportUrl: '',
    supportEmail: '',
  });
  const [savingBranding, setSavingBranding] = useState(false);

  // 2FA
  const [twoFa, setTwoFa] = useState<TwoFaStatus>({ enabled: false, qrCodeUrl: null });
  const [twoFaCode, setTwoFaCode] = useState('');
  const [settingUp2fa, setSettingUp2fa] = useState(false);

  // API Keys
  const [apiKeys, setApiKeys] = useState<ApiKey[]>([]);
  const [newKeyName, setNewKeyName] = useState('');
  const [newKeyValue, setNewKeyValue] = useState('');
  const [creatingKey, setCreatingKey] = useState(false);

  useEffect(() => {
    setLoading(true);
    Promise.all([
      api.get('/branding'),
      api.get('/auth/2fa/status'),
      api.get('/apikeys'),
    ]).then(([brandRes, tfaRes, keysRes]) => {
      setBranding(brandRes.data);
      setTwoFa(tfaRes.data);
      setApiKeys(keysRes.data);
    }).catch((err) => setError(err.response?.data?.message || 'Failed to load settings'))
      .finally(() => setLoading(false));
  }, []);

  // Branding handlers
  const handleSaveBranding = async (e: React.FormEvent) => {
    e.preventDefault();
    setSavingBranding(true);
    try {
      await api.put('/branding', branding);
      alert('Branding settings saved.');
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to save branding');
    } finally {
      setSavingBranding(false);
    }
  };

  const handleLogoUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const formData = new FormData();
    formData.append('logo', file);
    try {
      const res = await api.post('/branding/logo', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      setBranding((prev) => ({ ...prev, logoUrl: res.data.logoUrl }));
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to upload logo');
    }
    e.target.value = '';
  };

  // 2FA handlers
  const handleSetup2fa = async () => {
    setSettingUp2fa(true);
    try {
      const res = await api.post('/auth/2fa/setup');
      setTwoFa({ enabled: false, qrCodeUrl: res.data.qrCodeUrl });
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to set up 2FA');
    } finally {
      setSettingUp2fa(false);
    }
  };

  const handleVerify2fa = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await api.post('/auth/2fa/verify', { code: twoFaCode });
      setTwoFa((prev) => ({ ...prev, enabled: true }));
      setTwoFaCode('');
      alert('Two-factor authentication enabled.');
    } catch (err: any) {
      alert(err.response?.data?.message || 'Invalid code');
    }
  };

  // API Key handlers
  const handleCreateKey = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreatingKey(true);
    try {
      const res = await api.post('/apikeys', { name: newKeyName });
      setNewKeyValue(res.data.key);
      setNewKeyName('');
      const keysRes = await api.get('/apikeys');
      setApiKeys(keysRes.data);
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to create API key');
    } finally {
      setCreatingKey(false);
    }
  };

  const handleRevokeKey = async (id: number, name: string) => {
    if (!confirm(`Revoke API key "${name}"?`)) return;
    try {
      await api.delete(`/apikeys/${id}`);
      setApiKeys((prev) => prev.filter((k) => k.id !== id));
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to revoke key');
    }
  };

  if (loading) {
    return <div className="text-gray-500">Loading settings...</div>;
  }

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Settings</h2>

      {error && (
        <div className="mb-4 p-3 bg-red-50 text-red-700 rounded">{error}</div>
      )}

      {/* Branding Settings */}
      <div className="bg-white rounded-lg shadow p-6 mb-6">
        <h3 className="text-lg font-semibold text-gray-900 mb-4">Branding</h3>
        <form onSubmit={handleSaveBranding} className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Panel Name</label>
            <input
              type="text"
              value={branding.panelName}
              onChange={(e) => setBranding((p) => ({ ...p, panelName: e.target.value }))}
              className="border border-gray-300 rounded px-3 py-2 w-full"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Primary Color</label>
            <div className="flex items-center gap-2">
              <input
                type="color"
                value={branding.primaryColor}
                onChange={(e) => setBranding((p) => ({ ...p, primaryColor: e.target.value }))}
                className="h-10 w-10 rounded border border-gray-300 cursor-pointer"
              />
              <input
                type="text"
                value={branding.primaryColor}
                onChange={(e) => setBranding((p) => ({ ...p, primaryColor: e.target.value }))}
                className="border border-gray-300 rounded px-3 py-2 w-28 font-mono text-sm"
              />
            </div>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Sidebar Color</label>
            <div className="flex items-center gap-2">
              <input
                type="color"
                value={branding.sidebarColor}
                onChange={(e) => setBranding((p) => ({ ...p, sidebarColor: e.target.value }))}
                className="h-10 w-10 rounded border border-gray-300 cursor-pointer"
              />
              <input
                type="text"
                value={branding.sidebarColor}
                onChange={(e) => setBranding((p) => ({ ...p, sidebarColor: e.target.value }))}
                className="border border-gray-300 rounded px-3 py-2 w-28 font-mono text-sm"
              />
            </div>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Logo</label>
            <div className="flex items-center gap-3">
              {branding.logoUrl && (
                <img src={branding.logoUrl} alt="Logo" className="h-10 w-10 object-contain rounded" />
              )}
              <label className="px-3 py-1.5 bg-gray-100 text-gray-700 rounded hover:bg-gray-200 cursor-pointer text-sm">
                Upload Logo
                <input type="file" accept="image/*" className="hidden" onChange={handleLogoUpload} />
              </label>
            </div>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Support URL</label>
            <input
              type="url"
              value={branding.supportUrl}
              onChange={(e) => setBranding((p) => ({ ...p, supportUrl: e.target.value }))}
              placeholder="https://support.example.com"
              className="border border-gray-300 rounded px-3 py-2 w-full"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Support Email</label>
            <input
              type="email"
              value={branding.supportEmail}
              onChange={(e) => setBranding((p) => ({ ...p, supportEmail: e.target.value }))}
              placeholder="support@example.com"
              className="border border-gray-300 rounded px-3 py-2 w-full"
            />
          </div>
          <div className="sm:col-span-2">
            <label className="block text-sm font-medium text-gray-700 mb-1">Custom CSS</label>
            <textarea
              value={branding.customCss}
              onChange={(e) => setBranding((p) => ({ ...p, customCss: e.target.value }))}
              rows={4}
              placeholder=".sidebar { /* ... */ }"
              className="border border-gray-300 rounded px-3 py-2 w-full font-mono text-sm"
            />
          </div>
          <div className="sm:col-span-2">
            <button
              type="submit"
              disabled={savingBranding}
              className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
            >
              {savingBranding ? 'Saving...' : 'Save Branding'}
            </button>
          </div>
        </form>
      </div>

      {/* Two-Factor Authentication */}
      <div className="bg-white rounded-lg shadow p-6 mb-6">
        <h3 className="text-lg font-semibold text-gray-900 mb-4">Two-Factor Authentication</h3>
        <div className="flex items-center gap-3 mb-4">
          <span className="text-sm text-gray-500">Status:</span>
          <span className={`text-xs px-2 py-0.5 rounded font-medium ${
            twoFa.enabled ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'
          }`}>
            {twoFa.enabled ? 'Enabled' : 'Disabled'}
          </span>
        </div>

        {!twoFa.enabled && !twoFa.qrCodeUrl && (
          <button
            onClick={handleSetup2fa}
            disabled={settingUp2fa}
            className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
          >
            {settingUp2fa ? 'Setting up...' : 'Set Up 2FA'}
          </button>
        )}

        {!twoFa.enabled && twoFa.qrCodeUrl && (
          <div>
            <p className="text-sm text-gray-600 mb-3">Scan this QR code with your authenticator app, then enter the code below to verify.</p>
            <div className="mb-4">
              <img src={twoFa.qrCodeUrl} alt="2FA QR Code" className="w-48 h-48 border rounded" />
            </div>
            <form onSubmit={handleVerify2fa} className="flex items-end gap-3">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Verification Code</label>
                <input
                  type="text"
                  value={twoFaCode}
                  onChange={(e) => setTwoFaCode(e.target.value)}
                  placeholder="123456"
                  required
                  maxLength={6}
                  className="border border-gray-300 rounded px-3 py-2 w-32 font-mono"
                />
              </div>
              <button
                type="submit"
                className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700"
              >
                Verify & Enable
              </button>
            </form>
          </div>
        )}

        {twoFa.enabled && (
          <p className="text-sm text-gray-500">Two-factor authentication is active on your account.</p>
        )}
      </div>

      {/* API Keys */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200">
          <h3 className="text-lg font-semibold text-gray-900">API Keys</h3>
        </div>

        <div className="p-6 border-b border-gray-200">
          <form onSubmit={handleCreateKey} className="flex items-end gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Key Name</label>
              <input
                type="text"
                value={newKeyName}
                onChange={(e) => setNewKeyName(e.target.value)}
                placeholder="My API Key"
                required
                className="border border-gray-300 rounded px-3 py-2 w-56"
              />
            </div>
            <button
              type="submit"
              disabled={creatingKey}
              className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
            >
              {creatingKey ? 'Creating...' : 'Create Key'}
            </button>
          </form>
          {newKeyValue && (
            <div className="mt-3 p-3 bg-green-50 rounded">
              <p className="text-sm text-green-800 mb-1 font-medium">New API key created. Copy it now -- it will not be shown again.</p>
              <code className="text-sm bg-white px-2 py-1 rounded border font-mono select-all">{newKeyValue}</code>
            </div>
          )}
        </div>

        <table className="w-full">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Name</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Key Prefix</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Created</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Last Used</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {apiKeys.length === 0 ? (
              <tr>
                <td colSpan={5} className="px-6 py-8 text-center text-gray-500">No API keys.</td>
              </tr>
            ) : (
              apiKeys.map((key) => (
                <tr key={key.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 font-medium text-gray-900">{key.name}</td>
                  <td className="px-6 py-4 text-sm text-gray-500 font-mono">{key.prefix}...</td>
                  <td className="px-6 py-4 text-sm text-gray-500">{new Date(key.createdAt).toLocaleDateString()}</td>
                  <td className="px-6 py-4 text-sm text-gray-500">
                    {key.lastUsedAt ? new Date(key.lastUsedAt).toLocaleDateString() : 'Never'}
                  </td>
                  <td className="px-6 py-4">
                    <button
                      onClick={() => handleRevokeKey(key.id, key.name)}
                      className="text-red-600 hover:text-red-800 text-sm"
                    >
                      Revoke
                    </button>
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
