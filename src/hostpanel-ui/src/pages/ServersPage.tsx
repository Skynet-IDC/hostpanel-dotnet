import { useEffect, useState } from 'react';
import api from '../api/client';
import { Badge, Card, Dialog, Empty, Field, IconBtn, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface ServerRow {
  id: number;
  hostname: string;
  ipAddress: string;
  isLocal: boolean;
  isActive: boolean;
  isHealthy: boolean;
  lastHealthCheck?: string;
  osInfo?: string;
  serverGroup?: string;
}

export function ServersPage() {
  const [servers, setServers] = useState<ServerRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [checking, setChecking] = useState<number | null>(null);

  const load = async () => {
    setLoading(true);
    setErr(null);
    try {
      const r = await api.get<ServerRow[]>('/servers');
      setServers(r.data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load servers');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const healthCheck = async (id: number) => {
    setChecking(id);
    try {
      await api.post(`/servers/${id}/health-check`, {});
      await load();
    } catch (e) {
      alert(e instanceof Error ? e.message : 'Health check failed');
    } finally {
      setChecking(null);
    }
  };

  const remove = async (id: number, hostname: string) => {
    if (!confirm(`Remove server ${hostname}? Its HA sync state is also removed.`)) return;
    try {
      await api.delete(`/servers/${id}`);
      await load();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { message?: string } } }).response?.data?.message
        : null;
      alert(msg ?? 'Delete failed');
    }
  };

  return (
    <>
      <PageHead
        title="Server management"
        sub={`${servers.length} server${servers.length === 1 ? '' : 's'} in the fleet`}
        actions={
          <button className="btn primary" onClick={() => setCreateOpen(true)}>
            <Icons.plus />Add server
          </button>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card flush>
        {loading ? (
          <div className="muted" style={{ padding: 16 }}>Loading…</div>
        ) : servers.length === 0 ? (
          <Empty
            icon="cloud"
            title="No servers registered"
            sub="This panel only knows about its own local node. Add remote nodes to enable multi-server HA, geo-DR, and load balancing."
            action={<button className="btn primary" onClick={() => setCreateOpen(true)} style={{ marginTop: 4 }}><Icons.plus />Add server</button>}
          />
        ) : (
          <table className="tbl">
            <thead>
              <tr>
                <th>Hostname</th>
                <th>IP</th>
                <th>Role</th>
                <th>Health</th>
                <th>OS</th>
                <th>Group</th>
                <th>Last checked</th>
                <th style={{ textAlign: 'right' }} />
              </tr>
            </thead>
            <tbody>
              {servers.map(s => (
                <tr key={s.id}>
                  <td className="mono" style={{ fontWeight: 600 }}>{s.hostname}</td>
                  <td className="mono muted" style={{ fontSize: 11 }}>{s.ipAddress}</td>
                  <td>
                    {s.isLocal ? <Badge tone="primary" dot>Local</Badge>
                      : s.isActive ? <Badge tone="info">Remote</Badge>
                      : <Badge>Disabled</Badge>}
                  </td>
                  <td>
                    {s.isHealthy
                      ? <Badge tone="success" dot>Healthy</Badge>
                      : <Badge tone="danger" dot>Unreachable</Badge>}
                  </td>
                  <td className="muted" style={{ fontSize: 11 }}>{s.osInfo ?? '—'}</td>
                  <td className="muted" style={{ fontSize: 11 }}>{s.serverGroup ?? '—'}</td>
                  <td className="muted" style={{ fontSize: 11 }}>
                    {s.lastHealthCheck ? new Date(s.lastHealthCheck).toLocaleString() : 'Never'}
                  </td>
                  <td>
                    <div className="row-actions">
                      <IconBtn
                        icon="refresh"
                        title="Health check now"
                        onClick={() => healthCheck(s.id)}
                      />
                      {!s.isLocal && (
                        <IconBtn icon="trash" danger title="Remove" onClick={() => remove(s.id, s.hostname)} />
                      )}
                      {checking === s.id && <span className="muted" style={{ fontSize: 10 }}>…</span>}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      {createOpen && <AddServerDialog onClose={() => setCreateOpen(false)} onSaved={() => { setCreateOpen(false); void load(); }} />}
    </>
  );
}

function AddServerDialog({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [hostname, setHostname] = useState('');
  const [ipAddress, setIpAddress] = useState('');
  const [agentPort, setAgentPort] = useState(50051);
  const [agentToken, setAgentToken] = useState('');
  const [serverGroup, setServerGroup] = useState('failover');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const submit = async () => {
    if (!hostname.trim() || !ipAddress.trim()) { setErr('Hostname and IP required'); return; }
    setBusy(true);
    setErr(null);
    try {
      await api.post('/servers', {
        hostname: hostname.trim(),
        ipAddress: ipAddress.trim(),
        agentPort,
        agentToken: agentToken || null,
        serverGroup: serverGroup || null,
      });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { message?: string } } }).response?.data?.message
        : null;
      setErr(msg ?? (e instanceof Error ? e.message : 'Add failed'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={true}
      onClose={onClose}
      title="Add server to fleet"
      sub="Register a remote node running the HostPanel agent."
      foot={
        <>
          {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
          <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn primary" onClick={submit} disabled={busy || !hostname.trim() || !ipAddress.trim()}>
            {busy ? 'Adding…' : 'Add server'}
          </button>
        </>
      }
    >
      <div className="col gap-3">
        <Field label="Hostname" hint="Friendly name shown in the UI.">
          <input className="input mono" autoFocus value={hostname} onChange={e => setHostname(e.target.value)} placeholder="standby-01" />
        </Field>
        <Field label="IP address">
          <input className="input mono" value={ipAddress} onChange={e => setIpAddress(e.target.value)} placeholder="192.168.10.236" />
        </Field>
        <Field label="Agent port" hint="HostPanel agent gRPC port — usually 50051.">
          <input className="input mono" type="number" value={agentPort} onChange={e => setAgentPort(Number(e.target.value))} />
        </Field>
        <Field label="Agent token" hint="The agent's bearer token (from /etc/hostpanel/agent.token on the remote).">
          <input className="input mono" type="password" value={agentToken} onChange={e => setAgentToken(e.target.value)} />
        </Field>
        <Field label="Group" hint="Free-form tag — used to scope HA strategies (e.g. failover, geo-dr).">
          <input className="input mono" value={serverGroup} onChange={e => setServerGroup(e.target.value)} />
        </Field>
      </div>
    </Dialog>
  );
}
