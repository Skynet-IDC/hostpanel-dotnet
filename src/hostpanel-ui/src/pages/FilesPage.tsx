import { useEffect, useState, type ChangeEvent } from 'react';
import api from '../api/client';
import { Card, Empty, IconBtn, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface FileEntry {
  name: string;
  path: string;
  isDirectory: boolean;
  sizeBytes: number;
  modifiedAt: string;
}

function formatSize(bytes: number) {
  if (bytes === 0) return '—';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function FilesPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [currentPath, setCurrentPath] = useState('/');
  const [entries, setEntries] = useState<FileEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    api.get<{ id: number; name: string }[]>('/domains')
      .then(r => { setDomains(r.data); if (r.data.length > 0) setDomainId(r.data[0].id); })
      .finally(() => setLoading(false));
  }, []);

  const load = async () => {
    if (!domainId) return;
    setErr(null);
    try {
      const { data } = await api.get(`/domains/${domainId}/files`, { params: { path: currentPath } });
      setEntries(data.entries ?? []);
    } catch (e) {
      setEntries([]);
      setErr(e instanceof Error ? e.message : 'Failed to load files');
    }
  };

  useEffect(() => { void load(); }, [domainId, currentPath]);

  const goUp = () => {
    const parts = currentPath.split('/').filter(Boolean);
    parts.pop();
    setCurrentPath('/' + parts.join('/'));
  };

  const upload = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file || !domainId) return;
    const fd = new FormData();
    fd.append('file', file);
    try {
      await api.post(`/domains/${domainId}/files/upload?path=${encodeURIComponent(currentPath)}`, fd, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      await load();
    } catch (e2) {
      // UI-BUG-010: response body may be JSON ({message, diskQuotaMb, currentUsageMb,
      // fileSizeMb} from FilesController.Upload's quota 413) OR the stock nginx HTML
      // 413 if the file is larger than client_max_body_size. Walk both shapes so the
      // operator sees something actionable instead of a generic "Upload failed".
      const resp = e2 && typeof e2 === 'object' && 'response' in e2
        ? (e2 as { response?: { status?: number; data?: unknown } }).response
        : null;
      const data = resp?.data;
      let msg: string | null = null;
      if (data && typeof data === 'object' && 'message' in data) {
        const d = data as { message?: string; diskQuotaMb?: number; currentUsageMb?: number; fileSizeMb?: number };
        msg = d.message ?? null;
        if (msg && d.diskQuotaMb && d.currentUsageMb !== undefined && d.fileSizeMb !== undefined)
          msg = `${msg} (${Math.round(d.currentUsageMb)} + ${Math.round(d.fileSizeMb)} > ${d.diskQuotaMb} MB)`;
      } else if (typeof data === 'string') {
        msg = data.match(/<title>([^<]+)<\/title>/i)?.[1] ?? null;
      }
      if (!msg && resp?.status === 413) msg = 'File too large for this server';
      alert(msg ?? 'Upload failed');
    } finally {
      e.target.value = '';
    }
  };

  const remove = async (entry: FileEntry) => {
    if (!domainId || !confirm(`Delete ${entry.name}?`)) return;
    try {
      await api.delete(`/domains/${domainId}/files`, { params: { path: entry.path } });
      await load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Delete failed');
    }
  };

  const mkdir = async () => {
    if (!domainId) return;
    const name = prompt('New folder name:');
    if (!name) return;
    try {
      await api.post(`/domains/${domainId}/files/mkdir`, { path: currentPath, name });
      await load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Mkdir failed');
    }
  };

  const rename = async (entry: FileEntry) => {
    if (!domainId) return;
    const newName = prompt(`Rename "${entry.name}" to:`, entry.name);
    if (!newName || newName === entry.name) return;
    try {
      await api.post(`/domains/${domainId}/files/rename`, { oldPath: entry.path, newName });
      await load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Rename failed');
    }
  };

  return (
    <>
      <PageHead
        title="File manager"
        sub={currentPath}
        actions={
          <>
            {domains.length > 0 && (
              <select
                className="select"
                value={domainId ?? ''}
                onChange={e => { setDomainId(Number(e.target.value)); setCurrentPath('/'); }}
                style={{ maxWidth: 240 }}
              >
                {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select>
            )}
            <button className="btn" onClick={goUp} disabled={currentPath === '/'}><Icons.chevL />Up</button>
            <button className="btn" onClick={mkdir}><Icons.plus />New folder</button>
            <label className="btn primary" style={{ cursor: 'pointer' }}>
              <Icons.upload />Upload
              <input type="file" style={{ display: 'none' }} onChange={upload} />
            </label>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : entries.length === 0 ? <Empty icon="folder" title="Empty directory" sub="Upload a file to get started." />
          : (
            <table className="tbl">
              <thead><tr><th>Name</th><th>Size</th><th>Modified</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {entries.map(entry => {
                  const Icon = entry.isDirectory ? Icons.folder : Icons.file;
                  return (
                    <tr
                      key={entry.path}
                      style={{ cursor: entry.isDirectory ? 'pointer' : 'default' }}
                      onClick={() => entry.isDirectory && setCurrentPath(entry.path)}
                    >
                      <td>
                        <div className="row gap-2">
                          <Icon style={{ color: entry.isDirectory ? 'var(--primary-400)' : 'var(--muted)' }} />
                          <span style={{ fontWeight: entry.isDirectory ? 600 : 400 }}>{entry.name}</span>
                        </div>
                      </td>
                      <td className="mono muted">{entry.isDirectory ? '—' : formatSize(entry.sizeBytes)}</td>
                      <td className="muted mono" style={{ fontSize: 11.5 }}>{new Date(entry.modifiedAt).toLocaleString()}</td>
                      <td onClick={e => e.stopPropagation()}>
                        <div className="row-actions">
                          <IconBtn icon="edit" title="Rename" onClick={() => rename(entry)} />
                          <IconBtn icon="trash" danger title="Delete" onClick={() => remove(entry)} />
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
      </Card>
    </>
  );
}
