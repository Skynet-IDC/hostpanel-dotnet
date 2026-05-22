import { useEffect, useMemo, useState } from 'react';
import api from '../api/client';
import { Card, Dialog, Empty, Field, IconBtn, PageHead, Stat, StatusBadge, Tabs } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface Invoice {
  id: number;
  subscriptionId: number;
  subscriptionName?: string;
  provider?: string;
  providerInvoiceId?: string | null;
  amount: number;
  currency: string;
  status: string;
  dueDate: string;
  paidDate: string | null;
  createdAt: string;
}

type TabId = 'all' | 'sent' | 'paid' | 'overdue' | 'cancelled';

export function BillingPage() {
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [subs, setSubs] = useState<{ id: number; name: string }[]>([]);
  const [filterSub, setFilterSub] = useState<number | ''>('');
  const [tab, setTab] = useState<TabId>('all');
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [genOpen, setGenOpen] = useState(false);
  const [genSubId, setGenSubId] = useState<number | ''>('');
  const [genAmount, setGenAmount] = useState('');
  // F1: per-invoice provider override. Empty string = "inherit from subscription".
  const [genProvider, setGenProvider] = useState<'' | 'Stripe' | 'PayPal'>('');
  const [genBusy, setGenBusy] = useState(false);
  const [genErr, setGenErr] = useState<string | null>(null);
  const [genPaymentLink, setGenPaymentLink] = useState<string | null>(null);

  const reload = async () => {
    try {
      const [inv, sub] = await Promise.all([api.get<Invoice[]>('/billing/invoices'), api.get<{ id: number; name: string }[]>('/subscriptions')]);
      setInvoices(inv.data); setSubs(sub.data);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Load failed');
    } finally { setLoading(false); }
  };
  useEffect(() => { void reload(); }, []);

  const generateInvoice = async () => {
    if (!genSubId) { setGenErr('Pick a subscription'); return; }
    setGenBusy(true); setGenErr(null); setGenPaymentLink(null);
    try {
      const body: Record<string, unknown> = { subscriptionId: genSubId };
      if (genAmount && !isNaN(Number(genAmount))) body.amount = Number(genAmount);
      if (genProvider) body.provider = genProvider;
      const r = await api.post<{ paymentLink?: string | null; providerInvoiceId?: string | null }>('/billing/invoices/generate', body);
      // F1: backend returns paymentLink (Stripe/PayPal hosted URL). Show it inline so the
      // operator can copy the link to email/Slack without bouncing through the providers' dashboards.
      if (r.data.paymentLink) {
        setGenPaymentLink(r.data.paymentLink);
      } else {
        setGenOpen(false);
        setGenAmount('');
        setGenSubId('');
        setGenProvider('');
      }
      void reload();
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { message?: string } } }).response?.data?.message
        : null;
      setGenErr(msg ?? (e instanceof Error ? e.message : 'Generate failed'));
    } finally { setGenBusy(false); }
  };

  const filtered = useMemo(() => {
    let list = invoices;
    if (filterSub) list = list.filter(i => i.subscriptionId === filterSub);
    if (tab !== 'all') list = list.filter(i => i.status.toLowerCase() === tab);
    return list;
  }, [invoices, filterSub, tab]);

  const totals = useMemo(() => {
    const paid = invoices.filter(i => i.status.toLowerCase() === 'paid').reduce((s, i) => s + i.amount, 0);
    const outstanding = invoices.filter(i => ['sent', 'overdue'].includes(i.status.toLowerCase())).reduce((s, i) => s + i.amount, 0);
    return { paid, outstanding };
  }, [invoices]);

  const downloadPdf = (id: number) => {
    window.open(`/api/v1/billing/invoices/${id}/pdf`, '_blank');
  };

  return (
    <>
      <PageHead
        title="Billing"
        sub="Invoices, payments, and overdue tracking"
        actions={
          <>
            <select className="select" value={filterSub} onChange={e => setFilterSub(e.target.value ? Number(e.target.value) : '')} style={{ maxWidth: 240 }}>
              <option value="">All subscriptions</option>
              {subs.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
            <button className="btn primary" onClick={() => { setGenSubId(filterSub || ''); setGenErr(null); setGenOpen(true); }}>
              <Icons.plus />Generate invoice
            </button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      <div className="stats" style={{ marginBottom: 16, gridTemplateColumns: 'repeat(3, 1fr)' }}>
        <Stat label="Total paid" icon="check2" value={`$${totals.paid.toFixed(2)}`} foot={<span className="muted">lifetime</span>} />
        <Stat label="Outstanding" icon="clock" value={`$${totals.outstanding.toFixed(2)}`} foot={<span className="muted">sent + overdue</span>} />
        <Stat label="Invoices" icon="package" value={String(invoices.length)} foot={<span className="muted">total in database</span>} />
      </div>

      <Tabs active={tab} onChange={id => setTab(id as TabId)} tabs={[
        { id: 'all', label: 'All', count: invoices.length },
        { id: 'sent', label: 'Sent', count: invoices.filter(i => i.status.toLowerCase() === 'sent').length },
        { id: 'paid', label: 'Paid', count: invoices.filter(i => i.status.toLowerCase() === 'paid').length },
        { id: 'overdue', label: 'Overdue', count: invoices.filter(i => i.status.toLowerCase() === 'overdue').length },
        { id: 'cancelled', label: 'Cancelled', count: invoices.filter(i => i.status.toLowerCase() === 'cancelled').length },
      ]} />

      <Card flush>
        {loading ? <div className="muted" style={{ padding: 16 }}>Loading…</div>
          : filtered.length === 0 ? <Empty icon="package" title="No invoices" sub="They appear here once the billing cycle runs (daily midnight UTC)." />
          : (
            <table className="tbl">
              <thead><tr><th>#</th><th>Subscription</th><th>Provider</th><th>Amount</th><th>Status</th><th>Due</th><th>Paid</th><th style={{ textAlign: 'right' }} /></tr></thead>
              <tbody>
                {filtered.map(inv => (
                  <tr key={inv.id}>
                    <td className="mono" style={{ fontWeight: 600 }}>#{String(inv.id).padStart(6, '0')}</td>
                    <td>{inv.subscriptionName ?? `sub #${inv.subscriptionId}`}</td>
                    <td className="muted">{inv.provider ?? '—'}</td>
                    <td className="mono">{inv.amount.toFixed(2)} {inv.currency}</td>
                    <td><StatusBadge status={inv.status} /></td>
                    <td className="muted mono" style={{ fontSize: 11.5 }}>{new Date(inv.dueDate).toLocaleDateString()}</td>
                    <td className="muted mono" style={{ fontSize: 11.5 }}>{inv.paidDate ? new Date(inv.paidDate).toLocaleDateString() : '—'}</td>
                    <td><div className="row-actions"><IconBtn icon="download" title="Download invoice" onClick={() => downloadPdf(inv.id)} /></div></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </Card>

      <Dialog
        open={genOpen}
        onClose={() => setGenOpen(false)}
        title="Generate invoice now"
        sub="Bypasses the daily billing cycle — useful for signup invoices, ad-hoc charges, or pro-rations."
        foot={
          <>
            {genErr && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{genErr}</span>}
            <button className="btn ghost" onClick={() => setGenOpen(false)} disabled={genBusy}>Cancel</button>
            <button className="btn primary" onClick={generateInvoice} disabled={genBusy || !genSubId}>
              {genBusy ? 'Generating…' : 'Generate'}
            </button>
          </>
        }
      >
        <div className="col gap-3">
          <Field label="Subscription">
            <select className="select" value={genSubId} onChange={e => setGenSubId(e.target.value ? Number(e.target.value) : '')}>
              <option value="">— pick one —</option>
              {subs.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </Field>
          <Field label="Amount (optional)" hint="Leave blank to use the plan's price for the subscription's current billing cycle (honors per-cycle overrides).">
            <input className="input mono" type="number" step="0.01" value={genAmount} onChange={e => setGenAmount(e.target.value)} placeholder="e.g. 49.00" />
          </Field>
          <Field label="Payment provider" hint="Inherit = use the subscription's PreferredPaymentProvider. Override to send this single invoice via the other provider.">
            <select className="select" value={genProvider} onChange={e => setGenProvider(e.target.value as '' | 'Stripe' | 'PayPal')}>
              <option value="">Inherit from subscription</option>
              <option value="Stripe">Stripe</option>
              <option value="PayPal">PayPal</option>
            </select>
          </Field>
          {genPaymentLink && (
            <div className="alert success" style={{ flexDirection: 'column', alignItems: 'flex-start', gap: 6 }}>
              <strong style={{ fontSize: 13 }}>Invoice created — payment link:</strong>
              <a href={genPaymentLink} target="_blank" rel="noreferrer" className="mono" style={{ fontSize: 11.5, wordBreak: 'break-all' }}>
                {genPaymentLink}
              </a>
              <button className="btn sm ghost" onClick={() => navigator.clipboard.writeText(genPaymentLink)}>Copy link</button>
            </div>
          )}
        </div>
      </Dialog>
    </>
  );
}
