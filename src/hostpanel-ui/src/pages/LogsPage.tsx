import { useEffect, useState } from 'react';
import api from '../api/client';
import { useAuth } from '../contexts/AuthContext';
import { Card, Empty, IconBtn, PageHead } from '../components/ui/primitives';

interface DomainRow { id: number; name: string; }

type LogType = 'access' | 'error' | 'mail';

export function LogsPage() {
  const { user } = useAuth();
  const isAdminOrReseller = user?.role === 'Admin' || user?.role === 'Reseller';
  const [domains, setDomains] = useState<DomainRow[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [logType, setLogType] = useState<LogType>('access');
  const [lines, setLines] = useState(200);
  const [content, setContent] = useState<string>('');
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(false);

  useEffect(() => {
    api.get<DomainRow[]>('/domains').then(r => {
      setDomains(r.data);
      if (r.data.length > 0) setDomainId(r.data[0].id);
    }).catch(e => setErr(e instanceof Error ? e.message : 'Failed to load domains'));
  }, []);

  const load = async () => {
    if (logType !== 'mail' && !domainId) return;
    setLoading(true);
    setErr(null);
    try {
      // Mail log endpoint is mounted under any domain id (controller doesn't read it).
      // Pass the first domain id we have OR 0 — backend ignores it for mail.
      const idForRequest = logType === 'mail' ? (domainId ?? domains[0]?.id ?? 0) : domainId;
      const r = await api.get<{ content?: string; lines?: string[] }>(
        `/domains/${idForRequest}/logs/${logType}?lines=${lines}`
      );
      // system.readLog response shape: { content: "..." } or { lines: [...] }
      const text = r.data.content
        ?? (Array.isArray(r.data.lines) ? r.data.lines.join('\n') : '')
        ?? '';
      setContent(text);
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { error?: string } } }).response?.data?.error
        : null;
      setErr(msg ?? (e instanceof Error ? e.message : 'Failed to load log'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, [domainId, logType, lines]);

  useEffect(() => {
    if (!autoRefresh) return;
    const id = setInterval(() => { void load(); }, 5000);
    return () => clearInterval(id);
  }, [autoRefresh, domainId, logType, lines]);

  return (
    <>
      <PageHead
        title="Domain logs"
        sub={domainId ? `${lines} lines from ${logType} log` : 'Pick a domain'}
        actions={
          <>
            <label className="row" style={{ alignItems: 'center', gap: 6, fontSize: 13 }}>
              <input type="checkbox" checked={autoRefresh} onChange={e => setAutoRefresh(e.target.checked)} />
              Auto-refresh (5s)
            </label>
            <IconBtn icon="refresh" title="Refresh now" onClick={load} />
          </>
        }
      />

      <Card>
        <div className="row" style={{ gap: 12, marginBottom: 16, alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <label className="field" style={{ minWidth: 220 }}>
            <span className="field__label">Domain</span>
            <select className="select" value={domainId ?? ''} onChange={e => setDomainId(Number(e.target.value))}>
              {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          </label>
          <div className="tabs">
            <button className={`tab ${logType === 'access' ? 'tab--active' : ''}`} onClick={() => setLogType('access')}>
              Access
            </button>
            <button className={`tab ${logType === 'error' ? 'tab--active' : ''}`} onClick={() => setLogType('error')}>
              Error
            </button>
            {isAdminOrReseller && (
              <button className={`tab ${logType === 'mail' ? 'tab--active' : ''}`} onClick={() => setLogType('mail')}>
                Mail (server)
              </button>
            )}
          </div>
          <label className="field" style={{ width: 120 }}>
            <span className="field__label">Lines</span>
            <select className="select" value={lines} onChange={e => setLines(Number(e.target.value))}>
              <option value={50}>50</option>
              <option value={200}>200</option>
              <option value={500}>500</option>
              <option value={1000}>1000</option>
            </select>
          </label>
        </div>

        {err && <div className="alert alert--error">{err}</div>}
        {loading && content.length === 0 && <Empty title="Loading…" />}
        {!loading && content.length === 0 && !err && (
          <Empty title="Log is empty" sub="No entries in this log file yet." />
        )}
        {content && (
          <pre
            style={{
              maxHeight: 600,
              overflow: 'auto',
              background: 'var(--color-bg-subtle, #111)',
              color: 'var(--color-fg, #ddd)',
              padding: 14,
              borderRadius: 8,
              fontSize: 11,
              lineHeight: 1.5,
              fontFamily: 'ui-monospace, SF Mono, Menlo, Consolas, monospace',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-all',
            }}
          >
            {content}
          </pre>
        )}
      </Card>
    </>
  );
}
