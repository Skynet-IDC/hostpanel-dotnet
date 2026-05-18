import { useEffect, useMemo, useState } from 'react';
import api from '../api/client';
import { Card, Empty, IconBtn, PageHead, Stat, StatusBadge, Tabs } from '../components/ui/primitives';
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

  useEffect(() => {
    (async () => {
      try {
        const [inv, sub] = await Promise.all([api.get<Invoice[]>('/billing/invoices'), api.get<{ id: number; name: string }[]>('/subscriptions')]);
        setInvoices(inv.data); setSubs(sub.data);
      } catch (e) {
        setErr(e instanceof Error ? e.message : 'Load failed');
      } finally { setLoading(false); }
    })();
  }, []);

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
          <select className="select" value={filterSub} onChange={e => setFilterSub(e.target.value ? Number(e.target.value) : '')} style={{ maxWidth: 240 }}>
            <option value="">All subscriptions</option>
            {subs.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
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
    </>
  );
}
