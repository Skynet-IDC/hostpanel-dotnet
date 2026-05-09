import { useEffect, useState } from 'react';
import api from '../api/client';

interface FileEntry {
  name: string;
  path: string;
  isDirectory: boolean;
  sizeBytes: number;
  modifiedAt: string;
}

export function FilesPage() {
  const [domainId, setDomainId] = useState<number | null>(null);
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [currentPath, setCurrentPath] = useState('/');
  const [entries, setEntries] = useState<FileEntry[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.get('/domains').then((res) => {
      setDomains(res.data);
      if (res.data.length > 0) setDomainId(res.data[0].id);
    }).finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    if (!domainId) return;
    setLoading(true);
    api.get(`/domains/${domainId}/files`, { params: { path: currentPath } })
      .then((res) => setEntries(res.data.entries))
      .catch(() => setEntries([]))
      .finally(() => setLoading(false));
  }, [domainId, currentPath]);

  const navigate = (entry: FileEntry) => {
    if (entry.isDirectory) {
      setCurrentPath(entry.path);
    }
  };

  const goUp = () => {
    const parts = currentPath.split('/').filter(Boolean);
    parts.pop();
    setCurrentPath('/' + parts.join('/'));
  };

  const handleDelete = async (entry: FileEntry) => {
    if (!confirm(`Delete ${entry.name}?`)) return;
    await api.delete(`/domains/${domainId}/files`, { params: { path: entry.path } });
    // Refresh
    const res = await api.get(`/domains/${domainId}/files`, { params: { path: currentPath } });
    setEntries(res.data.entries);
  };

  const handleUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file || !domainId) return;
    const formData = new FormData();
    formData.append('file', file);
    await api.post(`/domains/${domainId}/files/upload?path=${encodeURIComponent(currentPath)}`, formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
    const res = await api.get(`/domains/${domainId}/files`, { params: { path: currentPath } });
    setEntries(res.data.entries);
    e.target.value = '';
  };

  const formatSize = (bytes: number) => {
    if (bytes === 0) return '-';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-2xl font-bold text-gray-900">File Manager</h2>
        <select
          value={domainId ?? ''}
          onChange={(e) => { setDomainId(Number(e.target.value)); setCurrentPath('/'); }}
          className="border border-gray-300 rounded px-3 py-2"
        >
          {domains.map((d) => (
            <option key={d.id} value={d.id}>{d.name}</option>
          ))}
        </select>
      </div>

      <div className="bg-white rounded-lg shadow">
        {/* Breadcrumb / toolbar */}
        <div className="flex items-center gap-3 p-4 border-b border-gray-200">
          <button onClick={goUp} disabled={currentPath === '/'} className="px-3 py-1 bg-gray-100 rounded hover:bg-gray-200 disabled:opacity-50 text-sm">
            Up
          </button>
          <span className="font-mono text-sm text-gray-600 flex-1">{currentPath}</span>
          <label className="px-3 py-1 bg-blue-600 text-white rounded hover:bg-blue-700 cursor-pointer text-sm">
            Upload
            <input type="file" className="hidden" onChange={handleUpload} />
          </label>
        </div>

        {loading ? (
          <div className="p-8 text-center text-gray-500">Loading...</div>
        ) : (
          <table className="w-full">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Name</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Size</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Modified</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {entries.length === 0 ? (
                <tr><td colSpan={4} className="px-6 py-8 text-center text-gray-500">Empty directory</td></tr>
              ) : entries.map((entry) => (
                <tr key={entry.path} className="hover:bg-gray-50">
                  <td className="px-6 py-3">
                    <button
                      onClick={() => navigate(entry)}
                      className={`flex items-center gap-2 ${entry.isDirectory ? 'text-blue-600 hover:text-blue-800 font-medium' : 'text-gray-900'}`}
                    >
                      <span className="text-lg">{entry.isDirectory ? '📁' : '📄'}</span>
                      {entry.name}
                    </button>
                  </td>
                  <td className="px-6 py-3 text-sm text-gray-500">{formatSize(entry.sizeBytes)}</td>
                  <td className="px-6 py-3 text-sm text-gray-500">{new Date(entry.modifiedAt).toLocaleString()}</td>
                  <td className="px-6 py-3">
                    <button onClick={() => handleDelete(entry)} className="text-red-600 hover:text-red-800 text-sm">Delete</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
