import { useEffect, useState } from 'react';
import api from '../api/client';
import { Card, Dialog, Empty, Field, IconBtn, Meter, PageHead, Tabs } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface Mailbox { id: number; localPart: string; email: string; quotaMb: number; usedMb: number; createdAt: string }
interface Alias { id: number; source: string; destination: string }

type TabId = 'mailboxes' | 'aliases';

export function EmailPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [mailboxes, setMailboxes] = useState<Mailbox[]>([]);
  const [aliases, setAliases] = useState<Alias[]>([]);
  const [tab, setTab] = useState<TabId>('mailboxes');
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [mbOpen, setMbOpen] = useState(false);
  const [aliasOpen, setAliasOpen] = useState(false);

  useEffect(() => {
    api.get<{ id: number; name: string }[]>('/domains')
      .then(r => { setDomains(r.data); if (r.data.length > 0) setDomainId(r.data[0].id); })
      .finally(() => setLoading(false));
  }, []);

  const load = async () => {
    if (!domainId) return;
    setErr(null);
    try {
      const [mb, al] = await Promise.all([
        api.get<Mailbox[]>(`/domains/${domainId}/mailboxes`),
        api.get<Alias[]>(`/domains/${domainId}/aliases`),
      ]);
      setMailboxes(mb.data);
      setAliases(al.data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Load failed');
    }
  };

  useEffect(() => { void load(); }, [domainId]);

  const selected = domains.find(d => d.id === domainId);

  const removeMb = async (id: number, email: string) => {
    if (!confirm(`Delete mailbox ${email}?`)) return;
    await api.delete(`/mailboxes/${id}`);
    void load();
  };

  const removeAlias = async (id: number, src: string) => {
    if (!confirm(`Delete alias ${src}?`)) return;
    await api.delete(`/aliases/${id}`);
    void load();
  };

  return (
    <>
      <PageHead
        title="Email"
        sub={selected ? `Mailboxes + aliases for ${selected.name}` : 'Pick a domain'}
        actions={
          <>
            {domains.length > 0 && (
              <select className="select" value={domainId ?? ''} onChange={e => setDomainId(Number(e.target.value))} style={{ maxWidth: 240 }}>
                {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select>
            )}
            <button className="btn primary" onClick={() => tab === 'mailboxes' ? setMbOpen(true) : setAliasOpen(true)} disabled={!domainId}>
              <Icons.plus />{tab === 'mailboxes' ? 'New mailbox' : 'New alias'}
            </button>
          </>
        }
      />

      <Tabs active={tab} onChange={id => setTab(id as TabId)} tabs={[
        { id: 'mailboxes', label: 'Mailboxes', count: mailboxes.length },
        { id: 'aliases', label: 'Aliases', count: aliases.length },
      ]} />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {tab === 'mailboxes' && (
        <Card flush>
          {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
            : mailboxes.length === 0 ? <Empty icon="mail" title="No mailboxes yet" sub="Create one to receive mail at this domain." />
            : (
              <table className="tbl">
                <thead><tr><th>Email</th><th>Quota</th><th>Used</th><th>Created</th><th style={{ textAlign: 'right' }} /></tr></thead>
                <tbody>
                  {mailboxes.map(mb => (
                    <tr key={mb.id}>
                      <td className="mono" style={{ fontWeight: 600 }}>{mb.email}</td>
                      <td className="mono">{mb.quotaMb} MB</td>
                      <td style={{ width: 160 }}><Meter value={mb.usedMb} total={mb.quotaMb} hideText /></td>
                      <td className="muted mono" style={{ fontSize: 11.5 }}>{new Date(mb.createdAt).toLocaleDateString()}</td>
                      <td><div className="row-actions"><IconBtn icon="trash" danger title="Delete" onClick={() => removeMb(mb.id, mb.email)} /></div></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
        </Card>
      )}

      {tab === 'aliases' && (
        <Card flush>
          {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
            : aliases.length === 0 ? <Empty icon="mail" title="No aliases" sub="Forward an address to another mailbox or external email." />
            : (
              <table className="tbl">
                <thead><tr><th>Source</th><th>Destination</th><th style={{ textAlign: 'right' }} /></tr></thead>
                <tbody>
                  {aliases.map(a => (
                    <tr key={a.id}>
                      <td className="mono">{a.source}</td>
                      <td className="mono muted">{a.destination}</td>
                      <td><div className="row-actions"><IconBtn icon="trash" danger title="Delete alias" onClick={() => removeAlias(a.id, a.source)} /></div></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
        </Card>
      )}

      <MailboxDialog open={mbOpen} onClose={() => setMbOpen(false)} domainId={domainId} domainName={selected?.name ?? ''} onSaved={() => { setMbOpen(false); void load(); }} />
      <AliasDialog open={aliasOpen} onClose={() => setAliasOpen(false)} domainId={domainId} domainName={selected?.name ?? ''} onSaved={() => { setAliasOpen(false); void load(); }} />
    </>
  );
}

function MailboxDialog({ open, onClose, domainId, domainName, onSaved }: { open: boolean; onClose: () => void; domainId: number | null; domainName: string; onSaved: () => void }) {
  const [localPart, setLocalPart] = useState('');
  const [password, setPassword] = useState('');
  const [quotaMb, setQuotaMb] = useState(500);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => { if (open) { setLocalPart(''); setPassword(''); setQuotaMb(500); setErr(null); } }, [open]);

  const submit = async () => {
    if (!domainId) return;
    setBusy(true); setErr(null);
    try {
      await api.post(`/domains/${domainId}/mailboxes`, { localPart, password, quotaMb });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Create failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} title="New mailbox"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !localPart || !password}>{busy ? 'Creating…' : 'Create'}</button>
      </>}>
      <div className="col gap-3">
        <Field label={`Email address`}>
          <div className="row gap-1">
            <input className="input mono" value={localPart} onChange={e => setLocalPart(e.target.value)} autoFocus placeholder="user" style={{ flex: 1 }} />
            <span className="muted mono" style={{ alignSelf: 'center' }}>@{domainName}</span>
          </div>
        </Field>
        <Field label="Password"><input className="input mono" type="password" value={password} onChange={e => setPassword(e.target.value)} /></Field>
        <Field label="Quota (MB)"><input className="input mono" type="number" min={1} value={quotaMb} onChange={e => setQuotaMb(Number(e.target.value))} /></Field>
      </div>
    </Dialog>
  );
}

function AliasDialog({ open, onClose, domainId, domainName, onSaved }: { open: boolean; onClose: () => void; domainId: number | null; domainName: string; onSaved: () => void }) {
  const [source, setSource] = useState('');
  const [destination, setDestination] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => { if (open) { setSource(''); setDestination(''); setErr(null); } }, [open]);

  const submit = async () => {
    if (!domainId) return;
    setBusy(true); setErr(null);
    try {
      await api.post(`/domains/${domainId}/aliases`, { source, destination });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Create failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} title="New alias" sub={`Forward an address on ${domainName} to somewhere else.`}
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !source || !destination}>{busy ? 'Adding…' : 'Add alias'}</button>
      </>}>
      <div className="col gap-3">
        <Field label="Source"><input className="input mono" autoFocus placeholder={`info@${domainName}`} value={source} onChange={e => setSource(e.target.value)} /></Field>
        <Field label="Destination"><input className="input mono" placeholder="user@example.com" value={destination} onChange={e => setDestination(e.target.value)} /></Field>
      </div>
    </Dialog>
  );
}
