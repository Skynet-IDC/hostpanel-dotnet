import type { SVGProps } from 'react';
import type { ReactNode } from 'react';

/** Single-stroke 16px icon set ported from the Helm design bundle. */

type IconProps = SVGProps<SVGSVGElement> & { size?: number };

function wrap(content: ReactNode, props: IconProps) {
  const { size, ...rest } = props;
  return (
    <svg
      viewBox="0 0 16 16"
      width={size ?? 16}
      height={size ?? 16}
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      strokeLinecap="round"
      strokeLinejoin="round"
      {...rest}
    >
      {content}
    </svg>
  );
}

const Dashboard = (p: IconProps) => wrap(
  <>
    <rect x="2" y="2" width="5.5" height="5.5" rx="1.2" />
    <rect x="8.5" y="2" width="5.5" height="3" rx="1.2" />
    <rect x="2" y="8.5" width="5.5" height="5.5" rx="1.2" />
    <rect x="8.5" y="6" width="5.5" height="8" rx="1.2" />
  </>, p);

const Users = (p: IconProps) => wrap(
  <>
    <circle cx="6" cy="5.5" r="2.5" />
    <path d="M1.5 13.5c.6-2.2 2.4-3.5 4.5-3.5s3.9 1.3 4.5 3.5" />
    <circle cx="11.5" cy="6" r="2" />
    <path d="M11.5 10c1.5 0 2.8 1 3 2.5" />
  </>, p);

const User = (p: IconProps) => wrap(
  <>
    <circle cx="8" cy="5.5" r="2.5" />
    <path d="M3 13.5c.6-2.2 2.5-3.5 5-3.5s4.4 1.3 5 3.5" />
  </>, p);

const Package = (p: IconProps) => wrap(
  <>
    <path d="M2 4.5 8 2l6 2.5v7L8 14 2 11.5z" />
    <path d="M2 4.5 8 7l6-2.5M8 7v7" />
  </>, p);

const Layers = (p: IconProps) => wrap(
  <>
    <path d="M8 1.5 1.5 5 8 8.5 14.5 5z" />
    <path d="M2 8 8 11l6-3M2 11l6 3 6-3" />
  </>, p);

const Shield = (p: IconProps) => wrap(
  <path d="M8 1.5 2.5 3.5v4c0 3.2 2.4 5.6 5.5 7 3.1-1.4 5.5-3.8 5.5-7v-4z" />, p);

const ShieldCheck = (p: IconProps) => wrap(
  <>
    <path d="M8 1.5 2.5 3.5v4c0 3.2 2.4 5.6 5.5 7 3.1-1.4 5.5-3.8 5.5-7v-4z" />
    <path d="m5.5 7.5 2 2 3-3.5" />
  </>, p);

const Clock = (p: IconProps) => wrap(
  <>
    <circle cx="8" cy="8" r="6" />
    <path d="M8 5v3.2l2 1.5" />
  </>, p);

const Globe = (p: IconProps) => wrap(
  <>
    <circle cx="8" cy="8" r="6" />
    <path d="M2 8h12M8 2c1.7 2 2.5 4 2.5 6S9.7 14 8 14M8 2c-1.7 2-2.5 4-2.5 6S6.3 14 8 14" />
  </>, p);

const Folder = (p: IconProps) => wrap(
  <path d="M2 4.5c0-.6.4-1 1-1h3.5l1.5 1.5H13c.6 0 1 .4 1 1V12c0 .6-.4 1-1 1H3c-.6 0-1-.4-1-1z" />, p);

const FolderOpen = (p: IconProps) => wrap(
  <>
    <path d="M2 4.5c0-.6.4-1 1-1h3.5l1.5 1.5H13c.6 0 1 .4 1 1V7H2z" />
    <path d="m2 7 1.5 5h10L15 7z" />
  </>, p);

const File = (p: IconProps) => wrap(
  <>
    <path d="M3 1.5h6L13 5.5V14c0 .3-.2.5-.5.5H3.5c-.3 0-.5-.2-.5-.5V2c0-.3.2-.5.5-.5z" />
    <path d="M9 1.5V5.5h4" />
  </>, p);

const Upload = (p: IconProps) => wrap(
  <>
    <path d="M8 2v8M5 5l3-3 3 3" />
    <path d="M2.5 11v1.5c0 .6.4 1 1 1h9c.6 0 1-.4 1-1V11" />
  </>, p);

const Download = (p: IconProps) => wrap(
  <>
    <path d="M8 2v8m-3-3 3 3 3-3" />
    <path d="M2.5 12.5v.5c0 .3.2.5.5.5h10c.3 0 .5-.2.5-.5v-.5" />
  </>, p);

const Mail = (p: IconProps) => wrap(
  <>
    <rect x="1.5" y="3" width="13" height="10" rx="1.5" />
    <path d="m2 4.5 6 4.5 6-4.5" />
  </>, p);

const Database = (p: IconProps) => wrap(
  <>
    <ellipse cx="8" cy="3.5" rx="5.5" ry="2" />
    <path d="M2.5 3.5v9c0 1.1 2.5 2 5.5 2s5.5-.9 5.5-2v-9" />
    <path d="M2.5 8c0 1.1 2.5 2 5.5 2s5.5-.9 5.5-2" />
  </>, p);

const Lock = (p: IconProps) => wrap(
  <>
    <rect x="2.5" y="7" width="11" height="7" rx="1.5" />
    <path d="M5 7V4.5a3 3 0 0 1 6 0V7" />
  </>, p);

const LockOpen = (p: IconProps) => wrap(
  <>
    <rect x="2.5" y="7" width="11" height="7" rx="1.5" />
    <path d="M5 7V4.5a3 3 0 0 1 5.6-1.4" />
  </>, p);

const Dns = (p: IconProps) => wrap(
  <path d="M8 1.5v13M1.5 4h13M1.5 8h13M1.5 12h13" />, p);

const Archive = (p: IconProps) => wrap(
  <>
    <rect x="1.5" y="2.5" width="13" height="3" rx="1" />
    <path d="M2.5 5.5v7c0 .6.4 1 1 1h9c.6 0 1-.4 1-1v-7" />
    <path d="M6 8h4" />
  </>, p);

const Wordpress = (p: IconProps) => wrap(
  <>
    <circle cx="8" cy="8" r="6.3" />
    <path d="M2.2 8h11.6M4 4l8 8M12 4 4 12" />
  </>, p);

const Docker = (p: IconProps) => wrap(
  <>
    <rect x="2" y="7" width="2" height="2" />
    <rect x="4.5" y="7" width="2" height="2" />
    <rect x="7" y="7" width="2" height="2" />
    <rect x="9.5" y="7" width="2" height="2" />
    <rect x="4.5" y="4.5" width="2" height="2" />
    <rect x="7" y="4.5" width="2" height="2" />
    <path d="M1.5 9.5h11c1 0 2-1 2-2 0 0-1 0-1.5.3-.2-.7-1-1.3-1.8-1.3-.2 1-.2 2 .3 2.7" />
    <path d="M3 12c2 .5 8.5.7 11-2.5" />
  </>, p);

const Git = (p: IconProps) => wrap(
  <>
    <circle cx="4" cy="4" r="1.5" />
    <circle cx="4" cy="12" r="1.5" />
    <circle cx="12" cy="8" r="1.5" />
    <path d="M4 5.5v5M5.5 4h2.5c1.5 0 2.5 1 2.5 2.5" />
  </>, p);

const Webhook = (p: IconProps) => wrap(
  <>
    <path d="M5 8.5 6.5 11a2.5 2.5 0 1 1-2.6-3.6" />
    <path d="M11 11h-3a2.5 2.5 0 1 1 .5-3.7" />
    <path d="m9.5 4.5 1.5 2.5a2.5 2.5 0 1 1-3.5 1" />
  </>, p);

const List = (p: IconProps) => wrap(
  <>
    <path d="M5 4h9M5 8h9M5 12h9" />
    <circle cx="2.5" cy="4" r=".7" fill="currentColor" />
    <circle cx="2.5" cy="8" r=".7" fill="currentColor" />
    <circle cx="2.5" cy="12" r=".7" fill="currentColor" />
  </>, p);

const Settings = (p: IconProps) => wrap(
  <>
    <circle cx="8" cy="8" r="2" />
    <path d="M8 1.5v1.8M8 12.7v1.8M14.5 8h-1.8M3.3 8H1.5M12.6 3.4l-1.3 1.3M4.7 11.3l-1.3 1.3M12.6 12.6l-1.3-1.3M4.7 4.7 3.4 3.4" />
  </>, p);

const Brush = (p: IconProps) => wrap(
  <>
    <path d="M2.5 11c0 1.5 1 2.5 2.5 2.5S7 12.5 7 11c0-.7-.5-1.3-1-1.5" />
    <path d="M7 9.5 13 3a1.5 1.5 0 0 0-2-2L4.5 7.5" />
  </>, p);

const Cpu = (p: IconProps) => wrap(
  <>
    <rect x="3" y="3" width="10" height="10" rx="1.5" />
    <rect x="6" y="6" width="4" height="4" />
    <path d="M6 1.5v1.5M10 1.5v1.5M6 13v1.5M10 13v1.5M1.5 6H3M1.5 10H3M13 6h1.5M13 10h1.5" />
  </>, p);

const Zap = (p: IconProps) => wrap(
  <path d="M9 1.5 3 9h4l-1 5.5 6-7.5H8z" />, p);

const Search = (p: IconProps) => wrap(
  <>
    <circle cx="7" cy="7" r="4.5" />
    <path d="m10.5 10.5 3 3" />
  </>, p);

const Plus = (p: IconProps) => wrap(<path d="M8 3v10M3 8h10" />, p);
const Minus = (p: IconProps) => wrap(<path d="M3 8h10" />, p);
const X = (p: IconProps) => wrap(<path d="m4 4 8 8M12 4l-8 8" />, p);
const Check = (p: IconProps) => wrap(<path d="m3 8 3.5 3.5L13 5" />, p);
const ChevR = (p: IconProps) => wrap(<path d="m6 3 5 5-5 5" />, p);
const ChevL = (p: IconProps) => wrap(<path d="m10 3-5 5 5 5" />, p);
const ChevD = (p: IconProps) => wrap(<path d="m3 6 5 5 5-5" />, p);
const ChevU = (p: IconProps) => wrap(<path d="m3 10 5-5 5 5" />, p);

const More = (p: IconProps) => wrap(
  <>
    <circle cx="3.5" cy="8" r="1" fill="currentColor" />
    <circle cx="8" cy="8" r="1" fill="currentColor" />
    <circle cx="12.5" cy="8" r="1" fill="currentColor" />
  </>, p);

const Filter = (p: IconProps) => wrap(
  <path d="M2 3h12L9.5 9v4l-3-1.5V9z" />, p);

const Sort = (p: IconProps) => wrap(
  <path d="M4 3v10M2 11l2 2 2-2M12 13V3M14 5l-2-2-2 2" />, p);

const Edit = (p: IconProps) => wrap(
  <path d="M11 2 14 5 5.5 13.5H2.5V10.5z" />, p);

const Trash = (p: IconProps) => wrap(
  <path d="M2.5 4h11M6 4V2.5h4V4M4 4v9.5c0 .3.2.5.5.5h7c.3 0 .5-.2.5-.5V4M6.5 6.5v5M9.5 6.5v5" />, p);

const Copy = (p: IconProps) => wrap(
  <>
    <rect x="5" y="5" width="9" height="9" rx="1" />
    <path d="M2 2h7v3M2 2v7h3" />
  </>, p);

const External = (p: IconProps) => wrap(
  <>
    <path d="M9 2h5v5M14 2 7.5 8.5" />
    <path d="M12 9.5V13c0 .3-.2.5-.5.5H3c-.3 0-.5-.2-.5-.5V4.5c0-.3.2-.5.5-.5h3.5" />
  </>, p);

const Refresh = (p: IconProps) => wrap(
  <>
    <path d="M2.5 8a5.5 5.5 0 0 1 9.5-3.5L14 6" />
    <path d="M14 2v4h-4M13.5 8a5.5 5.5 0 0 1-9.5 3.5L2 10" />
    <path d="M2 14v-4h4" />
  </>, p);

const Play = (p: IconProps) => wrap(<path d="M4 3v10l9-5z" fill="currentColor" />, p);
const Pause = (p: IconProps) => wrap(
  <>
    <rect x="4" y="3" width="3" height="10" fill="currentColor" />
    <rect x="9" y="3" width="3" height="10" fill="currentColor" />
  </>, p);
const Stop = (p: IconProps) => wrap(<rect x="3.5" y="3.5" width="9" height="9" rx="1" fill="currentColor" />, p);

const Warn = (p: IconProps) => wrap(
  <>
    <path d="M8 2 14 13H2z" />
    <path d="M8 6v3.5M8 11.5v.1" />
  </>, p);

const Info = (p: IconProps) => wrap(
  <>
    <circle cx="8" cy="8" r="6" />
    <path d="M8 7v4M8 5.5v.1" />
  </>, p);

const Check2 = (p: IconProps) => wrap(
  <>
    <circle cx="8" cy="8" r="6" />
    <path d="m5.5 8 2 2 3-3.5" />
  </>, p);

const Alert = (p: IconProps) => wrap(
  <>
    <circle cx="8" cy="8" r="6" />
    <path d="M8 5v4M8 11v.1" />
  </>, p);

const Bell = (p: IconProps) => wrap(
  <>
    <path d="M3.5 11h9l-1-1.5V7a3.5 3.5 0 1 0-7 0v2.5z" />
    <path d="M6.5 13a1.5 1.5 0 0 0 3 0" />
  </>, p);

const PanelLeft = (p: IconProps) => wrap(
  <>
    <rect x="1.5" y="2.5" width="13" height="11" rx="1.5" />
    <path d="M6 2.5v11" />
  </>, p);

const Terminal = (p: IconProps) => wrap(
  <>
    <rect x="1.5" y="2.5" width="13" height="11" rx="1.5" />
    <path d="m4 6 2 2-2 2M8 11h3" />
  </>, p);

const Key = (p: IconProps) => wrap(
  <>
    <circle cx="5.5" cy="10.5" r="2.5" />
    <path d="m7.5 8.5 6-6M11 5l1.5 1.5M12.5 3.5 14 5" />
  </>, p);

const Cloud = (p: IconProps) => wrap(
  <path d="M4.5 12C2.6 12 1 10.5 1 8.6 1 7 2.2 5.6 3.7 5.3 4.2 3.4 6 2 8 2c2.4 0 4.4 1.9 4.5 4.3C13.9 6.6 15 7.8 15 9.3c0 1.5-1.2 2.7-2.7 2.7z" />, p);

const History = (p: IconProps) => wrap(
  <>
    <path d="M2 6a6 6 0 1 1 1 5" />
    <path d="M2 2v4h4" />
    <path d="M8 5v3.2l2 1.5" />
  </>, p);

const Trending = (p: IconProps) => wrap(
  <>
    <path d="m1.5 11 4-4 2.5 2.5L14 4" />
    <path d="M10 4h4v4" />
  </>, p);

const Link = (p: IconProps) => wrap(
  <>
    <path d="M6.5 9.5a2.5 2.5 0 0 0 3.5 0l2-2a2.5 2.5 0 0 0-3.5-3.5l-1 1" />
    <path d="M9.5 6.5a2.5 2.5 0 0 0-3.5 0l-2 2a2.5 2.5 0 0 0 3.5 3.5l1-1" />
  </>, p);

const Bug = (p: IconProps) => wrap(
  <>
    <rect x="4" y="5" width="8" height="8" rx="3" />
    <path d="M2 7h2M12 7h2M2 11h2M12 11h2M8 5V3.5M5.5 5l-1-1M10.5 5l1-1" />
  </>, p);

const Star = (p: IconProps) => wrap(
  <path d="M8 1.5 9.7 5.6l4.4.3-3.3 2.9 1 4.3L8 11l-3.8 2.1 1-4.3L1.9 5.9l4.4-.3z" />, p);

const Network = (p: IconProps) => wrap(
  <>
    <circle cx="8" cy="3" r="1.5" />
    <circle cx="3" cy="12" r="1.5" />
    <circle cx="13" cy="12" r="1.5" />
    <path d="m8 4.5-5 6M8 4.5l5 6M4.5 12h7" />
  </>, p);

const Graph = (p: IconProps) => wrap(
  <>
    <path d="M2 14V2M2 14h12" />
    <path d="M5 11V6.5M8 11V4M11 11V8" />
  </>, p);

const Log = (p: IconProps) => wrap(
  <>
    <rect x="2.5" y="1.5" width="11" height="13" rx="1.5" />
    <path d="M5 5h6M5 8h6M5 11h4" />
  </>, p);

const Menu = (p: IconProps) => wrap(
  <path d="M2 4h12M2 8h12M2 12h12" />, p);

export const Icons = {
  dashboard: Dashboard, users: Users, user: User, package: Package, layers: Layers,
  shield: Shield, shieldCheck: ShieldCheck, clock: Clock, globe: Globe,
  folder: Folder, folderOpen: FolderOpen, file: File, upload: Upload, download: Download,
  mail: Mail, database: Database, lock: Lock, lockOpen: LockOpen, dns: Dns,
  archive: Archive, wordpress: Wordpress, docker: Docker, git: Git, webhook: Webhook,
  list: List, settings: Settings, brush: Brush, cpu: Cpu, zap: Zap, search: Search,
  plus: Plus, minus: Minus, x: X, check: Check, chevR: ChevR, chevL: ChevL, chevD: ChevD, chevU: ChevU,
  more: More, filter: Filter, sort: Sort, edit: Edit, trash: Trash, copy: Copy,
  external: External, refresh: Refresh, play: Play, pause: Pause, stop: Stop,
  warn: Warn, info: Info, check2: Check2, alert: Alert, bell: Bell,
  panelLeft: PanelLeft, terminal: Terminal, key: Key, cloud: Cloud,
  history: History, trending: Trending, link: Link, bug: Bug, star: Star,
  network: Network, graph: Graph, log: Log, menu: Menu,
} as const;

export type IconName = keyof typeof Icons;
