namespace HostPanel.Installer.Pages;

/// <summary>
/// Wizard page 9 — live progress via SSE.
/// Renders a checklist of steps + tailing log. Each event from
/// /api/install/events updates the corresponding step's row and appends
/// the message to the log pane. On run_completed, surfaces an
/// "Open HostPanel →" deep link. On run_failed, surfaces the diagnostic
/// bundle path.
/// </summary>
public static class ProgressPage
{
    public static string Render() =>
        HtmlLayout.RenderPage(
            title: "Installing",
            currentStep: 9,
            totalSteps: 9,
            bodyContent: """
<h1>Installing HostPanel</h1>
<p class='sub'>Each step streams its status here. Leave this page open until
the run completes — closing the browser doesn't stop the install (the binary
keeps running) but you lose the live tail.</p>

<div id='banner'></div>

<div class='card'>
  <h2 style='margin-top:0'>Steps</h2>
  <div id='steps'></div>
</div>

<div class='card'>
  <h2 style='margin-top:0; display:flex; justify-content:space-between; align-items:center'>
    Log <span style='font-weight:400; font-size:13px; color:var(--muted)'><label><input type='checkbox' id='autoscroll' checked> auto-scroll</label></span>
  </h2>
  <pre id='log' style='max-height:400px; overflow:auto; background:var(--code-bg); padding:.75rem; border-radius:6px; font-size:12px; line-height:1.4; margin:0; white-space:pre-wrap; word-break:break-word'></pre>
</div>

<div class='actions' id='actions'>
  <span style='color:var(--muted); align-self:center'>Install in progress…</span>
</div>
""",
            extraScript: """
const $ = s => document.querySelector(s);
const STATUS_LABEL = { Pending: '·', Running: '⟳', Completed: '✓', Skipped: '−', Failed: '✗' };
const STATUS_CLASS = { Pending: '', Running: 'status-warn', Completed: 'status-pass', Skipped: '', Failed: 'status-block' };

function el(tag, attrs, ...children) {
  const n = document.createElement(tag);
  if (attrs) for (const [k, v] of Object.entries(attrs)) {
    if (v == null) continue;
    if (k === 'class') n.className = v;
    else if (k === 'onclick') n.addEventListener('click', v);
    else n.setAttribute(k, v);
  }
  for (const c of children) {
    if (c == null) continue;
    n.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
  }
  return n;
}
function clear(p) { while (p.firstChild) p.removeChild(p.firstChild); }

const stepRows = new Map(); // stepId -> {row, status}

function upsertStep(id, label, status) {
  let entry = stepRows.get(id);
  if (!entry) {
    const row = el('div', {class: 'check-row'});
    const badge = el('span', {class: 'status-badge'});
    const lab = el('div', {class: 'check-label'});
    const right = el('span');
    row.appendChild(badge);
    row.appendChild(lab);
    row.appendChild(right);
    $('#steps').appendChild(row);
    entry = { row, badge, lab, right };
    stepRows.set(id, entry);
  }
  entry.lab.textContent = label || id;
  entry.badge.textContent = STATUS_LABEL[status] || '?';
  entry.badge.className = 'status-badge ' + (STATUS_CLASS[status] || '');
  entry.right.textContent = status;
  entry.right.className = STATUS_CLASS[status] || '';
}

function appendLog(line) {
  const log = $('#log');
  log.appendChild(document.createTextNode(line + '\n'));
  if ($('#autoscroll').checked) log.scrollTop = log.scrollHeight;
}

function setBanner(kind, msg, links) {
  const banner = $('#banner');
  clear(banner);
  const div = el('div', {class: 'banner banner-' + kind});
  div.appendChild(el('strong', null, msg));
  if (links) for (const lk of links) {
    div.appendChild(document.createTextNode(' '));
    div.appendChild(el('a', {href: lk.href, class: 'btn primary', style: 'margin-left:.5rem'}, lk.label));
  }
  banner.appendChild(div);
}

function setActions(htmlNodes) {
  const a = $('#actions');
  clear(a);
  for (const n of htmlNodes) a.appendChild(n);
}

const evt = new EventSource('/api/install/events');
evt.onmessage = e => {
  let ev; try { ev = JSON.parse(e.data); } catch { return; }
  if (ev.message) appendLog(ev.message);
  if (ev.stepId && ev.status) upsertStep(ev.stepId, ev.stepLabel, ev.status);
  if (ev.phase === 'run_completed') {
    setBanner('pass', 'Install complete.', [{label: 'Open HostPanel →', href: 'https://localhost:8443'}]);
    setActions([el('a', {class: 'btn', href: '/wizard/confirm'}, 'Back to summary')]);
    evt.close();
  }
  if (ev.phase === 'run_failed') {
    setBanner('block', ev.message || 'Install failed.');
    setActions([
      el('a', {class: 'btn', href: '/wizard/progress'}, '↻ Reload progress'),
      el('a', {class: 'btn primary', href: '/wizard/confirm'}, '← Back to summary'),
    ]);
    evt.close();
  }
};
evt.onerror = e => appendLog('!! SSE error — connection lost. Reload to reconnect.');
""");
}
