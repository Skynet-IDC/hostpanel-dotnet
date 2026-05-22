namespace HostPanel.Installer.Pages;

/// <summary>
/// Stage 2 page — preflight & ports.
/// Renders the system-info report and a port landscape table. Read-only: this
/// page never mutates the box. The "Continue" button is disabled if any check
/// is a hard blocker; otherwise the operator can proceed (Stage 3 form pages).
/// Soft warnings are surfaced inline but don't block.
///
/// XSS safety: the JS uses DOM-API construction (textContent + createElement +
/// appendChild) exclusively. No innerHTML on user-derived strings.
/// </summary>
public static class PreflightPage
{
    public static string Render() =>
        HtmlLayout.RenderPage(
            title: "Preflight",
            currentStep: 2,
            totalSteps: 9,
            bodyContent: """
<h1>Preflight checks</h1>
<p class='sub'>Read-only inspection of this box. Nothing is changed yet — anything that
would mutate the system is surfaced as a prompt on later pages.</p>

<div id='loading' class='card'>
  <span class='spinner'></span> Gathering system info…
</div>

<div id='report' style='display:none'>
  <div id='banner-area'></div>

  <h2>System</h2>
  <div class='card'>
    <dl class='meta'>
      <dt>OS:</dt>       <dd id='os'>—</dd>
      <dt>Memory:</dt>   <dd id='mem'>—</dd>
      <dt>Swap:</dt>     <dd id='swap'>—</dd>
      <dt>Disk:</dt>     <dd id='disk'>—</dd>
    </dl>
  </div>

  <h2>Checks</h2>
  <div id='checks' class='card'></div>

  <h2>Network interfaces</h2>
  <div class='card'>
    <table id='ifs-table'><thead>
      <tr><th>Interface</th><th>IPv4</th><th>MAC</th></tr>
    </thead><tbody></tbody></table>
  </div>

  <h2>Listening ports (watched)</h2>
  <p class='sub' style='font-size: 13px;'>Other ports may be listening too — the installer only watches the ones it needs.</p>
  <div class='card'>
    <table id='ports-table'><thead>
      <tr><th>Port</th><th>Bound to</th><th>Process</th><th>PID</th></tr>
    </thead><tbody></tbody></table>
  </div>

  <div class='actions'>
    <button class='btn' onclick='location.href="/welcome"'>← Back</button>
    <button class='btn' onclick='refresh()'>↻ Re-scan</button>
    <button id='continue' class='btn primary' onclick='location.href = nextHref'>Continue →</button>
  </div>
</div>
""",
            extraScript: """
const $ = s => document.querySelector(s);
const STATUS_LABEL = { Pass: '✓', Warn: '⚠', Block: '✗' };
const STATUS_CLASS = { Pass: 'status-pass', Warn: 'status-warn', Block: 'status-block' };
// Filled from the wizard-state endpoint so primary skips straight to /wizard/database
// and standby skips straight to /wizard/network.
let nextHref = '/wizard/database';

// Small DOM-construction helper: el('tag', {class,id,style,title,onclick}, ...children-or-text-or-null)
function el(tag, attrs, ...children) {
  const node = document.createElement(tag);
  if (attrs) for (const [k, v] of Object.entries(attrs)) {
    if (v == null) continue;
    if (k === 'class') node.className = v;
    else if (k === 'onclick') node.addEventListener('click', v);
    else if (k === 'disabled') node.disabled = !!v;
    else node.setAttribute(k, v);
  }
  for (const c of children) {
    if (c == null) continue;
    node.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
  }
  return node;
}

function clear(parent) { while (parent.firstChild) parent.removeChild(parent.firstChild); }

async function refresh() {
  $('#loading').style.display = 'block';
  $('#report').style.display = 'none';
  try {
    const r = await fetch('/api/preflight');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    const d = await r.json();
    render(d);
  } catch (e) {
    const loading = $('#loading');
    clear(loading);
    loading.appendChild(el('div', {class: 'banner banner-block'}, 'Preflight failed: ' + e.message));
  }
}

function render(d) {
  $('#loading').style.display = 'none';
  $('#report').style.display = 'block';

  // Top banner
  const ba = $('#banner-area');
  clear(ba);
  if (d.hasAnyBlockers) {
    ba.appendChild(el('div', {class: 'banner banner-block'},
      el('strong', null, 'Cannot continue'),
      ' — at least one blocker present. Resolve below and re-scan.'));
  } else if (d.hasAnyWarnings) {
    ba.appendChild(el('div', {class: 'banner banner-warn'},
      el('strong', null, 'Warnings present'),
      ' — install can proceed. Review below.'));
  } else {
    ba.appendChild(el('div', {class: 'banner banner-pass'},
      el('strong', null, 'All checks passed.'),
      ' This box is ready for HostPanel.'));
  }

  // System summary
  $('#os').textContent = d.os.prettyName;
  const memGb = Math.round(d.memory.totalKb / 1024 / 1024 * 10) / 10;
  const availGb = Math.round(d.memory.availableKb / 1024 / 1024 * 10) / 10;
  $('#mem').textContent = `${memGb} GB total, ${availGb} GB available`;
  const swapMb = Math.round(d.memory.swapTotalKb / 1024);
  $('#swap').textContent = swapMb > 0 ? `${swapMb} MB` : 'none configured';
  $('#disk').textContent = d.disks.map(x => `${x.mount} ${x.freeGb}/${x.totalGb} GB free`).join(' · ') || '—';

  // Checks
  const checksDiv = $('#checks');
  clear(checksDiv);
  for (const c of d.checks) {
    const row = el('div', {class: 'check-row'});
    row.appendChild(el('span', {class: 'status-badge ' + STATUS_CLASS[c.status]}, STATUS_LABEL[c.status]));
    const body = el('div', null);
    body.appendChild(el('div', {class: 'check-label'}, c.label));
    if (c.detail) body.appendChild(el('div', {class: 'check-detail'}, c.detail));
    if (c.suggestion) body.appendChild(el('div', {class: 'check-suggestion'}, c.suggestion));
    row.appendChild(body);
    row.appendChild(el('span', {class: STATUS_CLASS[c.status]}, c.status.toUpperCase()));
    checksDiv.appendChild(row);
  }

  // Interfaces
  const ifsBody = $('#ifs-table tbody');
  clear(ifsBody);
  for (const i of d.interfaces) {
    const tr = el('tr');
    const nameCell = el('td');
    nameCell.appendChild(el('code', null, i.name));
    if (i.isLoopback) {
      nameCell.appendChild(document.createTextNode(' '));
      nameCell.appendChild(el('span', {style: 'color:var(--muted)'}, '(loopback)'));
    }
    tr.appendChild(nameCell);
    tr.appendChild(el('td', null, i.ipv4.length ? i.ipv4.join(', ') : ''));
    if (i.ipv4.length === 0) {
      tr.lastChild.appendChild(el('span', {style: 'color:var(--muted)'}, 'none'));
    }
    const macCell = el('td');
    macCell.appendChild(el('code', null, i.macAddress || ''));
    tr.appendChild(macCell);
    ifsBody.appendChild(tr);
  }

  // Ports
  const portsBody = $('#ports-table tbody');
  clear(portsBody);
  if (d.ports.length === 0) {
    const tr = el('tr');
    tr.appendChild(el('td', {colspan: '4', style: 'color:var(--muted);text-align:center'}, 'None of the watched ports are listening.'));
    portsBody.appendChild(tr);
  } else {
    for (const p of d.ports) {
      const tr = el('tr');
      const portCell = el('td'); portCell.appendChild(el('code', null, String(p.port)));
      const addrCell = el('td'); addrCell.appendChild(el('code', null, p.address));
      tr.appendChild(portCell);
      tr.appendChild(addrCell);
      tr.appendChild(el('td', null, p.processName || '<unknown>'));
      tr.appendChild(el('td', null, p.pid != null ? String(p.pid) : '—'));
      portsBody.appendChild(tr);
    }
  }

  // Continue gate + dynamic next-href from wizard state
  const cont = $('#continue');
  cont.disabled = d.hasAnyBlockers;
  cont.title = d.hasAnyBlockers ? 'Resolve blockers before continuing' : '';
}

// Fetch wizard state once so the Continue button knows whether to go to /database (primary) or /network (standby).
fetch('/api/wizard/next?from=preflight').then(r => r.json()).then(j => { nextHref = j.next; }).catch(() => {});

refresh();
""");
}
