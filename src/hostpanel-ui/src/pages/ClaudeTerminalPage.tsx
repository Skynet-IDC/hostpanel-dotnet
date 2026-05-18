import { useEffect, useRef, useState } from 'react';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import '@xterm/xterm/css/xterm.css';
import api from '../api/client';
import { Card, Dialog, Empty, Field, PageHead, Tabs } from '../components/ui/primitives';
import { Icons } from '../components/ui/Icons';
import { panelApi, type PanelSubscription } from '../api/panel';

const KEY_STORAGE = 'hp_claude_api_key';

type Mode = 'chat' | 'terminal';

interface ChatTurn {
  id: number;
  role: 'user' | 'assistant' | 'system';
  text: string;
  ts: number;
}

interface ClaudeContext {
  subscriptionId: number;
  tenantUser: string | null;
  docroot: string | null;
  ready: boolean;
}

export function ClaudeTerminalPage() {
  const [subs, setSubs] = useState<PanelSubscription[]>([]);
  const [subId, setSubId] = useState<number | null>(null);
  const [context, setContext] = useState<ClaudeContext | null>(null);
  const [mode, setMode] = useState<Mode>('chat');
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [apiKey, setApiKey] = useState<string>(() => localStorage.getItem(KEY_STORAGE) ?? '');
  const [keyDialogOpen, setKeyDialogOpen] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const turnIdRef = useRef(0);
  const scrollRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    panelApi.listSubscriptions()
      .then(s => { setSubs(s); if (s.length > 0) setSubId(s[0].id); })
      .catch(e => setErr(e instanceof Error ? e.message : 'Load failed'));
  }, []);

  useEffect(() => {
    if (!subId) { setContext(null); return; }
    api.get<ClaudeContext>(`/claude/context/${subId}`).then(r => setContext(r.data)).catch(() => setContext(null));
  }, [subId]);

  useEffect(() => {
    if (scrollRef.current) scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
  }, [turns]);

  const pushTurn = (role: ChatTurn['role'], text: string) => {
    setTurns(prev => [...prev, { id: ++turnIdRef.current, role, text, ts: Date.now() }]);
  };

  const send = async () => {
    if (!subId || !input.trim() || busy) return;
    if (!apiKey) { setKeyDialogOpen(true); return; }
    const prompt = input.trim();
    setInput('');
    pushTurn('user', prompt);
    setBusy(true);
    setErr(null);
    try {
      const { data } = await api.post<{ exitCode: number; stdout: string; stderr: string }>(
        '/claude/chat',
        { subscriptionId: subId, prompt },
        { headers: { 'X-Anthropic-Api-Key': apiKey } },
      );
      if (data.stdout) pushTurn('assistant', data.stdout);
      if (data.stderr && data.exitCode !== 0) pushTurn('system', `stderr (exit ${data.exitCode}):\n${data.stderr}`);
      else if (data.exitCode !== 0) pushTurn('system', `Process exited with code ${data.exitCode}`);
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e ? (e as { response?: { data?: { message?: string } } }).response?.data?.message : null;
      pushTurn('system', `Error: ${msg ?? (e instanceof Error ? e.message : 'request failed')}`);
    } finally {
      setBusy(false);
    }
  };

  const saveKey = (k: string) => {
    setApiKey(k);
    if (k) localStorage.setItem(KEY_STORAGE, k);
    else localStorage.removeItem(KEY_STORAGE);
    setKeyDialogOpen(false);
  };

  return (
    <>
      <PageHead
        title="Claude Code"
        sub={context?.docroot ? `Sandbox: ${context.docroot}` : 'AI-assisted dev in your tenant'}
        actions={
          <>
            {subs.length > 0 && (
              <select className="select" value={subId ?? ''} onChange={e => setSubId(Number(e.target.value))} style={{ maxWidth: 240 }}>
                {subs.map(s => <option key={s.id} value={s.id}>{s.systemUsername || s.domains?.[0]?.name || `Sub #${s.id}`}</option>)}
              </select>
            )}
            <button className="btn" onClick={() => setKeyDialogOpen(true)}>
              <Icons.key />{apiKey ? 'API key set' : 'Set API key'}
            </button>
          </>
        }
      />

      {err && <div className="alert danger" style={{ marginBottom: 12 }}><Icons.alert />{err}</div>}

      {!subId ? (
        <Card><Empty icon="terminal" title="No subscription" sub="Create a subscription first." /></Card>
      ) : !apiKey ? (
        <Card>
          <Empty
            icon="key"
            title="Bring your own Anthropic API key"
            sub="The panel never stores your key — it stays in your browser and only travels per request. Get one at console.anthropic.com."
            action={<button className="btn primary" style={{ marginTop: 4 }} onClick={() => setKeyDialogOpen(true)}><Icons.key />Set API key</button>}
          />
        </Card>
      ) : !context?.ready ? (
        <Card>
          <Empty icon="warn" title="Sandbox not ready" sub={`Expected docroot ${context?.docroot ?? '(none)'} doesn't exist. Make sure the subscription's main domain has been fully provisioned.`} />
        </Card>
      ) : (
        <>
          <Tabs active={mode} onChange={m => setMode(m as Mode)} tabs={[
            { id: 'chat', label: 'Chat (one-shot)' },
            { id: 'terminal', label: 'Interactive terminal' },
          ]} />

          {mode === 'chat' ? (
            <>
              <Card flush style={{ height: '60vh', display: 'flex', flexDirection: 'column' }}>
                <div
                  ref={scrollRef}
                  style={{ flex: 1, overflowY: 'auto', padding: 16, background: 'var(--bg)', fontFamily: 'var(--font-mono)', fontSize: 12.5 }}
                >
                  {turns.length === 0 ? (
                    <div className="muted" style={{ textAlign: 'center', padding: 24 }}>
                      Ask Claude to help build your site. e.g. "Create a contact page in index.html" or "Add a meta description to my homepage".
                    </div>
                  ) : (
                    turns.map(t => <TurnView key={t.id} turn={t} />)
                  )}
                  {busy && <div className="muted" style={{ marginTop: 8 }}>Claude is thinking…</div>}
                </div>
                <div className="row gap-2" style={{ padding: 12, borderTop: '1px solid var(--border)', background: 'var(--surface)' }}>
                  <input
                    className="input"
                    placeholder="Ask Claude to do something in your site…"
                    value={input}
                    onChange={e => setInput(e.target.value)}
                    onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void send(); } }}
                    disabled={busy}
                    style={{ flex: 1 }}
                  />
                  <button className="btn primary" onClick={() => void send()} disabled={busy || !input.trim()}>
                    <Icons.zap />{busy ? 'Running…' : 'Send'}
                  </button>
                </div>
              </Card>
              <div className="alert" style={{ marginTop: 12 }}>
                <Icons.info />
                <div>One-shot mode runs <span className="mono">claude --print --dangerously-skip-permissions</span> per prompt. For an interactive TUI session use the Terminal tab.</div>
              </div>
            </>
          ) : (
            <InteractiveTerminal subId={subId} apiKey={apiKey} />
          )}
        </>
      )}

      <ApiKeyDialog open={keyDialogOpen} onClose={() => setKeyDialogOpen(false)} initial={apiKey} onSave={saveKey} />
    </>
  );
}

function InteractiveTerminal({ subId, apiKey }: { subId: number; apiKey: string }) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const termRef = useRef<Terminal | null>(null);
  const wsRef = useRef<WebSocket | null>(null);
  const [status, setStatus] = useState<'connecting' | 'open' | 'closed' | 'error'>('connecting');
  const [errMsg, setErrMsg] = useState<string | null>(null);

  useEffect(() => {
    if (!containerRef.current) return;
    const term = new Terminal({
      fontFamily: '"JetBrains Mono", ui-monospace, "SF Mono", monospace',
      fontSize: 13,
      cursorBlink: true,
      scrollback: 5000,
      theme: {
        background: '#1a1d27',
        foreground: '#e6e6e8',
        cursor: '#a78bfa',
        selectionBackground: '#3a3f4d',
      },
    });
    const fit = new FitAddon();
    term.loadAddon(fit);
    term.open(containerRef.current);
    fit.fit();
    termRef.current = term;

    const onResize = () => { try { fit.fit(); } catch { /* swallowed */ } };
    window.addEventListener('resize', onResize);

    const token = localStorage.getItem('hp_token') ?? '';
    const wsScheme = window.location.protocol === 'https:' ? 'wss' : 'ws';
    const wsHost = window.location.host;
    const url = `${wsScheme}://${wsHost}/api/v1/claude/terminal/${subId}?token=${encodeURIComponent(token)}&apiKey=${encodeURIComponent(apiKey)}`;
    const ws = new WebSocket(url);
    ws.binaryType = 'arraybuffer';
    wsRef.current = ws;

    ws.onopen = () => {
      setStatus('open');
      term.writeln('\x1b[2;37m[connected — claude TUI starting…]\x1b[0m');
    };
    ws.onmessage = ev => {
      if (typeof ev.data === 'string') term.write(ev.data);
      else if (ev.data instanceof ArrayBuffer) term.write(new Uint8Array(ev.data));
    };
    ws.onerror = () => {
      setStatus('error');
      setErrMsg('WebSocket error — check that the API has WebSockets enabled and your JWT is valid.');
      term.writeln('\r\n\x1b[1;31m[error]\x1b[0m connection failed');
    };
    ws.onclose = () => {
      setStatus(s => (s === 'error' ? 'error' : 'closed'));
      term.writeln('\r\n\x1b[2;37m[session ended]\x1b[0m');
    };

    term.onData(data => {
      if (ws.readyState === WebSocket.OPEN) ws.send(data);
    });

    return () => {
      window.removeEventListener('resize', onResize);
      try { ws.close(); } catch { /* swallowed */ }
      term.dispose();
    };
  }, [subId, apiKey]);

  return (
    <>
      <Card flush style={{ overflow: 'hidden' }}>
        <div className="row between" style={{ padding: '8px 12px', borderBottom: '1px solid var(--border)', background: 'var(--surface-2)', fontSize: 12 }}>
          <span className="row gap-2">
            <span className={`dot-status ${status === 'open' ? 'up' : status === 'error' ? 'down' : 'idle'}`} />
            <span className="muted">{status === 'open' ? 'Live' : status === 'closed' ? 'Closed' : status === 'error' ? 'Error' : 'Connecting…'}</span>
          </span>
          <span className="muted mono">PTY via script(1) + sudo -u tenant</span>
        </div>
        <div ref={containerRef} style={{ height: '60vh', background: '#1a1d27', padding: 8 }} />
      </Card>
      {errMsg && <div className="alert danger" style={{ marginTop: 12 }}><Icons.alert />{errMsg}</div>}
      <div className="alert" style={{ marginTop: 12 }}>
        <Icons.info />
        <div>
          Interactive terminal runs <span className="mono">sudo -u {'<tenant>'} script -qfc claude /dev/null</span> over WebSocket.
          Your API key is sent in the WS query string from this browser only — never persisted server-side. Session caps at 1 hour.
        </div>
      </div>
    </>
  );
}

function TurnView({ turn }: { turn: ChatTurn }) {
  const color = turn.role === 'user' ? 'var(--primary-400)' : turn.role === 'system' ? 'var(--warn)' : 'var(--text)';
  const label = turn.role === 'user' ? 'you' : turn.role === 'assistant' ? 'claude' : 'system';
  return (
    <div style={{ marginBottom: 12, whiteSpace: 'pre-wrap' }}>
      <div style={{ color, fontWeight: 600, fontSize: 11, textTransform: 'uppercase', letterSpacing: '0.08em', marginBottom: 2 }}>{label}</div>
      <div style={{ color: 'var(--text)' }}>{turn.text}</div>
    </div>
  );
}

function ApiKeyDialog({ open, onClose, initial, onSave }: { open: boolean; onClose: () => void; initial: string; onSave: (k: string) => void }) {
  const [k, setK] = useState(initial);
  useEffect(() => { if (open) setK(initial); }, [open, initial]);
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Anthropic API key"
      sub="Stored only in your browser's localStorage. Sent per-request as a header (chat) or query string (terminal); the server never persists it."
      foot={
        <>
          {initial && <button className="btn ghost" onClick={() => onSave('')}>Remove key</button>}
          <button className="btn ghost" onClick={onClose}>Cancel</button>
          <button className="btn primary" onClick={() => onSave(k.trim())} disabled={!k.trim()}>Save</button>
        </>
      }
    >
      <Field label="API key" hint="Create at console.anthropic.com → API Keys. Format: sk-ant-…">
        <input className="input mono" type="password" autoFocus value={k} onChange={e => setK(e.target.value)} placeholder="sk-ant-…" />
      </Field>
    </Dialog>
  );
}
