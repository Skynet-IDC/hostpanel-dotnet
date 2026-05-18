import type { CSSProperties, ReactNode } from 'react';
import { Icons, type IconName } from './Icons';

/** Card with optional title/subtitle/actions and a flush-body variant. */
export function Card({
  title, sub, actions, children, flush, className = '', style,
}: {
  title?: ReactNode;
  sub?: ReactNode;
  actions?: ReactNode;
  children?: ReactNode;
  flush?: boolean;
  className?: string;
  style?: CSSProperties;
}) {
  return (
    <div className={`card ${className}`} style={style}>
      {(title || actions) && (
        <div className="card-head">
          <div>
            {title && <div className="card-title">{title}</div>}
            {sub && <div className="card-sub">{sub}</div>}
          </div>
          {actions && <div className="row gap-2">{actions}</div>}
        </div>
      )}
      <div className={`card-body ${flush ? 'flush' : ''}`}>{children}</div>
    </div>
  );
}

export type BadgeTone = 'default' | 'success' | 'warn' | 'danger' | 'info' | 'primary';

export function Badge({ tone = 'default', dot, children }: { tone?: BadgeTone; dot?: boolean; children: ReactNode }) {
  return (
    <span className={`badge ${tone === 'default' ? '' : tone}`}>
      {dot && <span className="dot" />}
      {children}
    </span>
  );
}

const STATUS_MAP: Record<string, { tone: BadgeTone; label: string }> = {
  active: { tone: 'success', label: 'Active' },
  running: { tone: 'success', label: 'Running' },
  valid: { tone: 'success', label: 'Valid' },
  ok: { tone: 'success', label: 'OK' },
  clean: { tone: 'success', label: 'Clean' },
  success: { tone: 'success', label: 'Success' },
  up: { tone: 'success', label: 'Up' },
  trial: { tone: 'info', label: 'Trial' },
  pending: { tone: 'warn', label: 'Pending' },
  warn: { tone: 'warn', label: 'Warning' },
  suspended: { tone: 'danger', label: 'Suspended' },
  stopped: { tone: 'default', label: 'Stopped' },
  idle: { tone: 'default', label: 'Idle' },
  fail: { tone: 'danger', label: 'Failed' },
  failed: { tone: 'danger', label: 'Failed' },
  infected: { tone: 'danger', label: 'Infected' },
  expired: { tone: 'danger', label: 'Expired' },
  disabled: { tone: 'default', label: 'Disabled' },
};

export function StatusBadge({ status }: { status: string }) {
  const entry = STATUS_MAP[status.toLowerCase()] ?? { tone: 'default' as BadgeTone, label: status };
  return <Badge tone={entry.tone} dot>{entry.label}</Badge>;
}

export function PageHead({
  title, sub, actions,
}: { title: ReactNode; sub?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="page-head">
      <div>
        <h1 className="page-title">{title}</h1>
        {sub && <p className="page-sub">{sub}</p>}
      </div>
      {actions && <div className="row gap-2">{actions}</div>}
    </div>
  );
}

export function Tabs({
  tabs, active, onChange,
}: {
  tabs: ReadonlyArray<{ id: string; label: string; count?: number }>;
  active: string;
  onChange: (id: string) => void;
}) {
  return (
    <div className="tabs">
      {tabs.map(t => (
        <div
          key={t.id}
          className="tab"
          data-active={t.id === active}
          onClick={() => onChange(t.id)}
          role="tab"
          aria-selected={t.id === active}
          tabIndex={0}
        >
          {t.label}
          {t.count != null && <span className="muted" style={{ marginLeft: 6 }}>{t.count}</span>}
        </div>
      ))}
    </div>
  );
}

export function pct(used: number, total: number) {
  if (!total) return 0;
  return Math.min(100, Math.round((used / total) * 100));
}

export function Meter({
  value, total, tone, label, hideText,
}: {
  value: number;
  total: number;
  tone?: 'warn' | 'danger' | '';
  label?: ReactNode;
  hideText?: boolean;
}) {
  const p = pct(value, total);
  const t = tone ?? (p > 90 ? 'danger' : p > 75 ? 'warn' : '');
  return (
    <div className="col gap-1" style={{ minWidth: 120 }}>
      {!hideText && (
        <div className="between" style={{ fontSize: 11, color: 'var(--muted)' }}>
          <span>{label}</span>
          <span className="mono" style={{ color: 'var(--text-2)' }}>{value} / {total}</span>
        </div>
      )}
      <div className={`meter ${t}`}><div className="fill" style={{ width: `${p}%` }} /></div>
    </div>
  );
}

export function Sparkline({
  data, height = 36, color = 'var(--primary-400)',
}: { data: number[]; height?: number; color?: string }) {
  const w = 100;
  const h = height;
  const max = Math.max(...data);
  const min = Math.min(...data);
  const range = max - min || 1;
  const pts = data.map((v, i) => `${(i / (data.length - 1)) * w},${h - ((v - min) / range) * (h - 4) - 2}`).join(' ');
  const area = `0,${h} ${pts} ${w},${h}`;
  return (
    <svg viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none" style={{ width: '100%', height: h }}>
      <polygon points={area} fill={color} opacity={0.16} />
      <polyline points={pts} fill="none" stroke={color} strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" vectorEffect="non-scaling-stroke" />
    </svg>
  );
}

export function AreaChart({
  data, height = 200, color = 'var(--primary-500)', xTicks,
}: {
  data: number[];
  height?: number;
  color?: string;
  xTicks?: string[];
}) {
  const w = 800;
  const pad = 28;
  const max = Math.max(...data) * 1.1;
  const min = 0;
  const range = max - min || 1;
  const xStep = (w - pad * 2) / (data.length - 1 || 1);
  const pts = data.map((v, i) => [pad + i * xStep, height - pad - ((v - min) / range) * (height - pad * 2)] as const);
  const linePath = 'M ' + pts.map(p => p.join(',')).join(' L ');
  const areaPath = `${linePath} L ${pad + (data.length - 1) * xStep} ${height - pad} L ${pad} ${height - pad} Z`;
  const yLines = 4;
  return (
    <svg viewBox={`0 0 ${w} ${height}`} preserveAspectRatio="none" style={{ width: '100%', height, display: 'block' }}>
      <g className="chart-grid">
        {Array.from({ length: yLines + 1 }).map((_, i) => {
          const y = pad + i * ((height - pad * 2) / yLines);
          return <line key={i} x1={pad} x2={w - pad} y1={y} y2={y} />;
        })}
      </g>
      <g className="chart-axis">
        {Array.from({ length: yLines + 1 }).map((_, i) => {
          const y = pad + i * ((height - pad * 2) / yLines);
          const v = Math.round((max - (max / yLines) * i));
          return <text key={i} x={pad - 6} y={y + 3} textAnchor="end">{v}</text>;
        })}
        {xTicks?.map((t, i) => (
          <text key={i} x={pad + (i / (xTicks.length - 1 || 1)) * (w - pad * 2)} y={height - 8} textAnchor="middle">{t}</text>
        ))}
      </g>
      <path d={areaPath} fill={`color-mix(in oklch, ${color} 18%, transparent)`} />
      <path d={linePath} fill="none" stroke={color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
      {pts.length > 0 && (
        <circle cx={pts[pts.length - 1][0]} cy={pts[pts.length - 1][1]} r={3} fill={color} />
      )}
    </svg>
  );
}

export function Donut({
  value, total, size = 64, color = 'var(--primary-500)', label,
}: { value: number; total: number; size?: number; color?: string; label?: ReactNode }) {
  const p = total ? Math.min(1, value / total) : 0;
  const r = (size - 8) / 2;
  const c = 2 * Math.PI * r;
  return (
    <svg viewBox={`0 0 ${size} ${size}`} width={size} height={size}>
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--border)" strokeWidth={6} />
      <circle
        cx={size / 2}
        cy={size / 2}
        r={r}
        fill="none"
        stroke={color}
        strokeWidth={6}
        strokeDasharray={`${p * c} ${c}`}
        strokeLinecap="round"
        transform={`rotate(-90 ${size / 2} ${size / 2})`}
      />
      {label && (
        <text x={size / 2} y={size / 2 + 4} textAnchor="middle" fontSize={14} fontWeight={700} fill="var(--text)">{label}</text>
      )}
    </svg>
  );
}

export function Dialog({
  open, onClose, title, sub, size = '', children, foot,
}: {
  open: boolean;
  onClose?: () => void;
  title: ReactNode;
  sub?: ReactNode;
  size?: '' | 'lg' | 'xl';
  children: ReactNode;
  foot?: ReactNode;
}) {
  if (!open) return null;
  return (
    <div
      className="overlay"
      onMouseDown={e => {
        if (e.target === e.currentTarget) onClose?.();
      }}
    >
      <div className={`dialog ${size}`} role="dialog" aria-modal="true">
        <div className="dialog-head">
          <div style={{ flex: 1 }}>
            <div style={{ fontWeight: 700, fontSize: 15 }}>{title}</div>
            {sub && <div style={{ fontSize: 12, color: 'var(--muted)', marginTop: 2 }}>{sub}</div>}
          </div>
          {onClose && (
            <button className="btn ghost icon sm" onClick={onClose} aria-label="Close">
              <Icons.x />
            </button>
          )}
        </div>
        <div className="dialog-body">{children}</div>
        {foot && <div className="dialog-foot">{foot}</div>}
      </div>
    </div>
  );
}

export function Field({
  label, hint, error, children, span = 1,
}: { label?: ReactNode; hint?: ReactNode; error?: ReactNode; children: ReactNode; span?: number }) {
  return (
    <div className="field" style={{ gridColumn: `span ${span}` }}>
      {label && <label>{label}</label>}
      {children}
      {error && <div style={{ fontSize: 11, color: 'var(--danger)' }}>{error}</div>}
      {hint && !error && <div className="hint">{hint}</div>}
    </div>
  );
}

export function Checkbox({
  checked, onChange, label,
}: { checked?: boolean; onChange?: (checked: boolean) => void; label?: ReactNode }) {
  return (
    <label className="row gap-2" style={{ cursor: 'pointer', userSelect: 'none', fontSize: 'var(--fs-base)' }}>
      <span className="checkbox" data-checked={!!checked} onClick={() => onChange?.(!checked)} />
      {label}
    </label>
  );
}

export function Switch({ on, onChange }: { on?: boolean; onChange?: (on: boolean) => void }) {
  return <span className="switch" data-on={!!on} onClick={() => onChange?.(!on)} role="switch" aria-checked={!!on} />;
}

/** Deterministic avatar color from a string seed. */
export function avatarColor(seed: string) {
  const hue = [...(seed || 'x')].reduce((a, c) => a + c.charCodeAt(0), 0) % 360;
  return `linear-gradient(135deg, oklch(0.62 0.13 ${hue}), oklch(0.50 0.16 ${(hue + 40) % 360}))`;
}

export function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).map(w => w[0]).slice(0, 2).join('').toUpperCase();
}

export function Avatar({ seed, label, size = 26 }: { seed: string; label: string; size?: number }) {
  return (
    <span
      className="avatar"
      style={{ width: size, height: size, fontSize: size * 0.42, background: avatarColor(seed) }}
    >{label}</span>
  );
}

export function IconBtn({
  icon, title, danger, onClick,
}: { icon: IconName; title?: string; danger?: boolean; onClick?: () => void }) {
  const Icon = Icons[icon];
  return (
    <button className={`btn ghost icon sm ${danger ? 'danger' : ''}`} title={title} onClick={onClick}>
      <Icon />
    </button>
  );
}

export function Stat({
  label, value, foot, icon,
}: { label: ReactNode; value: ReactNode; foot?: ReactNode; icon?: IconName }) {
  const Icon = icon ? Icons[icon] : null;
  return (
    <div className="stat">
      <div className="stat-label">
        {Icon && <Icon style={{ width: 13, height: 13 }} />}
        {label}
      </div>
      <div className="stat-value">{value}</div>
      {foot && <div className="stat-foot">{foot}</div>}
    </div>
  );
}

export function Empty({
  icon = 'info', title, sub, action,
}: { icon?: IconName; title: ReactNode; sub?: ReactNode; action?: ReactNode }) {
  const Icon = Icons[icon];
  return (
    <div className="col center" style={{ padding: 36, textAlign: 'center', gap: 8, color: 'var(--muted)' }}>
      <div
        style={{
          width: 40, height: 40, borderRadius: 999,
          background: 'var(--surface-2)', border: '1px solid var(--border)',
          display: 'grid', placeItems: 'center', marginBottom: 4,
        }}
      >
        <Icon style={{ width: 18, height: 18 }} />
      </div>
      <div style={{ fontWeight: 600, color: 'var(--text)' }}>{title}</div>
      {sub && <div>{sub}</div>}
      {action}
    </div>
  );
}

export function bytesFmt(gb: number) {
  return gb >= 1000 ? `${(gb / 1000).toFixed(1)} TB` : `${gb.toFixed(1)} GB`;
}
