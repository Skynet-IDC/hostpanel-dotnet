import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Dialog, Empty, Field, IconBtn, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface DbRow { id: number; name: string; type: string; username: string; createdAt: string; subscriptionId: number; subscriptionName: string }
interface Sub { id: number; name: string }

const DB_TYPES = ['MSSQL', 'MySQL', 'MariaDB', 'PostgreSQL', 'SQLite'] as const;

interface DbStatus { type: string; available: boolean; serviceActive?: boolean; version?: string }

export function DatabasesPage() {
  const [databases, setDatabases] = useState<DbRow[]>([]);
  const [subs, setSubs] = useState<Sub[]>([]);
  const [supportedTypes, setSupportedTypes] = useState<string[]>([]);
  const [engineStatus, setEngineStatus] = useState<DbStatus[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  const load = async () => {
    setErr(null);
    try {
      const [db, s] = await Promise.all([api.get<DbRow[]>('/databases'), api.get<Sub[]>('/subscriptions')]);
      setDatabases(db.data);
      setSubs(s.data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Load failed');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  useEffect(() => {
    (async () => {
      try {
        // /databases/types returns the enum-with-numeric-value shape
        // [{type:"MsSql",value:0}, ...]; older builds returned bare string[]
        // or {types:string[]}. Normalize all three into plain string[].
        const t = await api.get<Array<{ type?: string; value?: number }> | string[] | { types?: string[] }>('/databases/types');
        const raw = Array.isArray(t.data) ? t.data : (t.data?.types ?? []);
        const list: string[] = (raw as Array<unknown>).map(x =>
          typeof x === 'string' ? x : ((x as { type?: string })?.type ?? '')
        ).filter(Boolean);
        setSupportedTypes(list);
      } catch { /* fall through */ }
      try {
        // /databases/status returns [{engine, available, ...}]; older builds
        // used {type, available}. Adapt to the local DbStatus shape which
        // keys on `type`.
        const s = await api.get<Array<{ engine?: string; type?: string; available?: boolean; serviceActive?: boolean; version?: string }> | { engines?: unknown[] }>('/databases/status');
        const raw: Array<{ engine?: string; type?: string; available?: boolean; serviceActive?: boolean; version?: string }> =
          Array.isArray(s.data) ? s.data : ((s.data?.engines ?? []) as Array<{ engine?: string; type?: string; available?: boolean; serviceActive?: boolean; version?: string }>);
        const list: DbStatus[] = raw.map(x => ({
          type: x.engine ?? x.type ?? '',
          available: x.available ?? false,
          serviceActive: x.serviceActive,
          version: x.version,
        }));
        setEngineStatus(list);
      } catch { /* fall through */ }
    })();
  }, []);

  const remove = async (id: number, name: string) => {
    if (!confirm(`Delete database ${name}? Cannot be undone.`)) return;
    await api.delete(`/databases/${id}`);
    void load();
  };

  return (
    <>
      <PageHead
        title="Databases"
        sub={`${databases.length} database${databases.length === 1 ? '' : 's'} · MySQL / Postgres / MSSQL`}
        actions={
          <button className="btn primary" onClick={() => setCreateOpen(true)} disabled={subs.length === 0}><Icons.plus />New database</button>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {(supportedTypes.length > 0 || engineStatus.length > 0) && (
        <Card title="Supported engines" sub="Service status on this server" style={{ marginBottom: 12 }}>
          <div className="row gap-2" style={{ flexWrap: 'wrap' }}>
            {(engineStatus.length > 0 ? engineStatus : supportedTypes.map(t => ({ type: t, available: true } as DbStatus))).map(e => (
              <Badge key={e.type} tone={e.serviceActive === false ? 'danger' : e.available === false ? 'warn' : 'success'} dot>
                {e.type}{e.version ? ` ${e.version}` : ''}{e.serviceActive === false ? ' (down)' : ''}
              </Badge>
            ))}
          </div>
        </Card>
      )}

      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : databases.length === 0 ? <Empty icon="database" title="No databases yet" sub="Create one to back a site or service." />
          : (
            <table className="tbl">
              <thead><tr><th>Name</th><th>Type</th><th>Username</th><th>Subscription</th><th>Created</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {databases.map(db => (
                  <tr key={db.id}>
                    <td className="mono" style={{ fontWeight: 600 }}>{db.name}</td>
                    <td><Badge tone="primary">{db.type}</Badge></td>
                    <td className="mono muted">{db.username}</td>
                    <td>{db.subscriptionName}</td>
                    <td className="muted mono" style={{ fontSize: 11.5 }}>{new Date(db.createdAt).toLocaleDateString()}</td>
                    <td><div className="row-actions"><IconBtn icon="trash" danger title="Delete" onClick={() => remove(db.id, db.name)} /></div></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </Card>

      <CreateDialog open={createOpen} onClose={() => setCreateOpen(false)} subs={subs} onSaved={() => { setCreateOpen(false); void load(); }} />
    </>
  );
}

function CreateDialog({ open, onClose, subs, onSaved }: { open: boolean; onClose: () => void; subs: Sub[]; onSaved: () => void }) {
  const [subId, setSubId] = useState<number | null>(null);
  const [name, setName] = useState('');
  const [type, setType] = useState<typeof DB_TYPES[number]>('MySQL');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => { if (open) { setSubId(subs[0]?.id ?? null); setName(''); setType('MySQL'); setErr(null); } }, [open, subs]);

  const submit = async () => {
    if (!subId) return;
    setBusy(true); setErr(null);
    try {
      await api.post('/databases', { subscriptionId: subId, name, type });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Create failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} title="New database"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !name}>{busy ? 'Creating…' : 'Create'}</button>
      </>}>
      <div className="grid-2" style={{ gap: 12 }}>
        <Field label="Subscription"><select className="select" value={subId ?? ''} onChange={e => setSubId(Number(e.target.value))}>{subs.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></Field>
        <Field label="Type"><select className="select" value={type} onChange={e => setType(e.target.value as typeof DB_TYPES[number])}>{DB_TYPES.map(t => <option key={t} value={t}>{t}</option>)}</select></Field>
        <Field label="Name" span={2}><input className="input mono" value={name} onChange={e => setName(e.target.value)} placeholder="my_database" autoFocus /></Field>
      </div>
    </Dialog>
  );
}
