import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { panelApi, type PanelAuditEntry, type PanelPlan, type PanelSubscription, type ServiceStats } from '../api/panel';
import { Card } from '../components/ui/primitives';
import { PageHead } from '../components/ui/primitives';
import { Stat } from '../components/ui/primitives';
import { StatusBadge } from '../components/ui/primitives';
import { Meter } from '../components/ui/primitives';
import { Empty } from '../components/ui/primitives';
import { Avatar } from '../components/ui/primitives';
import { initials } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface DashState {
  stats: ServiceStats | null;
  subs: PanelSubscription[];
  plans: PanelPlan[];
  audit: PanelAuditEntry[];
  loading: boolean;
  error: string | null;
}

function planById(plans: PanelPlan[], id: number) {
  return plans.find(p => p.id === id);
}

function formatGb(mb: number | undefined) {
  if (!mb) return '0 GB';
  const gb = mb / 1024;
  return gb >= 1000 ? `${(gb / 1000).toFixed(1)} TB` : `${gb.toFixed(1)} GB`;
}

function formatUptime(seconds: number | undefined) {
  if (!seconds) return '—';
  const days = Math.floor(seconds / 86400);
  if (days >= 1) return `${days}d`;
  const hours = Math.floor(seconds / 3600);
  return `${hours}h`;
}

export function DashboardPage() {
  const [state, setState] = useState<DashState>({
    stats: null, subs: [], plans: [], audit: [], loading: true, error: null,
  });

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [stats, subs, plans, audit] = await Promise.allSettled([
          panelApi.serverStats(),
          panelApi.listSubscriptions(),
          panelApi.listPlans(),
          panelApi.recentAudit(8),
        ]);
        if (cancelled) return;
        setState({
          stats: stats.status === 'fulfilled' ? stats.value : null,
          subs: subs.status === 'fulfilled' ? subs.value : [],
          plans: plans.status === 'fulfilled' ? plans.value : [],
          audit: audit.status === 'fulfilled' ? audit.value : [],
          loading: false,
          error: null,
        });
      } catch (err) {
        if (cancelled) return;
        setState(s => ({ ...s, loading: false, error: err instanceof Error ? err.message : 'Failed to load' }));
      }
    })();
    return () => { cancelled = true; };
  }, []);

  const { stats, subs, plans, audit, loading } = state;
  const active = subs.filter(s => s.status?.toLowerCase() === 'active').length;
  const suspended = subs.filter(s => s.status?.toLowerCase() === 'suspended').length;
  const mrr = subs
    .filter(s => s.status?.toLowerCase() === 'active')
    .reduce((sum, s) => sum + (planById(plans, s.servicePlanId)?.pricePerPeriod ?? 0), 0);
  const recentSubs = [...subs]
    .sort((a, b) => (b.createdAt ?? '').localeCompare(a.createdAt ?? ''))
    .slice(0, 6);

  return (
    <>
      <PageHead
        title="Dashboard"
        sub={stats?.hostname ? `${stats.hostname} · ${formatUptime(stats.uptimeSeconds)} uptime` : 'Server overview'}
        actions={
          <>
            <button className="btn" onClick={() => location.reload()}><Icons.refresh />Refresh</button>
            <Link to="/subscriptions" className="btn primary"><Icons.plus />New subscription</Link>
          </>
        }
      />

      {loading && (
        <Card><div className="muted" style={{ padding: 8 }}>Loading…</div></Card>
      )}

      {!loading && (
        <>
          <div className="stats" style={{ marginBottom: 16 }}>
            <Stat
              label="Active subscriptions"
              icon="layers"
              value={`${active}`}
              foot={<span className="muted">{suspended} suspended · {subs.length} total</span>}
            />
            <Stat
              label="MRR"
              icon="trending"
              value={`$${mrr.toFixed(2)}`}
              foot={<span className="muted">recurring revenue</span>}
            />
            <Stat
              label="Disk used"
              icon="archive"
              value={formatGb(stats?.usedDiskMb)}
              foot={<span className="muted">of {formatGb(stats?.totalDiskMb)}</span>}
            />
            <Stat
              label="Memory"
              icon="cpu"
              value={stats?.totalMemoryMb ? `${Math.round((stats.usedMemoryMb ?? 0) / 1024 * 10) / 10} GB` : '—'}
              foot={<span className="muted">of {stats?.totalMemoryMb ? `${Math.round(stats.totalMemoryMb / 1024 * 10) / 10} GB` : '—'}</span>}
            />
          </div>

          <div style={{ display: 'grid', gap: 16, gridTemplateColumns: '2fr 1fr' }}>
            <Card
              title="Recent subscriptions"
              sub={subs.length === 0 ? 'No subscriptions yet' : `${subs.length} total`}
              flush
              actions={<Link to="/subscriptions" className="btn ghost sm">All subscriptions →</Link>}
            >
              {recentSubs.length === 0 ? (
                <Empty
                  icon="layers"
                  title="No subscriptions"
                  sub="Create your first service plan, then add a customer subscription to start hosting."
                  action={<Link to="/plans" className="btn primary" style={{ marginTop: 4 }}><Icons.plus />Create plan</Link>}
                />
              ) : (
                <table className="tbl">
                  <thead>
                    <tr>
                      <th>Domain</th>
                      <th>Plan</th>
                      <th>Disk</th>
                      <th style={{ textAlign: 'right' }}>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {recentSubs.map(s => {
                      const plan = planById(plans, s.servicePlanId);
                      const mainDomain = s.domains?.[0]?.name ?? `subscription #${s.id}`;
                      return (
                        <tr key={s.id}>
                          <td className="mono" style={{ fontSize: 12 }}>{mainDomain}</td>
                          <td>{plan?.name ?? '—'}</td>
                          <td style={{ width: 130 }}>
                            <Meter value={0} total={plan?.diskQuotaMb ?? 1024} hideText />
                          </td>
                          <td style={{ textAlign: 'right' }}><StatusBadge status={s.status ?? 'idle'} /></td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              )}
            </Card>

            <Card title="Server" sub={stats?.osVersion ?? 'Resource overview'} actions={stats ? <span className="badge success"><span className="dot" />Healthy</span> : null}>
              {!stats ? (
                <div className="muted" style={{ fontSize: 12 }}>Stats unavailable</div>
              ) : (
                <div className="col gap-4">
                  <div className="col gap-2">
                    <div className="between" style={{ fontSize: 12 }}>
                      <span className="muted">CPU</span>
                      <span className="mono">{Math.round(stats.cpuUsagePercent ?? 0)}%</span>
                    </div>
                    <div className="meter">
                      <div className="fill" style={{ width: `${stats.cpuUsagePercent ?? 0}%` }} />
                    </div>
                  </div>
                  <div className="col gap-2">
                    <div className="between" style={{ fontSize: 12 }}>
                      <span className="muted">RAM</span>
                      <span className="mono">{formatGb(stats.usedMemoryMb)} / {formatGb(stats.totalMemoryMb)}</span>
                    </div>
                    <div className="meter">
                      <div
                        className="fill"
                        style={{
                          width: stats.totalMemoryMb
                            ? `${Math.min(100, ((stats.usedMemoryMb ?? 0) / stats.totalMemoryMb) * 100)}%`
                            : '0%',
                        }}
                      />
                    </div>
                  </div>
                  <div className="col gap-2">
                    <div className="between" style={{ fontSize: 12 }}>
                      <span className="muted">Disk</span>
                      <span className="mono">{formatGb(stats.usedDiskMb)} / {formatGb(stats.totalDiskMb)}</span>
                    </div>
                    <div className="meter">
                      <div
                        className="fill"
                        style={{
                          width: stats.totalDiskMb
                            ? `${Math.min(100, ((stats.usedDiskMb ?? 0) / stats.totalDiskMb) * 100)}%`
                            : '0%',
                        }}
                      />
                    </div>
                  </div>
                </div>
              )}
            </Card>
          </div>

          <div style={{ marginTop: 16 }}>
            <Card
              title="Recent activity"
              sub={audit.length === 0 ? 'No audit events yet' : 'Latest panel events'}
              actions={<Link to="/audit" className="btn ghost sm">View all</Link>}
            >
              {audit.length === 0 ? (
                <Empty icon="log" title="Quiet here" sub="Once admins, customers, or automated jobs do anything, it'll show up here." />
              ) : (
                <div className="col" style={{ gap: 0 }}>
                  {audit.map((a, i) => (
                    <div
                      key={a.id}
                      className="row gap-3"
                      style={{ padding: '8px 0', borderBottom: i < audit.length - 1 ? '1px solid var(--border)' : 'none' }}
                    >
                      <Avatar seed={a.userEmail ?? a.userId ?? 'system'} label={initials(a.userEmail ?? 'sys')} size={26} />
                      <div style={{ minWidth: 0, flex: 1 }}>
                        <div style={{ fontSize: 12.5, lineHeight: 1.4 }}>
                          <span style={{ fontWeight: 600 }}>{a.userEmail ?? 'system'}</span>
                          <span className="muted"> · </span>
                          <span className="mono" style={{ fontSize: 11.5 }}>{a.action}</span>
                          {a.resource && <span className="muted"> · {a.resource}</span>}
                        </div>
                      </div>
                      <div className="muted mono" style={{ fontSize: 11 }}>
                        {new Date(a.timestamp).toLocaleString()}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </Card>
          </div>
        </>
      )}
    </>
  );
}
