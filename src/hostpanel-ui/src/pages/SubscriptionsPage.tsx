import { useEffect, useMemo, useState } from 'react';
import api from '../api/client';
import { Avatar, Badge, Card, Checkbox, Dialog, Empty, Field, IconBtn, Meter, PageHead, Stat, StatusBadge, Tabs, initials } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';
import { panelApi, type PanelCustomer, type PanelPlan, type PanelSubscription } from '../api/panel';

type StatusFilter = 'all' | 'active' | 'suspended' | 'cancelled';
type ConfirmAction = 'suspend' | 'resume' | 'cancel';

interface UsageData {
  subscriptionId: number;
  servicePlan: string;
  periodStart: string;
  periodEnd: string;
  usage: {
    diskMb: number;
    bandwidthMb: number;
    bandwidthTracked: boolean;
    domainCount: number;
    mailboxCount: number;
    aliasCount: number;
    databaseCount: number;
    ftpAccountCount: number;
  };
  limits: {
    diskMb: number;
    bandwidthMb: number;
    maxDomains: number;
    maxMailboxes: number;
    maxDatabases: number;
    maxFtpAccounts: number;
  };
  ratios: {
    disk: number;
    bandwidth: number;
    domains: number;
    mailboxes: number;
    databases: number;
    ftpAccounts: number;
  };
}

export function SubscriptionsPage() {
  const [tab, setTab] = useState<StatusFilter>('all');
  const [subs, setSubs] = useState<PanelSubscription[]>([]);
  const [plans, setPlans] = useState<PanelPlan[]>([]);
  const [customers, setCustomers] = useState<PanelCustomer[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [confirming, setConfirming] = useState<{ sub: PanelSubscription; action: ConfirmAction } | null>(null);
  const [changePlan, setChangePlan] = useState<PanelSubscription | null>(null);
  const [usageFor, setUsageFor] = useState<PanelSubscription | null>(null);
  const [usageData, setUsageData] = useState<UsageData | null>(null);
  const [usageBusy, setUsageBusy] = useState(false);

  const load = async () => {
    setLoading(true);
    setErr(null);
    try {
      const [s, p, c] = await Promise.all([
        panelApi.listSubscriptions(),
        panelApi.listPlans(),
        panelApi.listCustomers(),
      ]);
      setSubs(s); setPlans(p); setCustomers(c);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const openUsage = async (sub: PanelSubscription) => {
    setUsageFor(sub);
    setUsageBusy(true);
    setUsageData(null);
    try {
      const r = await api.get<UsageData>(`/subscriptions/${sub.id}/usage`);
      setUsageData(r.data);
    } catch (e) {
      console.error('usage load failed', e);
    } finally { setUsageBusy(false); }
  };

  const planById = (id: number) => plans.find(p => p.id === id);
  const customerById = (id: string) => customers.find(c => c.id === id);

  const filtered = useMemo(() => {
    if (tab === 'all') return subs;
    return subs.filter(s => s.status.toLowerCase() === tab);
  }, [subs, tab]);

  const counts = useMemo(() => {
    const lower = (s: string) => s.toLowerCase();
    return {
      all: subs.length,
      active: subs.filter(s => lower(s.status) === 'active').length,
      suspended: subs.filter(s => lower(s.status) === 'suspended').length,
      cancelled: subs.filter(s => lower(s.status) === 'cancelled').length,
    };
  }, [subs]);

  return (
    <>
      <PageHead
        title="Subscriptions"
        sub={`${subs.length} subscription${subs.length === 1 ? '' : 's'} · ${counts.active} active`}
        actions={
          <button className="btn primary" onClick={() => setCreateOpen(true)} disabled={plans.length === 0 || customers.length === 0}>
            <Icons.plus />New subscription
          </button>
        }
      />

      <Tabs
        active={tab}
        onChange={id => setTab(id as StatusFilter)}
        tabs={[
          { id: 'all', label: 'All', count: counts.all },
          { id: 'active', label: 'Active', count: counts.active },
          { id: 'suspended', label: 'Suspended', count: counts.suspended },
          { id: 'cancelled', label: 'Cancelled', count: counts.cancelled },
        ]}
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <Card flush>
        {loading ? (
          <div className="muted" style={{ padding: 16 }}>Loading…</div>
        ) : filtered.length === 0 ? (
          <Empty
            icon="layers"
            title={subs.length === 0 ? 'No subscriptions yet' : `No ${tab} subscriptions`}
            sub={subs.length === 0
              ? 'A subscription connects a customer to a service plan and turns into a hosted site.'
              : 'Switch tabs or create a new one.'}
            action={subs.length === 0 && plans.length > 0 && customers.length > 0 ? (
              <button className="btn primary" onClick={() => setCreateOpen(true)} style={{ marginTop: 4 }}>
                <Icons.plus />New subscription
              </button>
            ) : undefined}
          />
        ) : (
          <table className="tbl">
            <thead>
              <tr>
                <th style={{ width: 26 }}><Checkbox /></th>
                <th>Domain</th>
                <th>Customer</th>
                <th>Plan</th>
                <th>Disk</th>
                <th>Created</th>
                <th>Status</th>
                <th style={{ textAlign: 'right' }} />
              </tr>
            </thead>
            <tbody>
              {filtered.map(s => {
                const plan = planById(s.servicePlanId);
                const cust = customerById(s.userId);
                const company = cust ? (cust.companyName ?? `${cust.firstName} ${cust.lastName}`.trim() ?? cust.email) : 'Unknown';
                const mainDomain = s.domains?.[0]?.name ?? `subscription #${s.id}`;
                const isSuspended = s.status.toLowerCase() === 'suspended';
                return (
                  <tr key={s.id}>
                    <td><Checkbox /></td>
                    <td className="mono" style={{ fontSize: 12 }}>{mainDomain}</td>
                    <td>
                      <div className="row gap-2">
                        <Avatar seed={company} label={initials(company)} size={22} />
                        <span>{company}</span>
                      </div>
                    </td>
                    <td>{plan?.name ?? '—'}</td>
                    <td style={{ width: 140 }}>
                      <Meter value={0} total={plan?.diskQuotaMb ?? 1024} hideText />
                    </td>
                    <td className="muted mono" style={{ fontSize: 11.5 }}>
                      {s.createdAt ? new Date(s.createdAt).toISOString().slice(0, 10) : '—'}
                    </td>
                    <td><StatusBadge status={s.status} /></td>
                    <td>
                      <div className="row-actions">
                        <IconBtn icon="trending" title="Usage details" onClick={() => void openUsage(s)} />
                        <IconBtn icon="edit" title="Change plan" onClick={() => setChangePlan(s)} />
                        {isSuspended
                          ? <IconBtn icon="play" title="Resume" onClick={() => setConfirming({ sub: s, action: 'resume' })} />
                          : <IconBtn icon="pause" title="Suspend" onClick={() => setConfirming({ sub: s, action: 'suspend' })} />}
                        <IconBtn icon="trash" danger title="Cancel" onClick={() => setConfirming({ sub: s, action: 'cancel' })} />
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </Card>

      <CreateFlow
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onSaved={() => { setCreateOpen(false); void load(); }}
        plans={plans}
        customers={customers}
      />
      <ConfirmDialog
        confirming={confirming}
        onClose={() => setConfirming(null)}
        onDone={() => { setConfirming(null); void load(); }}
      />
      <ChangePlanDialog
        sub={changePlan}
        onClose={() => setChangePlan(null)}
        onDone={() => { setChangePlan(null); void load(); }}
      />

      <Dialog
        open={usageFor !== null}
        onClose={() => { setUsageFor(null); setUsageData(null); }}
        title={`Usage — ${usageFor?.domains?.[0]?.name ?? `subscription #${usageFor?.id ?? ''}`}`}
        sub={usageData ? `Plan: ${usageData.servicePlan} · ${new Date(usageData.periodStart).toLocaleDateString()} → ${new Date(usageData.periodEnd).toLocaleDateString()}` : 'Loading…'}
        foot={<button className="btn ghost" onClick={() => { setUsageFor(null); setUsageData(null); }}>Close</button>}
      >
        {usageBusy && <div className="muted">Loading…</div>}
        {usageData && (
          <div className="col gap-4">
            <div>
              <div className="row" style={{ justifyContent: 'space-between', marginBottom: 4 }}>
                <span className="muted" style={{ fontSize: 12 }}>Disk</span>
                <span className="mono" style={{ fontSize: 12 }}>{usageData.usage.diskMb.toFixed(0)} / {usageData.limits.diskMb} MB</span>
              </div>
              <Meter value={usageData.usage.diskMb} total={usageData.limits.diskMb || 1} hideText />
            </div>
            <div>
              <div className="row" style={{ justifyContent: 'space-between', marginBottom: 4 }}>
                <span className="muted" style={{ fontSize: 12 }}>Bandwidth this period</span>
                <span className="mono" style={{ fontSize: 12 }}>
                  {usageData.usage.bandwidthMb.toFixed(0)} / {usageData.limits.bandwidthMb} MB
                  {!usageData.usage.bandwidthTracked && <Badge tone="warn" style={{ marginLeft: 8 }}>not tracked</Badge>}
                </span>
              </div>
              <Meter value={usageData.usage.bandwidthMb} total={usageData.limits.bandwidthMb || 1} hideText />
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 8 }}>
              <Stat label="Domains" value={`${usageData.usage.domainCount} / ${usageData.limits.maxDomains}`} />
              <Stat label="Mailboxes" value={`${usageData.usage.mailboxCount} / ${usageData.limits.maxMailboxes}`} />
              <Stat label="Databases" value={`${usageData.usage.databaseCount} / ${usageData.limits.maxDatabases}`} />
              <Stat label="FTP accounts" value={`${usageData.usage.ftpAccountCount} / ${usageData.limits.maxFtpAccounts}`} />
            </div>
          </div>
        )}
      </Dialog>
    </>
  );
}

function ChangePlanDialog({ sub, onClose, onDone }: { sub: PanelSubscription | null; onClose: () => void; onDone: () => void }) {
  const [plans, setPlans] = useState<PanelPlan[]>([]);
  const [selected, setSelected] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [creatingCustom, setCreatingCustom] = useState(false);
  const [custom, setCustom] = useState({ name: '', pricePerPeriod: 0, diskQuotaMb: 1024, bandwidthLimitMb: 50000, maxDomains: 5, maxEmailAccounts: 10, maxDatabases: 2, maxFtpAccounts: 5 });

  const refreshPlans = (subUserId: string) =>
    api.get(`/serviceplans?customerUserId=${encodeURIComponent(subUserId)}`).then(r => setPlans(r.data ?? [])).catch(() => setPlans([]));

  useEffect(() => {
    if (!sub) return;
    setSelected(sub.servicePlanId ?? null);
    setErr(null);
    setCreatingCustom(false);
    void refreshPlans(sub.userId);
  }, [sub]);

  const createCustom = async () => {
    if (!sub) return;
    setBusy(true); setErr(null);
    try {
      const r = await api.post(`/serviceplans/custom/${encodeURIComponent(sub.userId)}`, custom);
      const newPlan = r.data as PanelPlan;
      await refreshPlans(sub.userId);
      setSelected(newPlan.id);
      setCreatingCustom(false);
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? (e instanceof Error ? e.message : 'Custom plan create failed'));
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    if (!sub || selected === null) return;
    setBusy(true); setErr(null);
    try {
      await api.put(`/subscriptions/${sub.id}/plan`, { planId: selected });
      onDone();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? (e instanceof Error ? e.message : 'Plan change failed'));
    } finally {
      setBusy(false);
    }
  };

  if (!sub) return null;

  return (
    <Dialog
      open={!!sub}
      onClose={onClose}
      size="lg"
      title="Change plan"
      sub={`Subscription #${sub.id}`}
      foot={
        <>
          {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
          <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn primary" onClick={save} disabled={busy || selected === null || selected === sub.servicePlanId}>{busy ? 'Saving…' : 'Apply'}</button>
        </>
      }
    >
      <div className="col gap-2">
        {plans.length === 0 && <span className="muted">No plans available</span>}
        {plans.map(p => (
          <label key={p.id} className="row gap-2" style={{ alignItems: 'center', padding: '8px 10px', border: `1px solid ${selected === p.id ? 'var(--primary-500)' : 'var(--border)'}`, borderRadius: 6, background: selected === p.id ? 'color-mix(in oklch, var(--primary-500) 12%, transparent)' : 'var(--bg-2)', cursor: 'pointer' }}>
            <input type="radio" name="plan" checked={selected === p.id} onChange={() => setSelected(p.id)} />
            <div style={{ flex: 1 }}>
              <div className="row gap-2" style={{ alignItems: 'center' }}>
                <strong>{p.name}</strong>
                {p.isCustom && <Badge tone="info">Custom</Badge>}
                {p.id === sub.servicePlanId && <Badge tone="success">Current</Badge>}
              </div>
              <div className="muted" style={{ fontSize: 12 }}>${p.pricePerPeriod}/mo · {p.diskQuotaMb} MB disk · {p.maxDomains} domains</div>
            </div>
          </label>
        ))}
        {!creatingCustom ? (
          <button type="button" className="btn ghost" onClick={() => setCreatingCustom(true)} style={{ marginTop: 4 }}>+ Create a one-off custom plan for this customer</button>
        ) : (
          <div className="col gap-2" style={{ padding: 10, border: '1px dashed var(--primary-500)', borderRadius: 6, background: 'color-mix(in oklch, var(--primary-500) 6%, transparent)' }}>
            <strong style={{ fontSize: 13 }}>New custom plan</strong>
            <div className="grid-2" style={{ gap: 8 }}>
              <Field label="Name">
                <input className="input" value={custom.name} onChange={e => setCustom(c => ({ ...c, name: e.target.value }))} placeholder="e.g. ACME Special" />
              </Field>
              <Field label="Price / mo (USD)">
                <input className="input mono" type="number" value={custom.pricePerPeriod} onChange={e => setCustom(c => ({ ...c, pricePerPeriod: Number(e.target.value) }))} />
              </Field>
              <Field label="Disk quota (MB)">
                <input className="input mono" type="number" value={custom.diskQuotaMb} onChange={e => setCustom(c => ({ ...c, diskQuotaMb: Number(e.target.value) }))} />
              </Field>
              <Field label="Bandwidth (MB / mo)">
                <input className="input mono" type="number" value={custom.bandwidthLimitMb} onChange={e => setCustom(c => ({ ...c, bandwidthLimitMb: Number(e.target.value) }))} />
              </Field>
              <Field label="Max domains">
                <input className="input mono" type="number" value={custom.maxDomains} onChange={e => setCustom(c => ({ ...c, maxDomains: Number(e.target.value) }))} />
              </Field>
              <Field label="Max mailboxes">
                <input className="input mono" type="number" value={custom.maxEmailAccounts} onChange={e => setCustom(c => ({ ...c, maxEmailAccounts: Number(e.target.value) }))} />
              </Field>
              <Field label="Max databases">
                <input className="input mono" type="number" value={custom.maxDatabases} onChange={e => setCustom(c => ({ ...c, maxDatabases: Number(e.target.value) }))} />
              </Field>
              <Field label="Max FTP accts">
                <input className="input mono" type="number" value={custom.maxFtpAccounts} onChange={e => setCustom(c => ({ ...c, maxFtpAccounts: Number(e.target.value) }))} />
              </Field>
            </div>
            <div className="row gap-2">
              <button type="button" className="btn primary" onClick={createCustom} disabled={busy || !custom.name}>Create + select</button>
              <button type="button" className="btn ghost" onClick={() => setCreatingCustom(false)} disabled={busy}>Cancel</button>
            </div>
          </div>
        )}
      </div>
    </Dialog>
  );
}

// ──────────────── 4-step create flow ────────────────
type StepIdx = 0 | 1 | 2 | 3;

function CreateFlow({
  open, onClose, onSaved, plans, customers,
}: {
  open: boolean;
  onClose: () => void;
  onSaved: () => void;
  plans: PanelPlan[];
  customers: PanelCustomer[];
}) {
  const [step, setStep] = useState<StepIdx>(0);
  const [customerId, setCustomerId] = useState<string>('');
  const [planId, setPlanId] = useState<number | null>(null);
  const [mainDomain, setMainDomain] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setStep(0);
    setCustomerId(customers[0]?.id ?? '');
    setPlanId(plans[0]?.id ?? null);
    setMainDomain('');
    setErr(null);
  }, [open, plans, customers]);

  const customer = customers.find(c => c.id === customerId);
  const plan = plans.find(p => p.id === planId);

  const canNext = (() => {
    if (step === 0) return !!customer;
    if (step === 1) return !!plan;
    if (step === 2) return mainDomain.trim().length >= 3;
    return true;
  })();

  const submit = async () => {
    if (!customer || !plan) return;
    setBusy(true);
    setErr(null);
    try {
      await api.post('/subscriptions', {
        userId: customer.id,
        servicePlanId: plan.id,
        mainDomain: mainDomain.trim(),
      });
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? (e instanceof Error ? e.message : 'Create failed'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="lg"
      title="New subscription"
      sub={`Step ${step + 1} of 4`}
      foot={
        <>
          {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
          {step > 0 && <button className="btn ghost" onClick={() => setStep(s => Math.max(0, s - 1) as StepIdx)} disabled={busy}>Back</button>}
          {step < 3 ? (
            <button className="btn primary" onClick={() => setStep(s => (Math.min(3, s + 1) as StepIdx))} disabled={!canNext}>Continue</button>
          ) : (
            <button className="btn primary" onClick={submit} disabled={busy}>{busy ? 'Creating…' : 'Create subscription'}</button>
          )}
        </>
      }
    >
      <StepBar step={step} />

      {step === 0 && (
        <div className="col gap-3">
          <div className="muted" style={{ fontSize: 13 }}>Pick the customer this subscription belongs to.</div>
          <div className="col" style={{ maxHeight: 320, overflow: 'auto', border: '1px solid var(--border)', borderRadius: 8 }}>
            {customers.length === 0 ? (
              <div className="muted" style={{ padding: 16, textAlign: 'center' }}>No customers exist yet. Create one first.</div>
            ) : (
              customers.map(c => {
                const company = c.companyName ?? `${c.firstName} ${c.lastName}`.trim() ?? c.email;
                return (
                  <label
                    key={c.id}
                    className="row gap-3"
                    style={{
                      padding: 10,
                      borderBottom: '1px solid var(--border)',
                      cursor: 'pointer',
                      background: customerId === c.id ? 'color-mix(in oklch, var(--primary-500) 12%, transparent)' : 'transparent',
                    }}
                  >
                    <input type="radio" checked={customerId === c.id} onChange={() => setCustomerId(c.id)} />
                    <Avatar seed={company} label={initials(company)} size={26} />
                    <div className="col" style={{ flex: 1, lineHeight: 1.2 }}>
                      <div style={{ fontWeight: 600 }}>{company}</div>
                      <div className="muted" style={{ fontSize: 11 }}>{c.email}</div>
                    </div>
                    <Badge>{c.role}</Badge>
                  </label>
                );
              })
            )}
          </div>
        </div>
      )}

      {step === 1 && (
        <div className="col gap-3">
          <div className="muted" style={{ fontSize: 13 }}>Choose a service plan. This sets quotas + billing.</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 10 }}>
            {plans.map(p => (
              <label
                key={p.id}
                style={{
                  border: `1px solid ${planId === p.id ? 'var(--primary-500)' : 'var(--border)'}`,
                  borderRadius: 8,
                  padding: 12,
                  cursor: 'pointer',
                  background: planId === p.id ? 'color-mix(in oklch, var(--primary-500) 12%, transparent)' : 'var(--bg-2)',
                }}
              >
                <input type="radio" checked={planId === p.id} onChange={() => setPlanId(p.id)} style={{ marginRight: 6 }} />
                <span style={{ fontWeight: 700 }}>{p.name}</span>
                <div className="muted" style={{ fontSize: 11, marginTop: 6 }}>
                  ${p.pricePerPeriod.toFixed(2)}/mo · {(p.diskQuotaMb / 1024).toFixed(0)} GB · {(p.bandwidthLimitMb / 1024).toFixed(0)} GB BW
                </div>
                <div className="muted" style={{ fontSize: 11 }}>
                  {p.maxDomains} domain{p.maxDomains === 1 ? '' : 's'} · {p.maxEmailAccounts} mb · {p.maxDatabases} db
                </div>
              </label>
            ))}
          </div>
        </div>
      )}

      {step === 2 && (
        <div className="col gap-3">
          <Field label="Main domain" hint="The primary site for this subscription. You can add more later.">
            <input className="input mono" placeholder="example.com" value={mainDomain} onChange={e => setMainDomain(e.target.value.trim())} autoFocus />
          </Field>
          <div className="alert info"><Icons.info />Domain creates an nginx vhost + docroot + tenant system user when you finish.</div>
        </div>
      )}

      {step === 3 && (
        <div className="col gap-3">
          <div className="muted" style={{ fontSize: 13 }}>Review and create.</div>
          <ReviewRow label="Customer" value={customer ? (customer.companyName ?? customer.email) : '—'} />
          <ReviewRow label="Plan" value={plan ? `${plan.name} ($${plan.pricePerPeriod}/mo)` : '—'} />
          <ReviewRow label="Main domain" value={mainDomain || '—'} mono />
          <ReviewRow label="Disk quota" value={plan ? `${(plan.diskQuotaMb / 1024).toFixed(0)} GB` : '—'} />
          <ReviewRow label="Bandwidth" value={plan ? `${(plan.bandwidthLimitMb / 1024).toFixed(0)} GB / mo` : '—'} />
        </div>
      )}
    </Dialog>
  );
}

function StepBar({ step }: { step: StepIdx }) {
  const labels = ['Customer', 'Plan', 'Domain', 'Review'];
  return (
    <ol className="row gap-2" style={{ listStyle: 'none', padding: 0, margin: '0 0 16px', flexWrap: 'wrap' }}>
      {labels.map((l, i) => {
        const status = i < step ? 'done' : i === step ? 'current' : 'pending';
        return (
          <li
            key={l}
            className="row gap-2"
            style={{
              fontSize: 11,
              padding: '4px 10px',
              borderRadius: 999,
              border: '1px solid var(--border)',
              background: status === 'current' ? 'color-mix(in oklch, var(--primary-500) 18%, transparent)' : 'var(--surface)',
              color: status === 'pending' ? 'var(--muted)' : 'var(--text)',
              fontWeight: status === 'current' ? 600 : 500,
            }}
          >
            <span style={{ width: 14, height: 14, display: 'grid', placeItems: 'center' }}>
              {status === 'done' ? <Icons.check style={{ color: 'var(--success)' }} /> : i + 1}
            </span>
            {l}
          </li>
        );
      })}
    </ol>
  );
}

function ReviewRow({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="row between" style={{ padding: '8px 0', borderBottom: '1px solid var(--border)' }}>
      <span className="muted" style={{ fontSize: 12 }}>{label}</span>
      <span className={mono ? 'mono' : ''} style={{ fontWeight: 600, fontSize: 13 }}>{value}</span>
    </div>
  );
}

// ──────────────── suspend / resume / cancel confirm ────────────────
function ConfirmDialog({
  confirming, onClose, onDone,
}: {
  confirming: { sub: PanelSubscription; action: ConfirmAction } | null;
  onClose: () => void;
  onDone: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => { setErr(null); setBusy(false); }, [confirming]);

  if (!confirming) return null;
  const { sub, action } = confirming;
  const domain = sub.domains?.[0]?.name ?? `subscription #${sub.id}`;

  const meta = {
    suspend: {
      title: `Suspend ${domain}?`,
      sub: 'The site stops serving traffic. The customer will see a maintenance page; data is preserved.',
      confirmLabel: 'Suspend',
      confirmTone: 'danger' as const,
      consequences: [
        'HTTP/HTTPS requests return 503 from nginx',
        'Outbound email from this tenant is paused',
        'Cron jobs do not fire',
        'Files, databases, and DNS records are kept intact',
      ],
      run: () => api.post(`/subscriptions/${sub.id}/suspend`),
    },
    resume: {
      title: `Resume ${domain}?`,
      sub: 'The site goes back online. No data is touched — this just lifts the suspend.',
      confirmLabel: 'Resume',
      confirmTone: 'primary' as const,
      consequences: [
        'nginx starts serving the site again',
        'Cron jobs resume on their next scheduled tick',
        'Outbound email is allowed',
      ],
      run: () => api.post(`/subscriptions/${sub.id}/resume`),
    },
    cancel: {
      title: `Cancel ${domain}?`,
      sub: 'Marks the subscription cancelled. Files + databases stay until manually removed.',
      confirmLabel: 'Cancel subscription',
      confirmTone: 'danger' as const,
      consequences: [
        'Site is suspended and marked Cancelled',
        'No new invoices will be generated',
        'Tenant system user is removed (data files remain on disk)',
        'You can fully delete data later from the customer page',
      ],
      run: () => api.delete(`/subscriptions/${sub.id}`),
    },
  }[action];

  const submit = async () => {
    setBusy(true);
    setErr(null);
    try {
      await meta.run();
      onDone();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? (e instanceof Error ? e.message : `${meta.confirmLabel} failed`));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={meta.title}
      sub={meta.sub}
      foot={
        <>
          {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
          <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button className={`btn ${meta.confirmTone}`} onClick={submit} disabled={busy}>
            {busy ? 'Working…' : meta.confirmLabel}
          </button>
        </>
      }
    >
      <div className="alert warn"><Icons.warn /><div>Here's what happens:</div></div>
      <ul style={{ margin: '12px 0 0', paddingLeft: 18, fontSize: 13, lineHeight: 1.7 }}>
        {meta.consequences.map(c => <li key={c}>{c}</li>)}
      </ul>
    </Dialog>
  );
}
