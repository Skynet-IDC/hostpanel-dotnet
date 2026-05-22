import { useEffect, useState } from 'react';
import api from '../api/client';
import { Card, Dialog, Empty, Field, IconBtn, PageHead } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';
import { panelApi, type PanelPlan } from '../api/panel';

export function PlansPage() {
  const [plans, setPlans] = useState<PanelPlan[]>([]);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<PanelPlan | null>(null);
  const [err, setErr] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setErr(null);
    try {
      const data = await panelApi.listPlans();
      setPlans(data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load plans');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  return (
    <>
      <PageHead
        title="Service plans"
        sub={`${plans.length} plan${plans.length === 1 ? '' : 's'} · resource limits and pricing`}
        actions={
          <button className="btn primary" onClick={() => setCreateOpen(true)}>
            <Icons.plus />New plan
          </button>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {loading ? (
        <Card><div className="muted" style={{ padding: 8 }}>Loading…</div></Card>
      ) : plans.length === 0 ? (
        <Card>
          <Empty
            icon="package"
            title="No service plans yet"
            sub="Plans define what a customer subscription includes — disk, bandwidth, mailboxes, price. You need at least one before you can sell hosting."
            action={
              <button className="btn primary" onClick={() => setCreateOpen(true)} style={{ marginTop: 4 }}>
                <Icons.plus />Create first plan
              </button>
            }
          />
        </Card>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 12 }}>
          {plans.map(p => (
            <PlanCard key={p.id} plan={p} onEdit={() => setEditing(p)} />
          ))}
        </div>
      )}

      <PlanForm open={createOpen} onClose={() => setCreateOpen(false)} onSaved={() => { setCreateOpen(false); void load(); }} />
      <PlanForm open={!!editing} plan={editing ?? undefined} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); void load(); }} />
    </>
  );
}

function PlanCard({ plan, onEdit }: { plan: PanelPlan; onEdit: () => void }) {
  return (
    <div className="card" style={{ display: 'flex', flexDirection: 'column' }}>
      <div className="card-head">
        <div>
          <div className="card-title">{plan.name}</div>
          <div className="card-sub">{(plan.diskQuotaMb / 1024).toFixed(0)} GB disk</div>
        </div>
        <div className="row gap-1">
          <IconBtn icon="edit" title="Edit" onClick={onEdit} />
        </div>
      </div>
      <div className="card-body" style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div className="row" style={{ alignItems: 'baseline', gap: 4 }}>
          <span style={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.02em' }}>${plan.pricePerPeriod.toFixed(2)}</span>
          <span className="muted" style={{ fontSize: 12 }}>/ month</span>
        </div>
        <ul className="col gap-2" style={{ listStyle: 'none', padding: 0, margin: 0, fontSize: 12.5 }}>
          <PlanLine label={`${(plan.diskQuotaMb / 1024).toFixed(0)} GB disk space`} />
          <PlanLine label={`${(plan.bandwidthLimitMb / 1024).toFixed(0)} GB bandwidth / month`} />
          <PlanLine label={`${plan.maxDomains} ${plan.maxDomains === 1 ? 'domain' : 'domains'}`} />
          <PlanLine label={`${plan.maxEmailAccounts} mailbox${plan.maxEmailAccounts === 1 ? '' : 'es'}`} />
          <PlanLine label={`${plan.maxDatabases} database${plan.maxDatabases === 1 ? '' : 's'}`} />
        </ul>
      </div>
      <div style={{ padding: '10px 16px', borderTop: '1px solid var(--border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: 'var(--surface-2)' }}>
        <span className="muted" style={{ fontSize: 11 }}>plan_id: <span className="mono">{plan.id}</span></span>
        <button className="btn ghost sm" onClick={onEdit}>Edit →</button>
      </div>
    </div>
  );
}

function PlanLine({ label }: { label: string }) {
  return (
    <li className="row gap-2">
      <Icons.check style={{ width: 13, height: 13, color: 'var(--success)' }} />
      {label}
    </li>
  );
}

interface PlanFormState {
  name: string;
  pricePerPeriod: number;
  // F1: per-cycle override prices. Blank/null means "fall back to linear math against
  // pricePerPeriod for this cycle". Storing as string in the form state so the operator
  // can type partial values (e.g. "27") without it auto-coercing to 0.
  priceMonthly: string;
  priceQuarterly: string;
  priceSemiAnnual: string;
  priceAnnual: string;
  diskGb: number;
  bandwidthGb: number;
  maxDomains: number;
  maxEmailAccounts: number;
  maxDatabases: number;
}

function defaultsFrom(p?: PanelPlan): PlanFormState {
  return {
    name: p?.name ?? '',
    pricePerPeriod: p?.pricePerPeriod ?? 0,
    priceMonthly: p?.priceMonthly !== undefined && p?.priceMonthly !== null ? String(p.priceMonthly) : '',
    priceQuarterly: p?.priceQuarterly !== undefined && p?.priceQuarterly !== null ? String(p.priceQuarterly) : '',
    priceSemiAnnual: p?.priceSemiAnnual !== undefined && p?.priceSemiAnnual !== null ? String(p.priceSemiAnnual) : '',
    priceAnnual: p?.priceAnnual !== undefined && p?.priceAnnual !== null ? String(p.priceAnnual) : '',
    diskGb: p ? Math.round(p.diskQuotaMb / 1024) : 5,
    bandwidthGb: p ? Math.round(p.bandwidthLimitMb / 1024) : 50,
    maxDomains: p?.maxDomains ?? 1,
    maxEmailAccounts: p?.maxEmailAccounts ?? 5,
    maxDatabases: p?.maxDatabases ?? 1,
  };
}

function PlanForm({ open, onClose, plan, onSaved }: { open: boolean; onClose: () => void; plan?: PanelPlan; onSaved: () => void }) {
  const [s, setS] = useState<PlanFormState>(() => defaultsFrom(plan));
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setS(defaultsFrom(plan));
    setErr(null);
  }, [open, plan]);

  const set = <K extends keyof PlanFormState>(k: K, v: PlanFormState[K]) => setS(p => ({ ...p, [k]: v }));

  const submit = async () => {
    setBusy(true);
    setErr(null);
    try {
      // F1: send per-cycle overrides only when filled in. Empty strings become null so
      // the backend keeps the linear-fallback behavior on a freshly created plan.
      const toNum = (v: string): number | null => v.trim() === '' ? null : Number(v);
      const body = {
        name: s.name.trim(),
        pricePerPeriod: s.pricePerPeriod,
        priceMonthly: toNum(s.priceMonthly),
        priceQuarterly: toNum(s.priceQuarterly),
        priceSemiAnnual: toNum(s.priceSemiAnnual),
        priceAnnual: toNum(s.priceAnnual),
        diskQuotaMb: s.diskGb * 1024,
        bandwidthLimitMb: s.bandwidthGb * 1024,
        maxDomains: s.maxDomains,
        maxEmailAccounts: s.maxEmailAccounts,
        maxDatabases: s.maxDatabases,
      };
      if (plan) await api.put(`/serviceplans/${plan.id}`, body);
      else await api.post('/serviceplans', body);
      onSaved();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      setErr(msg ?? (e instanceof Error ? e.message : 'Save failed'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="lg"
      title={plan ? `Edit plan — ${plan.name}` : 'New service plan'}
      sub="Resource limits, pricing, and what's included for subscribers."
      foot={
        <>
          {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
          <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn primary" onClick={submit} disabled={busy || !s.name.trim()}>
            {busy ? 'Saving…' : plan ? 'Save changes' : 'Create plan'}
          </button>
        </>
      }
    >
      <div className="grid-2" style={{ gap: 14 }}>
        <Field label="Plan name" span={2}>
          <input className="input" value={s.name} onChange={e => set('name', e.target.value)} autoFocus />
        </Field>
        <Field label="Base price (USD)" hint="Used as fallback when a cycle below isn't filled in. Linear math: e.g. base $100 → annual $1200 unless overridden.">
          <input className="input mono" type="number" min={0} step={0.01} value={s.pricePerPeriod} onChange={e => set('pricePerPeriod', Number(e.target.value))} />
        </Field>
        <Field label="" hint="" span={2}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 10 }}>
            <Field label="1 mo override">
              <input className="input mono" type="number" min={0} step={0.01} placeholder="auto"
                value={s.priceMonthly} onChange={e => set('priceMonthly', e.target.value)} />
            </Field>
            <Field label="3 mo override">
              <input className="input mono" type="number" min={0} step={0.01} placeholder="auto"
                value={s.priceQuarterly} onChange={e => set('priceQuarterly', e.target.value)} />
            </Field>
            <Field label="6 mo override">
              <input className="input mono" type="number" min={0} step={0.01} placeholder="auto"
                value={s.priceSemiAnnual} onChange={e => set('priceSemiAnnual', e.target.value)} />
            </Field>
            <Field label="12 mo override">
              <input className="input mono" type="number" min={0} step={0.01} placeholder="auto"
                value={s.priceAnnual} onChange={e => set('priceAnnual', e.target.value)} />
            </Field>
          </div>
        </Field>
        <Field label="Disk space (GB)">
          <input className="input mono" type="number" min={1} value={s.diskGb} onChange={e => set('diskGb', Number(e.target.value))} />
        </Field>
        <Field label="Monthly bandwidth (GB)">
          <input className="input mono" type="number" min={1} value={s.bandwidthGb} onChange={e => set('bandwidthGb', Number(e.target.value))} />
        </Field>
        <Field label="Max domains">
          <input className="input mono" type="number" min={1} value={s.maxDomains} onChange={e => set('maxDomains', Number(e.target.value))} />
        </Field>
        <Field label="Max mailboxes">
          <input className="input mono" type="number" min={0} value={s.maxEmailAccounts} onChange={e => set('maxEmailAccounts', Number(e.target.value))} />
        </Field>
        <Field label="Max databases">
          <input className="input mono" type="number" min={0} value={s.maxDatabases} onChange={e => set('maxDatabases', Number(e.target.value))} />
        </Field>
      </div>
    </Dialog>
  );
}
