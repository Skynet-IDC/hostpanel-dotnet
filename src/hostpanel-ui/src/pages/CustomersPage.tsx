import { useEffect, useMemo, useState } from 'react';
import api from '../api/client';
import { Avatar, Badge, Card, Checkbox, Dialog, Empty, Field, IconBtn, PageHead, StatusBadge, Tabs, initials } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';

interface Customer {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  company?: string | null;
  companyName?: string | null;
  role: string;
  isActive: boolean;
  subscriptionCount?: number;
  mfaEnabled?: boolean;
  createdAt?: string;
}

function displayCompany(c: Customer) {
  return c.companyName || c.company || `${c.firstName} ${c.lastName}`.trim() || c.email;
}

function displayName(c: Customer) {
  return [c.firstName, c.lastName].filter(Boolean).join(' ').trim() || c.email;
}

type TabId = 'all' | 'customers' | 'resellers' | 'admins' | 'inactive';

export function CustomersPage() {
  const [tab, setTab] = useState<TabId>('all');
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<Customer | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.get<Customer[]>('/customers');
      setCustomers(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load customers');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const filtered = useMemo(() => {
    let list = customers;
    if (tab === 'customers') list = list.filter(c => c.role.toLowerCase() === 'customer');
    else if (tab === 'resellers') list = list.filter(c => c.role.toLowerCase() === 'reseller');
    else if (tab === 'admins') list = list.filter(c => c.role.toLowerCase() === 'admin');
    else if (tab === 'inactive') list = list.filter(c => !c.isActive);
    if (search.trim()) {
      const q = search.toLowerCase();
      list = list.filter(c =>
        c.email.toLowerCase().includes(q)
        || displayCompany(c).toLowerCase().includes(q)
        || displayName(c).toLowerCase().includes(q),
      );
    }
    return list;
  }, [customers, tab, search]);

  const counts = useMemo(() => ({
    all: customers.length,
    customers: customers.filter(c => c.role.toLowerCase() === 'customer').length,
    resellers: customers.filter(c => c.role.toLowerCase() === 'reseller').length,
    admins: customers.filter(c => c.role.toLowerCase() === 'admin').length,
    inactive: customers.filter(c => !c.isActive).length,
  }), [customers]);

  const onCreated = () => { setCreateOpen(false); void load(); };
  const onSaved = () => { setEditing(null); void load(); };

  return (
    <>
      <PageHead
        title="Customers"
        sub={`${customers.length} account${customers.length === 1 ? '' : 's'}`}
        actions={
          <>
            <button className="btn"><Icons.download />Export CSV</button>
            <button className="btn primary" onClick={() => setCreateOpen(true)}>
              <Icons.plus />New customer
            </button>
          </>
        }
      />

      <Tabs
        active={tab}
        onChange={id => setTab(id as TabId)}
        tabs={[
          { id: 'all', label: 'All', count: counts.all },
          { id: 'customers', label: 'Customers', count: counts.customers },
          { id: 'resellers', label: 'Resellers', count: counts.resellers },
          { id: 'admins', label: 'Admins', count: counts.admins },
          { id: 'inactive', label: 'Inactive', count: counts.inactive },
        ]}
      />

      <div className="row gap-2" style={{ marginBottom: 12 }}>
        <div
          className="row gap-2"
          style={{ flex: 1, background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 6, padding: '0 10px', height: 30 }}
        >
          <Icons.search style={{ width: 14, height: 14, color: 'var(--muted)' }} />
          <input
            className="input"
            placeholder="Search by name, email, company…"
            value={search}
            onChange={e => setSearch(e.target.value)}
            style={{ border: 'none', background: 'transparent', padding: 0, height: 28 }}
          />
        </div>
      </div>

      {error && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{error}</div>}

      <Card flush>
        {loading ? (
          <div className="muted" style={{ padding: 16 }}>Loading…</div>
        ) : filtered.length === 0 ? (
          <Empty
            icon="users"
            title={customers.length === 0 ? 'No customers yet' : 'No customers match'}
            sub={customers.length === 0 ? 'Add your first customer to start hosting their sites.' : 'Try a different search or tab.'}
            action={customers.length === 0 ? (
              <button className="btn primary" onClick={() => setCreateOpen(true)} style={{ marginTop: 4 }}>
                <Icons.plus />Add customer
              </button>
            ) : undefined}
          />
        ) : (
          <table className="tbl">
            <thead>
              <tr>
                <th style={{ width: 26 }}><Checkbox /></th>
                <th>Customer</th>
                <th>Email</th>
                <th>Role</th>
                <th>MFA</th>
                <th>Status</th>
                <th style={{ textAlign: 'right' }} />
              </tr>
            </thead>
            <tbody>
              {filtered.map(c => (
                <tr key={c.id} onClick={() => setEditing(c)} style={{ cursor: 'pointer' }}>
                  <td onClick={e => e.stopPropagation()}><Checkbox /></td>
                  <td>
                    <div className="row gap-3">
                      <Avatar seed={displayCompany(c)} label={initials(displayCompany(c))} size={26} />
                      <div className="col" style={{ lineHeight: 1.2 }}>
                        <div style={{ fontWeight: 600 }}>{displayCompany(c)}</div>
                        <div className="muted" style={{ fontSize: 11 }}>{displayName(c)}</div>
                      </div>
                    </div>
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>{c.email}</td>
                  <td>
                    {c.role.toLowerCase() === 'reseller'
                      ? <Badge tone="primary">Reseller</Badge>
                      : c.role.toLowerCase() === 'admin'
                        ? <Badge tone="warn">Admin</Badge>
                        : <Badge>Customer</Badge>}
                  </td>
                  <td>{c.mfaEnabled ? <Badge tone="success" dot>On</Badge> : <Badge>Off</Badge>}</td>
                  <td><StatusBadge status={c.isActive ? 'active' : 'disabled'} /></td>
                  <td>
                    <div className="row-actions">
                      <IconBtn icon="edit" title="Edit" onClick={() => setEditing(c)} />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <CustomerForm open={createOpen} onClose={() => setCreateOpen(false)} onSaved={onCreated} />
      <CustomerForm open={!!editing} onClose={() => setEditing(null)} customer={editing ?? undefined} onSaved={onSaved} />
    </>
  );
}

interface FormProps {
  open: boolean;
  onClose: () => void;
  customer?: Customer;
  onSaved: () => void;
}

function CustomerForm({ open, onClose, customer, onSaved }: FormProps) {
  const [firstName, setFirstName] = useState(customer?.firstName ?? '');
  const [lastName, setLastName] = useState(customer?.lastName ?? '');
  const [email, setEmail] = useState(customer?.email ?? '');
  const [company, setCompany] = useState(customer?.companyName ?? customer?.company ?? '');
  const [role, setRole] = useState(customer?.role.toLowerCase() ?? 'customer');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setFirstName(customer?.firstName ?? '');
    setLastName(customer?.lastName ?? '');
    setEmail(customer?.email ?? '');
    setCompany(customer?.companyName ?? customer?.company ?? '');
    setRole(customer?.role.toLowerCase() ?? 'customer');
    setPassword('');
    setErr(null);
  }, [open, customer]);

  const submit = async () => {
    setBusy(true);
    setErr(null);
    try {
      if (customer) {
        await api.put(`/customers/${customer.id}`, { firstName, lastName, company: company || null, role });
      } else {
        await api.post('/customers', { email, firstName, lastName, company: company || null, password, role });
      }
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
      title={customer ? `Edit ${displayCompany(customer)}` : 'New customer'}
      sub={customer ? customer.email : 'Create a customer account with login credentials.'}
      foot={
        <>
          {err && <span style={{ color: 'var(--danger)', fontSize: 12, marginRight: 'auto' }}>{err}</span>}
          <button className="btn ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn primary" onClick={submit} disabled={busy}>{busy ? 'Saving…' : customer ? 'Save changes' : 'Create customer'}</button>
        </>
      }
    >
      <div className="grid-2" style={{ gap: 14 }}>
        <Field label="First name">
          <input className="input" value={firstName} onChange={e => setFirstName(e.target.value)} autoFocus />
        </Field>
        <Field label="Last name">
          <input className="input" value={lastName} onChange={e => setLastName(e.target.value)} />
        </Field>
        <Field label="Email (login)">
          <input className="input mono" type="email" value={email} onChange={e => setEmail(e.target.value)} disabled={!!customer} />
        </Field>
        <Field label="Company (optional)">
          <input className="input" value={company} onChange={e => setCompany(e.target.value)} />
        </Field>
        <Field label="Role" span={2}>
          <div className="row gap-2">
            {(['customer', 'reseller', 'admin'] as const).map(r => (
              <label
                key={r}
                className="row gap-2"
                style={{
                  flex: 1,
                  border: `1px solid ${role === r ? 'var(--primary-500)' : 'var(--border)'}`,
                  borderRadius: 6,
                  padding: '6px 10px',
                  cursor: 'pointer',
                  background: role === r ? 'color-mix(in oklch, var(--primary-500) 14%, transparent)' : 'var(--bg-2)',
                }}
              >
                <input type="radio" name="role" checked={role === r} onChange={() => setRole(r)} />
                <span style={{ textTransform: 'capitalize' }}>{r}</span>
              </label>
            ))}
          </div>
        </Field>
        {!customer && (
          <Field label="Initial password" span={2} hint="Min 12 characters · sent only at creation, not stored in plaintext.">
            <input className="input mono" type="password" value={password} onChange={e => setPassword(e.target.value)} placeholder="••••••••••••" />
          </Field>
        )}
        {customer && (
          <Field label="Customer branding override" span={2} hint="Customer's portal + login page use these instead of the global panel branding.">
            <CustomerBrandingPanel customerId={customer.id} />
          </Field>
        )}
      </div>
    </Dialog>
  );
}

interface CustomerBrandingOverride {
  panelName?: string | null;
  logoUrl?: string | null;
  primaryColor?: string | null;
  loginMessage?: string | null;
  loginBackgroundUrl?: string | null;
}

function CustomerBrandingPanel({ customerId }: { customerId: string }) {
  const [data, setData] = useState<CustomerBrandingOverride>({});
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    api.get(`/branding?customerId=${encodeURIComponent(customerId)}`)
      .then(r => { if (alive) setData({ logoUrl: r.data?.logoUrl, primaryColor: r.data?.primaryColor, panelName: r.data?.panelName, loginMessage: r.data?.loginMessage }); })
      .catch(() => { /* ignore */ });
    return () => { alive = false; };
  }, [customerId]);

  const save = async () => {
    setBusy(true); setMsg(null);
    try {
      await api.put(`/branding/customer/${encodeURIComponent(customerId)}`, data);
      setMsg('Saved');
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Save failed');
    } finally {
      setBusy(false);
    }
  };

  const reset = async () => {
    if (!window.confirm('Clear this customer\'s branding override?')) return;
    setBusy(true); setMsg(null);
    try {
      await api.delete(`/branding/customer/${encodeURIComponent(customerId)}`);
      setData({});
      setMsg('Reset');
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Reset failed');
    } finally {
      setBusy(false);
    }
  };

  const upload = async (f: File) => {
    setBusy(true); setMsg(null);
    try {
      const fd = new FormData();
      fd.append('file', f);
      const r = await api.post(`/branding/customer/${encodeURIComponent(customerId)}/logo`, fd, { headers: { 'Content-Type': 'multipart/form-data' } });
      setData(d => ({ ...d, logoUrl: r.data?.url ?? d.logoUrl }));
      setMsg('Logo uploaded');
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Upload failed');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="col gap-2" style={{ padding: 10, border: '1px solid var(--border)', borderRadius: 6, background: 'var(--bg-2)' }}>
      <div className="row gap-3" style={{ alignItems: 'center' }}>
        {data.logoUrl ? <img src={data.logoUrl} alt="" style={{ height: 36, maxWidth: 120, objectFit: 'contain', background: '#fff', borderRadius: 4, padding: 2 }} /> : <span className="dim" style={{ fontSize: 12 }}>No logo</span>}
        <label className="btn ghost" style={{ marginLeft: 'auto', cursor: 'pointer' }}>
          Upload logo
          <input type="file" accept="image/png,image/jpeg,image/svg+xml,image/webp" style={{ display: 'none' }} onChange={e => e.target.files?.[0] && upload(e.target.files[0])} disabled={busy} />
        </label>
      </div>
      <div className="grid-2" style={{ gap: 10 }}>
        <Field label="Panel name override">
          <input className="input" value={data.panelName ?? ''} onChange={e => setData(d => ({ ...d, panelName: e.target.value }))} placeholder="(uses global)" />
        </Field>
        <Field label="Primary color (#hex)">
          <input className="input mono" value={data.primaryColor ?? ''} onChange={e => setData(d => ({ ...d, primaryColor: e.target.value }))} placeholder="#2563eb" />
        </Field>
        <Field label="Login message" span={2}>
          <input className="input" value={data.loginMessage ?? ''} onChange={e => setData(d => ({ ...d, loginMessage: e.target.value }))} placeholder="Welcome…" />
        </Field>
      </div>
      <div className="row gap-2">
        <button type="button" className="btn primary" onClick={save} disabled={busy}>{busy ? 'Saving…' : 'Save override'}</button>
        <button type="button" className="btn ghost" onClick={reset} disabled={busy}>Clear override</button>
        {msg && <span className="dim" style={{ fontSize: 12, marginLeft: 'auto' }}>{msg}</span>}
      </div>
    </div>
  );
}
