import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Checkbox, Dialog, Empty, Field, PageHead, Stat } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

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
  const [cfg, setCfg] = useState<GitConfig | null>(null);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [deploying, setDeploying] = useState(false);
  const [formOpen, setFormOpen] = useState(false);

  useEffect(() => {
    api.get<{ id: number; name: string }[]>('/domains')
      .then(r => { setDomains(r.data); if (r.data.length > 0) setDomainId(r.data[0].id); })
      .finally(() => setLoading(false));
  }, []);

  const load = async () => {
    if (!domainId) return;
    setErr(null);
    try {
      const { data } = await api.get<GitConfig>(`/domains/${domainId}/git`);
      setCfg(data);
    } catch {
      setCfg(null);
    }
  };
  useEffect(() => { void load(); }, [domainId]);

  const deploy = async () => {
    if (!domainId) return;
    setDeploying(true);
    try {
      await api.post(`/domains/${domainId}/git/deploy`);
      await load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Deploy failed');
    } finally { setDeploying(false); }
  };

  const selected = domains.find(d => d.id === domainId);

  return (
    <>
      <PageHead
        title="Git deployments"
        sub={selected ? `Continuous deploy for ${selected.name}` : 'Pick a domain'}
        actions={
          <>
            {domains.length > 0 && (
              <select className="select" value={domainId ?? ''} onChange={e => setDomainId(Number(e.target.value))} style={{ maxWidth: 240 }}>
                {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select>
            )}
            <button className="btn" onClick={() => setFormOpen(true)} disabled={!domainId}><Icons.git />{cfg?.repoUrl ? 'Reconfigure' : 'Configure'}</button>
            {cfg?.repoUrl && (
              <button className="btn primary" onClick={deploy} disabled={deploying}><Icons.upload />{deploying ? 'Deploying…' : 'Deploy now'}</button>
            )}
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {loading ? <Card><div className="muted" style={{ padding: 8 }}>Loading…</div></Card>
        : !cfg?.repoUrl ? (
          <Card>
            <Empty
              icon="git"
              title="No git repo configured"
              sub="Connect a repository to enable continuous deploy from main/master."
              action={<button className="btn primary" onClick={() => setFormOpen(true)} style={{ marginTop: 4 }} disabled={!domainId}><Icons.git />Configure repository</button>}
            />
          </Card>
        ) : (
          <>
            <div className="stats" style={{ marginBottom: 16 }}>
              <Stat label="Repository" icon="git" value={cfg.provider ?? '—'} foot={<span className="mono muted" style={{ fontSize: 11 }}>{cfg.repoUrl}</span>} />
              <Stat label="Branch" icon="zap" value={cfg.branch ?? 'main'} />
              <Stat label="Last deploy" icon="history" value={cfg.lastDeployAt ? new Date(cfg.lastDeployAt).toLocaleString() : 'Never'} foot={cfg.lastCommitHash ? <span className="mono muted" style={{ fontSize: 11 }}>{cfg.lastCommitHash.slice(0, 8)}</span> : undefined} />
              <Stat label="Auto-deploy" icon="refresh" value={cfg.autoDeployEnabled ? 'On' : 'Off'} foot={cfg.autoDeployEnabled ? <Badge tone="success" dot>Webhook armed</Badge> : <Badge>Manual</Badge>} />
            </div>
          </>
        )}

      <ConfigDialog open={formOpen} onClose={() => setFormOpen(false)} domainId={domainId} initial={cfg} onSaved={() => { setFormOpen(false); void load(); }} />
    </>
  );
}

function ConfigDialog({ open, onClose, domainId, initial, onSaved }: { open: boolean; onClose: () => void; domainId: number | null; initial: GitConfig | null; onSaved: () => void }) {
  const [repoUrl, setRepoUrl] = useState('');
  const [branch, setBranch] = useState('main');
  const [provider, setProvider] = useState('github');
  const [autoDeploy, setAutoDeploy] = useState(false);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setRepoUrl(initial?.repoUrl ?? '');
      setBranch(initial?.branch ?? 'main');
      setProvider(initial?.provider ?? 'github');
      setAutoDeploy(initial?.autoDeployEnabled ?? false);
      setErr(null);
    }
  }, [open, initial]);

  const submit = async () => {
    if (!domainId) return;
    setBusy(true); setErr(null);
    try {
      await api.post(`/domains/${domainId}/git`, { repoUrl, branch, provider, autoDeployEnabled: autoDeploy });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? 'Save failed');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={onClose} size="lg" title="Configure git repository"
      foot={<>
        {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
        <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
        <button className="btn primary" onClick={submit} disabled={busy || !repoUrl}>{busy ? 'Saving…' : 'Save'}</button>
      </>}>
      <div className="grid-2" style={{ gap: 12 }}>
        <Field label="Repository URL" span={2}><input className="input mono" value={repoUrl} onChange={e => setRepoUrl(e.target.value)} placeholder="https://github.com/user/repo.git" autoFocus /></Field>
        <Field label="Branch"><input className="input mono" value={branch} onChange={e => setBranch(e.target.value)} /></Field>
        <Field label="Provider">
          <select className="select" value={provider} onChange={e => setProvider(e.target.value)}>
            <option value="github">GitHub</option>
            <option value="gitlab">GitLab</option>
            <option value="bitbucket">Bitbucket</option>
            <option value="custom">Custom</option>
          </select>
        </Field>
        <Field span={2}><Checkbox checked={autoDeploy} onChange={setAutoDeploy} label="Auto-deploy on push (webhook)" /></Field>
      </div>
    </Dialog>
  );
}
