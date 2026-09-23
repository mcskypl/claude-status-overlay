// Harness developerski: uruchamia mostek, serwuje `ui/` i tlumaczy protokol
// w obie strony. Zastepuje hosta .NET na czas, gdy go jeszcze nie ma -
// pozwala sprawdzic kontrakt UI <-> mostek zanim zamrozi sie go w C#.
//
//   node dev-server.mjs            -> http://127.0.0.1:8723
//   node dev-server.mjs --cwd D:\repos\cos
//
// To NIE jest czesc produktu. Docelowo te sama role gra AssistantEngine
// po stronie C#, a UI i mostek zostaja bez zmian.

import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { homedir } from 'node:os';
import { join, extname, basename, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = fileURLToPath(new URL('.', import.meta.url));
const UI_DIR = join(HERE, '..', 'src', 'ClaudeStatus.Overlay', 'Assistant', 'ui');
const PORT = 8723;

function arg(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i !== -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

const CWD = arg('cwd', join(homedir(), '.claude', 'assistant'));

// ------------------------------------------------------------- kanal do UI

/** @type {Set<import('node:http').ServerResponse>} */
const clients = new Set();

function toUI(msg) {
  const line = `data: ${JSON.stringify(msg)}\n\n`;
  for (const res of clients) res.write(line);
}

// ---------------------------------------------------------------- mostek

const bridge = spawn(process.execPath, [join(HERE, 'index.mjs'), '--cwd', CWD], {
  stdio: ['pipe', 'pipe', 'inherit'],
});

function toBridge(msg) {
  bridge.stdin.write(JSON.stringify(msg) + '\n');
}

bridge.on('exit', (code) => {
  console.log(`\n[harness] mostek zakonczyl sie kodem ${code}`);
  toUI({ type: 'engineError', error: {
    title: 'Mostek przestal odpowiadac',
    text: 'Proces silnika zakonczyl sie. Uruchom harness ponownie.',
    diagnostic: `exit=${code} · ${new Date().toISOString()}`,
  }});
});

// ------------------------------------------------------- tlumaczenie nazw

const SERVERS = {
  clockify: 'Clockify',
  hubspot: 'HubSpot',
  'azure-devops': 'Azure DevOps',
  'optimes-monitor': 'Optimes Monitor',
  sqlserver: 'SQL Server',
};

function toolLabel(name) {
  if (name.startsWith('mcp__')) {
    const parts = name.split('__');
    const server = SERVERS[parts[1]] || parts[1];
    return `${server} · ${parts.slice(2).join('__')}`;
  }
  return name;
}

// --------------------------------------------------------- stan jednej tury

const turn = { text: '', tools: new Map(), started: 0 };

function resetTurn() {
  turn.text = '';
  turn.tools.clear();
  turn.started = Date.now();
}

function seconds(ms) {
  return (ms / 1000).toFixed(1).replace('.', ',') + ' s';
}

// ------------------------------------------------- mostek -> UI (tlumaczenie)

createInterface({ input: bridge.stdout }).on('line', (line) => {
  let m;
  try { m = JSON.parse(line); } catch { return; }

  switch (m.t) {
    case 'ready':
      console.log(`[harness] sesja ${m.session}`);
      console.log(`[harness] MCP: ${m.mcp.filter((s) => s.status === 'connected').map((s) => s.name).join(', ')}`);
      break;

    case 'delta':
      toUI({ type: 'delta', text: m.text });
      break;

    case 'text':
      turn.text += (turn.text ? '\n\n' : '') + m.text;
      toUI({ type: 'text', text: turn.text });
      break;

    case 'tool':
      turn.tools.set(m.id, { name: m.name, input: m.input, at: Date.now() });
      toUI({
        type: 'toolStart',
        id: m.id,
        label: `${toolLabel(m.name)}…`,
      });
      break;

    case 'tool_result': {
      const t = turn.tools.get(m.id);
      const label = t ? toolLabel(t.name) : m.id;
      if (m.error) {
        toUI({ type: 'toolError', id: m.id, label, reason: (m.text || '').slice(0, 400) });
      } else {
        toUI({
          type: 'toolDone',
          id: m.id,
          label,
          metric: t ? seconds(Date.now() - t.at) : '',
          input: t ? JSON.stringify(t.input, null, 2) : '',
          result: m.text || '',
        });
      }
      break;
    }

    case 'permission':
      toUI({ type: 'consent', consent: {
        id: m.id,
        title: m.title || toolLabel(m.tool),
        effect: describe(m),
        facts: facts(m),
      }});
      break;

    case 'done':
      toUI({ type: 'done' });
      if (m.error) {
        toUI({ type: 'engineError', error: {
          title: 'Tura zakonczyla sie bledem',
          text: 'Silnik przerwal ture. Tresc pytania jest zachowana.',
          diagnostic: `${m.error} · ${new Date().toLocaleString('pl-PL')}`,
        }});
      }
      break;

    case 'error':
      toUI({ type: 'engineError', error: {
        title: 'Blad silnika',
        text: m.message,
        diagnostic: new Date().toLocaleString('pl-PL'),
      }});
      break;
  }
});

function describe(m) {
  if (m.server) return `Operacja pojdzie przez serwer \`${m.server}\` w katalogu \`${CWD}\`.`;
  return `Narzedzie \`${m.tool}\` zadziala w katalogu \`${CWD}\`.`;
}

function facts(m) {
  const out = [{ label: 'Narzędzie', value: m.tool }, { label: 'Katalog', value: CWD }];
  if (m.server) out.push({ label: 'Serwer', value: `${m.server} (${m.source || '?'})` });
  if (m.reason) out.push({ label: 'Powód', value: m.reason });
  const arg = firstArg(m.input);
  if (arg) out.push({ label: 'Argument', value: arg });
  return out;
}

function firstArg(input) {
  if (!input || typeof input !== 'object') return '';
  for (const [k, v] of Object.entries(input)) {
    if (typeof v === 'string' && v.length) return `${k}: ${v.slice(0, 120)}`;
  }
  return '';
}

// ------------------------------------------------------------- UI -> mostek

function fromUI(msg) {
  switch (msg.type) {
    case 'ready':
      sendInit();
      break;

    case 'prompt':
      resetTurn();
      toUI({ type: 'turnStart' });
      toBridge({ t: 'prompt', text: msg.text });
      break;

    case 'interrupt':
      toBridge({ t: 'interrupt' });
      break;

    case 'consent':
      toBridge({
        t: 'permission',
        id: msg.id,
        allow: msg.decision !== 'deny',
        always: msg.decision === 'always',
      });
      break;

    default:
      console.log('[harness] UI:', msg.type, msg.text ? `"${msg.text}"` : '');
  }
}

// ------------------------------------------------- prawdziwe sesje i limity

const STATE_MAP = { working: 'pracuje', done: 'gotowe', attention: 'czeka', error: 'blad', idle: 'bezczynny' };

function age(ts) {
  const s = Math.max(0, (Date.now() - new Date(ts).getTime()) / 1000);
  if (s < 60) return Math.round(s) + 's';
  if (s < 3600) return Math.round(s / 60) + 'm';
  return Math.round(s / 3600) + 'h';
}

function readSessions() {
  const dir = join(homedir(), '.claude', 'status');
  if (!existsSync(dir)) return [];
  const out = [];
  for (const f of readdirSync(dir)) {
    if (!f.endsWith('.json')) continue;
    try {
      const d = JSON.parse(readFileSync(join(dir, f), 'utf8'));
      out.push({
        id: d.session_id,
        name: d.project || basename(d.cwd || ''),
        state: STATE_MAP[d.state] || 'bezczynny',
        age: age(d.ts),
        path: d.cwd,
        ts: d.ts,
      });
    } catch { /* plik w trakcie zapisu - pominiecie jest tansze niz blokada */ }
  }
  return out.sort((a, b) => new Date(b.ts) - new Date(a.ts)).slice(0, 12);
}

const DAYS = ['ndz', 'pon', 'wt', 'śr', 'czw', 'pt', 'sob'];

function resetLabel(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  const sameDay = d.toDateString() === new Date().toDateString();
  return sameDay ? `reset ${hh}:${mm}` : `reset ${DAYS[d.getDay()]} ${hh}:${mm}`;
}

function window_(w, spanMs) {
  if (!w) return null;
  const resets = w.resets_at ? new Date(w.resets_at).getTime() : 0;
  const elapsed = resets ? Math.max(0, Math.min(100, (1 - (resets - Date.now()) / spanMs) * 100)) : null;
  return { usedPct: w.utilization ?? 0, elapsedPct: elapsed, reset: resetLabel(w.resets_at) };
}

function readUsage() {
  try {
    const j = JSON.parse(readFileSync(join(homedir(), '.claude.json'), 'utf8'));
    const u = j.cachedUsageUtilization?.utilization;
    const fetched = j.cachedUsageUtilization?.fetchedAtMs;
    return {
      limits: u ? {
        window5h: window_(u.five_hour, 5 * 3600e3),
        window7d: window_(u.seven_day, 7 * 24 * 3600e3),
      } : null,
      account: {
        email: j.oauthAccount?.emailAddress || '',
        refreshedAgo: fetched ? `odświeżono ${age(new Date(fetched).toISOString())} temu` : '',
      },
    };
  } catch {
    return { limits: null, account: { email: '', refreshedAgo: '' } };
  }
}

function sendInit() {
  const u = readUsage();
  toUI({
    type: 'init',
    anchor: 'top',
    title: 'Nowa rozmowa',
    context: { name: basename(CWD), path: CWD, available: existsSync(CWD), recent: recentRepos() },
    sessions: readSessions(),
    limits: u.limits,
    account: u.account,
  });
  toUI({ type: 'show', visible: true });
}

function recentRepos() {
  const roots = ['D:\\repos', join(homedir(), '.claude')];
  const out = [{ name: basename(CWD), path: CWD, when: 'teraz' }];
  for (const root of roots) {
    if (!existsSync(root)) continue;
    try {
      for (const d of readdirSync(root, { withFileTypes: true })) {
        if (!d.isDirectory() || d.name.startsWith('.')) continue;
        const p = join(root, d.name);
        if (p !== CWD) out.push({ name: d.name, path: p, when: '' });
        if (out.length >= 12) return out;
      }
    } catch { /* brak dostepu do katalogu nie jest bledem harnessu */ }
  }
  return out;
}

setInterval(() => {
  if (!clients.size) return;
  const u = readUsage();
  toUI({ type: 'sessions', sessions: readSessions() });
  if (u.limits) toUI({ type: 'limits', limits: u.limits });
  toUI({ type: 'account', account: u.account });
}, 2000);

// ------------------------------------------------------------------ serwer

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.md': 'text/plain; charset=utf-8',
};

// Namiastka `chrome.webview`: SSE w dol, POST w gore. Dzieki niej assistant.js
// idzie sciezka produkcyjna, a nie trybem demo dla przegladarki.
const SHIM = `
window.chrome = { webview: {
  postMessage(s) { fetch('/__dev/ui', { method: 'POST', body: s }); },
  addEventListener(type, fn) {
    if (type !== 'message') return;
    new EventSource('/__dev/host').onmessage = (e) => fn({ data: e.data });
  },
}};
`;

createServer((req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1');

  if (url.pathname === '/__dev/host') {
    res.writeHead(200, {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache',
      Connection: 'keep-alive',
    });
    res.write(': polaczono\n\n');
    clients.add(res);
    req.on('close', () => clients.delete(res));
    return;
  }

  if (url.pathname === '/__dev/ui' && req.method === 'POST') {
    let body = '';
    req.on('data', (c) => { body += c; });
    req.on('end', () => {
      try { fromUI(JSON.parse(body)); } catch { /* ignorowane */ }
      res.writeHead(204).end();
    });
    return;
  }

  if (url.pathname === '/__dev/shim.js') {
    res.writeHead(200, { 'Content-Type': MIME['.js'] }).end(SHIM);
    return;
  }

  const name = url.pathname === '/' ? 'index.html' : normalize(url.pathname).replace(/^[\\/]+/, '');
  const file = join(UI_DIR, name);
  if (!file.startsWith(UI_DIR) || !existsSync(file)) {
    res.writeHead(404).end('nie ma takiego pliku');
    return;
  }

  let body = readFileSync(file);
  if (name === 'index.html') {
    // Dwie podmianki, obie wylacznie na czas podgladu w przegladarce:
    // 1. Namiastka musi wejsc PRZED assistant.js, inaczej skrypt zdazy uznac,
    //    ze nie dziala w WebView2, i wlaczy tryb demo.
    // 2. Produkcyjne `connect-src 'none'` jest sluszne - w WebView2 UI nie
    //    rusza sieci - ale tutaj zablokowaloby SSE i fetch namiastki.
    body = Buffer.from(String(body)
      .replace("connect-src 'none'", "connect-src 'self'")
      .replace(
        '<script src="assistant.js"></script>',
        '<script src="/__dev/shim.js"></script>\n<script src="assistant.js"></script>'));
  }
  res.writeHead(200, { 'Content-Type': MIME[extname(file)] || 'application/octet-stream' }).end(body);
}).listen(PORT, '127.0.0.1', () => {
  console.log(`[harness] UI:      http://127.0.0.1:${PORT}`);
  console.log(`[harness] katalog: ${CWD}`);
  console.log('[harness] Ctrl+C konczy harness razem z mostkiem.\n');
});

process.on('SIGINT', () => { bridge.kill(); process.exit(0); });
