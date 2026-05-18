import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Empty, PageHead, Stat, Switch } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface DomainSummary { id: number; name: string }

interface SslStatus {
  enabled: boolean;
  provider: string | null;
  expiryDate: string | null;
  daysUntilExpiry: number | null;
  autoRenew: boolean;
}

function expiryTone(days: number | null) {
  if (days === null) return 'default' as const;
  if (days <= 7) return 'danger' as const;
  if (days <= 30) return 'warn' as const;
  return 'success' as const;
}

export function SslPage() {
  const [domains, setDomains] = useState<DomainSummary[]>([]);
  const [domainId, setDomainId] = useState<number | null>(null);
  const [ssl, setSsl] = useState<SslStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [issuing, setIssuing] = useState(false);
  const [toggling, setToggling] = useState(false);

  useEffect(() => {
    api.get<DomainSummary[]>('/domains')
      .then(r => {
        setDomains(r.data);
        if (r.data.length > 0) setDomainId(r.data[0].id);
      })
      .catch(e => setErr(e instanceof Error ? e.message : 'Failed to load domains'))
      .finally(() => setLoading(false));
  }, []);

  const loadSsl = async () => {
    if (!domainId) return;
    setErr(null);
    try {
      const { data } = await api.get<SslStatus>(`/domains/${domainId}/ssl/status`);
      setSsl(data);
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? (e instanceof Error ? e.message : 'Load SSL failed'));
    }
  };

  useEffect(() => { void loadSsl(); }, [domainId]);

  const issue = async () => {
    if (!domainId) return;
    setIssuing(true);
    try {
      await api.post(`/domains/${domainId}/ssl/issue`);
      await loadSsl();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Issue failed');
    } finally {
      setIssuing(false);
    }
  };

  const toggle = async () => {
    if (!domainId) return;
    setToggling(true);
    try {
      await api.post(`/domains/${domainId}/ssl/toggle-auto-renew`);
      await loadSsl();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      alert(msg ?? 'Toggle failed');
    } finally {
      setToggling(false);
    }
  };

  const selected = domains.find(d => d.id === domainId);

  return (
    <>
      <PageHead
        title="SSL / TLS"
        sub="Let's Encrypt + manual certs · per-domain status"
        actions={
          domains.length > 0 && (
            <select
              className="select"
              style={{ maxWidth: 280 }}
              value={domainId ?? ''}
              onChange={e => setDomainId(Number(e.target.value))}
            >
              {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          )
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {loading ? (
        <Card><div className="muted" style={{ padding: 8 }}>Loading…</div></Card>
      ) : domains.length === 0 ? (
        <Card>
          <Empty icon="lock" title="No domains" sub="Add a domain first; SSL operates per-domain." />
        </Card>
      ) : !ssl ? (
        <Card><div className="muted" style={{ padding: 8 }}>No SSL data.</div></Card>
      ) : (
        <>
          <div className="stats" style={{ marginBottom: 16 }}>
            <Stat
              label="Status"
              icon={ssl.enabled ? 'lock' : 'lockOpen'}
              value={ssl.enabled ? 'Active' : 'Disabled'}
              foot={<span className="muted">{ssl.provider ?? 'No provider'}</span>}
            />
            <Stat
              label="Expires"
              icon="clock"
              value={ssl.expiryDate ? new Date(ssl.expiryDate).toLocaleDateString() : '—'}
              foot={ssl.daysUntilExpiry !== null && (
                <Badge tone={expiryTone(ssl.daysUntilExpiry)} dot>{ssl.daysUntilExpiry} days</Badge>
              )}
            />
            <Stat
              label="Auto-renew"
              icon="refresh"
              value={ssl.autoRenew ? 'On' : 'Off'}
              foot={<span className="muted">checks daily 4 weeks before expiry</span>}
            />
            <Stat
              label="Domain"
              icon="globe"
              value={selected?.name ?? '—'}
              foot={<a className="muted" href={`https://${selected?.name}`} target="_blank" rel="noreferrer">visit ↗</a>}
            />
          </div>

          <Card title="Actions" sub="Issue or refresh the certificate. Let's Encrypt is rate-limited; one or two issues per week is safe.">
            <div className="col gap-4">
              <div className="row between">
                <div>
                  <div style={{ fontWeight: 600 }}>{ssl.enabled ? 'Re-issue certificate' : 'Issue certificate'}</div>
                  <div className="muted" style={{ fontSize: 12 }}>HTTP-01 challenge for the apex. Wildcard via DNS-01 if Cloudflare token is set.</div>
                </div>
                <button className="btn primary" onClick={issue} disabled={issuing}>
                  {issuing ? 'Issuing…' : ssl.enabled ? 'Re-issue' : 'Issue certificate'}
                </button>
              </div>

              <div className="row between" style={{ borderTop: '1px solid var(--border)', paddingTop: 16 }}>
                <div>
                  <div style={{ fontWeight: 600 }}>Auto-renew</div>
                  <div className="muted" style={{ fontSize: 12 }}>When the cert is within 30 days of expiry, the panel re-issues automatically.</div>
                </div>
                <div className="row gap-2">
                  <span className="muted" style={{ fontSize: 12 }}>{ssl.autoRenew ? 'On' : 'Off'}</span>
                  <Switch on={ssl.autoRenew} onChange={() => !toggling && toggle()} />
                </div>
              </div>
            </div>
          </Card>
        </>
      )}
    </>
  );
}
