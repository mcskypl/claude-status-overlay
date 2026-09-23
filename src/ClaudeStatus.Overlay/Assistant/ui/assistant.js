// Okno asystenta - warstwa widoku. Nie zna Claude'a ani mostka Node: rozmawia
// wylacznie z hostem .NET, ktory tlumaczy protokol silnika na wiadomosci ponizej.
//
//   UI  -> host : ready, prompt, interrupt, consent, setModel,
//                 newConversation, openConversation, history, hide,
//                 retryTool, retryTurn, refreshUsage, installUpdate, copy
//   host -> UI  : init, anchor, show, limits, account, update, context, models, suggestions, history,
//                 turnStart, delta, text, toolStart, toolDone, toolError,
//                 consent, consentResolved, engineError, done, title, reset
//
// Tresc odpowiedzi modelu i wyniki narzedzi sa DANYMI, nie kodem: wszystko
// trafia do DOM-u przez textContent albo budowanie wezlow, nigdy przez innerHTML.

'use strict';

// ------------------------------------------------------------------ host

const webview = window.chrome && window.chrome.webview ? window.chrome.webview : null;

const host = {
  send(type, payload) {
    const msg = Object.assign({ type }, payload || {});
    if (webview) webview.postMessage(JSON.stringify(msg));
    else console.info('[host]', msg);
  },
};

// ------------------------------------------------------------------ stan

const state = {
  title: 'Nowa rozmowa',
  turns: [],
  streaming: false,
  consent: null,
  // Katalog roboczy jest staly - przychodzi z hosta i sluzy wylacznie do
  // pokazania, gdzie asystent pracuje.
  context: { name: '—', path: '' },
  // current = wybor z listy (null = z ustawien Claude Code), active = pelny
  // identyfikator, ktorym sesja naprawde jedzie, gdy host go zna.
  models: { current: null, active: '', options: [] },
  modelMenuOpen: false,
  suggestions: [],
  limits: null,
  update: null,
  account: { email: '', refreshedAgo: '' },
  history: [],
  historyOpen: false,
  anchor: 'top',
  micState: 'disabled',
  stickToBottom: true,
};

let turnSeq = 0;

// ------------------------------------------------------------------ skroty DOM

const $ = (id) => document.getElementById(id);

const el = {
  app: $('app'),
  limit5h: $('limit-5h'),
  limit7d: $('limit-7d'),
  account: $('account'),
  refreshed: $('refreshed'),
  refresh: $('refresh'),
  stateDot: $('state-dot'),
  convoTitle: $('convo-title'),
  contextChip: $('context-chip'),
  modelChip: $('model-chip'),
  modelName: $('model-name'),
  modelMenu: $('model-menu'),
  newConvo: $('new-convo'),
  historyBtn: $('history-btn'),
  history: $('history'),
  historyList: $('history-list'),
  historyClose: $('history-close'),
  scroll: $('scroll'),
  empty: $('empty'),
  emptyLead: $('empty-lead'),
  emptyExamples: $('empty-examples'),
  turns: $('turns'),
  field: $('field'),
  prompt: $('prompt'),
  send: $('send'),
  mic: $('mic'),
  charCount: $('char-count'),
  update: $('update'),
  updateText: $('update-text'),
  updateFill: $('update-fill'),
};

// Wezly zywe podczas strumienia - unikaja przerysowania calej rozmowy na kazdy token.
let stream = { text: null, wrap: null };

// ------------------------------------------------------------ pomocnicze DOM
//
// `node`, `clear`, `renderMarkdown`, `inline` i `table` mieszkaja w markdown.js -
// tym samym parserze, z ktorego korzysta dymek z odpowiedzia.

function show(n, visible) { n.classList.toggle('hidden', !visible); }

// ------------------------------------------------------------ podswietlanie

const KEYWORDS = {
  sql: 'select insert update delete from where group by order having join left right inner outer on as and or not null is in between like limit top distinct union all set values into create alter drop table index view sum count avg min max case when then else end desc asc',
  csharp: 'using namespace class struct interface record public private protected internal static readonly const var new return if else for foreach while do switch case break continue try catch finally throw async await void int string bool double decimal float long object null true false this base override virtual abstract sealed partial get set',
  javascript: 'const let var function return if else for while do switch case break continue try catch finally throw class extends new typeof instanceof async await import export from default null undefined true false this',
  powershell: 'if else elseif foreach for while do switch function param return break continue try catch finally throw begin process end filter in',
  json: 'true false null',
};
KEYWORDS.js = KEYWORDS.javascript;
KEYWORDS.ts = KEYWORDS.javascript;
KEYWORDS.cs = KEYWORDS.csharp;
KEYWORDS.ps1 = KEYWORDS.powershell;
KEYWORDS.pwsh = KEYWORDS.powershell;

const LINE_COMMENT = { sql: '--', csharp: '//', cs: '//', javascript: '//', js: '//', ts: '//', powershell: '#', ps1: '#', pwsh: '#' };

function codeBlock(code, lang) {
  const box = node('div', 'code');

  const head = node('div', 'code-head');
  head.appendChild(node('span', 'code-lang', lang || 'tekst'));
  head.appendChild(node('span', 'spacer'));
  const copy = node('button', 'code-copy', 'Kopiuj');
  copy.type = 'button';
  copy.addEventListener('click', () => {
    copyText(code);
    copy.textContent = 'Skopiowano';
    setTimeout(() => { copy.textContent = 'Kopiuj'; }, 1400);
  });
  head.appendChild(copy);
  box.appendChild(head);

  const pre = node('pre');
  highlight(pre, code, (lang || '').toLowerCase());
  box.appendChild(pre);
  return box;
}

function highlight(pre, code, lang) {
  const words = KEYWORDS[lang];
  if (!words) { pre.textContent = code; return; }

  const kw = new Set(words.split(/\s+/));
  const comment = LINE_COMMENT[lang];
  const commentRe = comment === '--' ? '--[^\\n]*' : comment === '#' ? '#[^\\n]*' : '//[^\\n]*';
  const re = new RegExp(
    '(' + commentRe + '|/\\*[\\s\\S]*?\\*/)' +          // 1 komentarz
    "|('(?:[^'\\\\]|\\\\.)*'|\"(?:[^\"\\\\]|\\\\.)*\")" + // 2 ciag znakow
    '|(\\b\\d+(?:\\.\\d+)?\\b)' +                        // 3 liczba
    '|([A-Za-z_][A-Za-z0-9_]*)',                         // 4 slowo
    'g');

  let last = 0, m;
  while ((m = re.exec(code)) !== null) {
    if (m.index > last) pre.appendChild(document.createTextNode(code.slice(last, m.index)));
    if (m[1]) pre.appendChild(node('span', 'tok-com', m[1]));
    else if (m[2]) pre.appendChild(node('span', 'tok-str', m[2]));
    else if (m[3]) pre.appendChild(node('span', 'tok-num', m[3]));
    else if (kw.has(m[4].toLowerCase())) pre.appendChild(node('span', 'tok-key', m[4]));
    else pre.appendChild(document.createTextNode(m[4]));
    last = re.lastIndex;
  }
  if (last < code.length) pre.appendChild(document.createTextNode(code.slice(last)));
}

function copyText(text) {
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(text).catch(() => host.send('copy', { text }));
  } else {
    host.send('copy', { text });
  }
}

// --------------------------------------------------------- limity i konto

function renderLimit(box, data) {
  if (!data) return;
  const used = Math.max(0, Math.min(100, data.usedPct));
  box.classList.toggle('low', 100 - used < 15);
  box.querySelector('.meter-fill').style.width = used + '%';
  const pace = box.querySelector('.meter-pace');
  if (typeof data.elapsedPct === 'number') {
    pace.style.left = Math.max(0, Math.min(100, data.elapsedPct)) + '%';
    pace.classList.remove('hidden');
  } else {
    pace.classList.add('hidden');
  }
  // W nagłówku jest ciasno, więc sam procent - słowo "zużyte" niosła szeroka kolumna.
  box.querySelector('.limit-pct').textContent = Math.round(used) + '%';
  box.querySelector('.limit-reset').textContent = data.reset || '';
}

function renderLimits() {
  if (!state.limits) return;
  renderLimit(el.limit5h, state.limits.window5h);
  renderLimit(el.limit7d, state.limits.window7d);
}

function renderAccount() {
  el.account.textContent = state.account.email || '';
  el.refreshed.textContent = state.account.refreshedAgo || '';
}

// Nowa wersja nakładki. Wiersz jest klikalny tylko wtedy, gdy jest co zrobić -
// w trakcie pobierania pokazuje sam postęp.
function renderUpdate() {
  const u = state.update;
  show(el.update, !!u);
  if (!u) return;

  el.updateText.textContent = u.text || '';
  el.update.classList.toggle('actionable', !!u.actionable);
  el.update.disabled = !u.actionable;
  el.updateFill.style.width =
    (typeof u.progress === 'number' && u.progress >= 0 ? Math.round(u.progress * 100) : 0) + '%';
}

// ------------------------------------------------------------ naglowek rozmowy

function headState() {
  if (state.turns.some((t) => t.error)) return 'error';
  if (state.consent) return 'waiting';
  if (state.streaming) return 'working';
  if (state.turns.length) return 'done';
  return 'idle';
}

function renderHead() {
  el.convoTitle.textContent = state.title;
  el.stateDot.className = 'state-dot ' + headState();
  el.contextChip.textContent = state.context.name || '—';
  el.contextChip.title = state.context.path || '';
  renderModel();
}

// ------------------------------------------------------------------ model

function modelOption(value) {
  return state.models.options.find((o) => o.value === value) || null;
}

function renderModel() {
  const m = state.models;
  const picked = m.current ? modelOption(m.current) : null;
  el.modelName.textContent = picked ? picked.name : (m.current || 'Domyślny');
  el.modelChip.title = 'Zmień model' + (m.active ? ' · teraz: ' + m.active : '');
  el.modelChip.setAttribute('aria-expanded', state.modelMenuOpen ? 'true' : 'false');
  el.modelChip.classList.toggle('open', state.modelMenuOpen);
  show(el.modelMenu, state.modelMenuOpen);
  if (!state.modelMenuOpen) return;

  clear(el.modelMenu);
  const rows = [{ value: null, name: 'Domyślny', description: 'Model z ustawień Claude Code' }]
    .concat(m.options);
  rows.forEach((o) => {
    const selected = (o.value || null) === (m.current || null);
    const b = node('button', 'model-item' + (selected ? ' selected' : ''));
    b.type = 'button';
    b.setAttribute('role', 'option');
    b.setAttribute('aria-selected', selected ? 'true' : 'false');
    b.appendChild(node('span', 'model-item-name', o.name));
    if (o.description) b.appendChild(node('span', 'model-item-desc', o.description));
    b.addEventListener('click', () => pickModel(o.value || null));
    el.modelMenu.appendChild(b);
  });
}

function toggleModelMenu(open) {
  state.modelMenuOpen = open === undefined ? !state.modelMenuOpen : open;
  renderModel();
}

// Zmiana dziala od nastepnego pytania, w tej samej rozmowie - host przelacza
// model w biegu, a przy zatrzymanym silniku poda go przy starcie.
function pickModel(value) {
  state.modelMenuOpen = false;
  if ((value || null) !== (state.models.current || null)) {
    state.models.current = value;
    state.models.active = '';
    host.send('setModel', { model: value || '' });
  }
  renderModel();
  el.prompt.focus();
}

function renderHistory() {
  clear(el.historyList);
  state.history.forEach((h) => {
    const b = node('button', 'history-item');
    b.type = 'button';
    b.appendChild(node('span', 'history-title', h.title));
    b.appendChild(node('span', 'history-meta', h.meta || ''));
    b.addEventListener('click', () => {
      host.send('openConversation', { id: h.id });
      toggleHistory(false);
    });
    el.historyList.appendChild(b);
  });
}

// ------------------------------------------------------------------ rozmowa

function renderTool(tool) {
  const forced = tool.status === 'running' || tool.status === 'error';
  const open = forced || tool.expanded;
  const box = node('div', 'tool' + (tool.status === 'error' ? ' error' : '') + (open ? ' open' : ''));

  const head = node('button', 'tool-head');
  head.type = 'button';

  if (tool.status === 'running') head.appendChild(node('span', 'tool-ring'));
  else head.appendChild(node('span', 'dot ' + (tool.status === 'error' ? 'error' : 'done')));

  head.appendChild(node('span', 'tool-name', tool.label));
  head.appendChild(node('span', 'spacer'));

  if (tool.status === 'error') {
    const retry = node('button', 'tool-retry', 'Ponów');
    retry.type = 'button';
    retry.addEventListener('click', (e) => {
      e.stopPropagation();
      host.send('retryTool', { id: tool.id });
    });
    head.appendChild(retry);
  } else {
    if (tool.metric) head.appendChild(node('span', 'tool-metric', tool.metric));
    if (tool.status === 'done') head.appendChild(node('span', 'tool-chevron', '▾'));
  }

  if (tool.status === 'done') {
    head.addEventListener('click', () => { tool.expanded = !tool.expanded; renderTurns(); });
  }
  box.appendChild(head);

  if (tool.status === 'error' && tool.reason) {
    box.appendChild(node('div', 'tool-reason', tool.reason));
  } else if (tool.status === 'done' && open) {
    const body = node('div', 'tool-body appear');
    body.appendChild(node('div', 'label-caps', 'Argumenty'));
    body.appendChild(node('pre', null, tool.input || ''));
    body.appendChild(node('div', 'label-caps', 'Wynik'));
    body.appendChild(node('pre', 'result', tool.result || ''));
    box.appendChild(body);
  }

  return box;
}

function renderConsent(c) {
  const wrap = node('div', 'consent-wrap');
  if (c.lead) {
    const lead = node('p');
    lead.style.cssText = 'margin:0;font-size:14px;line-height:1.55;color:var(--ink-90)';
    inline(lead, c.lead);
    wrap.appendChild(lead);
  }

  const card = node('div', 'consent appear');

  const flag = node('div', 'consent-flag');
  flag.appendChild(node('span', 'dot waiting'));
  flag.appendChild(node('span', 'label', 'Czeka na Twoją decyzję'));
  card.appendChild(flag);

  const block = node('div');
  block.style.cssText = 'display:flex;flex-direction:column;gap:6px';
  const title = node('div', 'consent-title');
  inline(title, c.title);
  block.appendChild(title);
  if (c.effect) {
    const eff = node('div', 'consent-effect');
    inline(eff, c.effect);
    block.appendChild(eff);
  }
  card.appendChild(block);

  if (c.facts && c.facts.length) {
    const dl = node('dl', 'consent-facts');
    dl.style.margin = '0';
    c.facts.forEach((f) => {
      dl.appendChild(node('dt', null, f.label));
      dl.appendChild(node('dd', null, f.value));
    });
    card.appendChild(dl);
  }

  const actions = node('div', 'consent-actions');
  const once = node('button', 'btn btn-primary', 'Zezwól raz');
  once.type = 'button';
  once.addEventListener('click', () => decide('once'));
  const always = node('button', 'btn btn-outline-blue', 'Zezwól zawsze');
  always.type = 'button';
  always.addEventListener('click', () => decide('always'));
  const deny = node('button', 'btn btn-outline', 'Odrzuć');
  deny.type = 'button';
  deny.addEventListener('click', () => decide('deny'));
  actions.appendChild(once);
  actions.appendChild(always);
  actions.appendChild(node('span', 'spacer'));
  actions.appendChild(deny);
  card.appendChild(actions);

  card.appendChild(node('div', 'consent-keys',
    'Enter = zezwól raz · Esc = odrzuć · „zawsze" zapamiętuje regułę dla tego katalogu'));

  wrap.appendChild(card);
  return wrap;
}

function renderEngineError(e) {
  const box = node('div', 'engine-error appear');
  const head = node('div', 'engine-error-head');
  head.appendChild(node('span', 'dot error'));
  head.appendChild(node('span', 'engine-error-title', e.title || 'Zerwane połączenie z silnikiem'));
  box.appendChild(head);
  if (e.text) box.appendChild(node('div', 'engine-error-text', e.text));
  if (e.diagnostic) box.appendChild(node('div', 'engine-error-diag', e.diagnostic));

  const actions = node('div', 'engine-error-actions');
  const retry = node('button', 'btn btn-sm btn-fill', 'Ponów turę');
  retry.type = 'button';
  retry.addEventListener('click', () => host.send('retryTurn', { id: e.turnId }));
  const details = node('button', 'btn btn-sm btn-outline', 'Szczegóły');
  details.type = 'button';
  details.addEventListener('click', () => host.send('errorDetails', { id: e.turnId }));
  actions.appendChild(retry);
  actions.appendChild(details);
  box.appendChild(actions);
  return box;
}

function renderTurns() {
  const empty = state.turns.length === 0;
  show(el.empty, empty);
  show(el.turns, !empty);
  if (empty) return;

  clear(el.turns);
  stream.text = null;
  stream.wrap = null;

  state.turns.forEach((t) => {
    if (t.role === 'user') {
      const row = node('div', 'turn-user');
      row.appendChild(node('div', 'bubble', t.text));
      el.turns.appendChild(row);
      return;
    }

    const row = node('div', 'turn-assistant');
    (t.tools || []).forEach((tool) => row.appendChild(renderTool(tool)));

    if (t.streaming) {
      const wrap = node('div', 'md');
      const p = node('p');
      const textNode = document.createTextNode(t.text || '');
      p.appendChild(textNode);
      p.appendChild(node('span', 'stream-caret'));
      wrap.appendChild(p);
      row.appendChild(wrap);
      stream.text = textNode;
      stream.wrap = wrap;

      const status = node('div', 'turn-status');
      status.appendChild(node('span', 'dot working'));
      status.appendChild(node('span', 'turn-status-text', 'Pracuję…'));
      const stop = node('button', 'btn-ghost');
      stop.type = 'button';
      stop.appendChild(node('span', 'stop-square'));
      stop.appendChild(document.createTextNode('Przerwij'));
      stop.appendChild(node('span', 'key', 'Esc'));
      stop.addEventListener('click', () => host.send('interrupt'));
      status.appendChild(stop);
      row.appendChild(status);
    } else if (t.text) {
      const md = renderMarkdown(t.text, { code: codeBlock });
      md.classList.add('appear');
      row.appendChild(md);
    }

    if (t.consent) row.appendChild(renderConsent(t.consent));
    if (t.error) row.appendChild(renderEngineError(Object.assign({ turnId: t.id }, t.error)));

    el.turns.appendChild(row);
  });

  scrollToBottom();
}

function scrollToBottom(force) {
  if (!state.stickToBottom && !force) return;
  el.scroll.scrollTop = el.scroll.scrollHeight;
}

// ------------------------------------------------------------------ akcje

function currentAssistantTurn() {
  for (let i = state.turns.length - 1; i >= 0; i--) {
    if (state.turns[i].role === 'assistant') return state.turns[i];
  }
  return null;
}

function ensureAssistantTurn() {
  const last = state.turns[state.turns.length - 1];
  if (last && last.role === 'assistant') return last;
  const t = { id: 'a' + (++turnSeq), role: 'assistant', text: '', tools: [], streaming: false };
  state.turns.push(t);
  return t;
}

function submitPrompt() {
  const text = el.prompt.value.trim();
  if (!text || state.streaming) return;

  state.turns.push({ id: 'u' + (++turnSeq), role: 'user', text });
  state.stickToBottom = true;
  el.prompt.value = '';
  syncComposer();
  renderTurns();
  renderHead();
  host.send('prompt', { text });
}

function decide(decision) {
  if (!state.consent) return;
  host.send('consent', { id: state.consent.id, decision });
  const turn = currentAssistantTurn();
  if (turn) turn.consent = null;
  state.consent = null;
  renderTurns();
  renderHead();
}

function toggleHistory(open) {
  state.historyOpen = open === undefined ? !state.historyOpen : open;
  show(el.history, state.historyOpen);
  if (state.historyOpen) {
    host.send('history');
    renderHistory();
    el.history.classList.add('appear');
  }
}

// ------------------------------------------------------------- kompozytor

function syncComposer() {
  const value = el.prompt.value;
  const has = value.trim().length > 0;

  el.prompt.style.height = 'auto';
  el.prompt.style.height = Math.min(el.prompt.scrollHeight, 132) + 'px';

  el.field.classList.toggle('filled', has);
  el.field.classList.toggle('multiline', el.prompt.scrollHeight > 30 || value.indexOf('\n') >= 0);
  el.send.classList.toggle('active', has);
  el.charCount.textContent = value.length ? value.length + ' / 4000' : '';
}

// ------------------------------------------------------------ klawiatura

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    e.preventDefault();
    if (state.modelMenuOpen) { toggleModelMenu(false); return; }
    if (state.consent) { decide('deny'); return; }
    if (state.historyOpen) { toggleHistory(false); return; }
    if (state.streaming) { host.send('interrupt'); return; }
    host.send('hide');
    return;
  }

  if (e.key === 'Enter' && !e.shiftKey) {
    // Pole promptu zostaje aktywne przy karcie zgody: Enter decyduje tylko wtedy,
    // gdy nie ma czego wyslac - inaczej uwaga dopisana do pola wygralaby z decyzja.
    if (state.consent && !el.prompt.value.trim()) {
      e.preventDefault();
      decide('once');
      return;
    }
    if (document.activeElement === el.prompt) {
      e.preventDefault();
      submitPrompt();
    }
  }
});

// -------------------------------------------------------------- zdarzenia UI

el.prompt.addEventListener('input', syncComposer);
el.send.addEventListener('click', submitPrompt);
el.newConvo.addEventListener('click', () => host.send('newConversation'));
el.historyBtn.addEventListener('click', () => toggleHistory());
el.historyClose.addEventListener('click', () => toggleHistory(false));
el.modelChip.addEventListener('click', (e) => { e.stopPropagation(); toggleModelMenu(); });
document.addEventListener('click', (e) => {
  if (state.modelMenuOpen && !el.modelMenu.contains(e.target)) toggleModelMenu(false);
});
el.update.addEventListener('click', () => {
  if (state.update && state.update.actionable) host.send('installUpdate');
});
el.refresh.addEventListener('click', () => {
  el.refresh.classList.add('spinning');
  host.send('refreshUsage');
  setTimeout(() => el.refresh.classList.remove('spinning'), 8000);
});

el.scroll.addEventListener('scroll', () => {
  const dist = el.scroll.scrollHeight - el.scroll.scrollTop - el.scroll.clientHeight;
  state.stickToBottom = dist <= 40;
});

// ------------------------------------------------------ wiadomosci od hosta

const handlers = {
  init(m) {
    if (m.context) state.context = Object.assign(state.context, m.context);
    if (m.models) state.models = m.models;
    if (Array.isArray(m.suggestions)) state.suggestions = m.suggestions;
    if (m.limits) state.limits = m.limits;
    if (m.update) state.update = m.update;
    if (m.account) state.account = m.account;
    if (m.anchor) handlers.anchor(m);
    if (m.title) state.title = m.title;
    renderAll();
  },

  anchor(m) {
    state.anchor = m.anchor;
    el.app.dataset.anchor = m.anchor;
    // Korzeń też musi wiedzieć: przy dolnej kotwicy treść trzyma się dołu.
    document.body.dataset.anchor = m.anchor;
  },

  /// Kształt okna. Rogi i rozmycie nakłada DWM na całe okno, więc to host wie,
  /// jaki jest promień; bok przy krawędzi ekranu wypycha poza nią, a tutaj
  /// wyrównuje to wyściółką, żeby treść nie wyjechała razem z rogami.
  /// `extent` to docelowa wysokość - stała przez cały wjazd, więc zmiana
  /// wysokości okna tylko przycina treść, zamiast przebudowywać jej układ.
  shape(m) {
    const root = document.documentElement.style;
    root.setProperty('--pane-radius', m.radius);
    root.setProperty('--pane-pad', m.pad);
    if (m.extent) root.setProperty('--extent', m.extent + 'px');
    if (m.extentW) root.setProperty('--extent-w', m.extentW + 'px');
  },

  show(m) {
    el.app.classList.toggle('closed', m.visible === false);
    if (m.visible !== false) setTimeout(() => el.prompt.focus(), 60);
  },

  limits(m) { state.limits = m.limits; renderLimits(); },
  update(m) { state.update = m.update || null; renderUpdate(); },
  account(m) { state.account = m.account || state.account; renderAccount(); },
  history(m) { state.history = m.items || []; if (state.historyOpen) renderHistory(); },
  title(m) { state.title = m.title || 'Nowa rozmowa'; renderHead(); },

  context(m) {
    state.context = Object.assign(state.context, m.context || {});
    renderHead();
  },

  models(m) {
    if (m.models) state.models = m.models;
    renderModel();
  },

  // Przychodza w tle, kilkadziesiat sekund po starcie - pusty ekran podmienia
  // je od razu, rozmowy w toku nie dotykaja.
  suggestions(m) {
    state.suggestions = Array.isArray(m.items) ? m.items : [];
    renderEmptyLead();
  },

  reset() {
    state.turns = [];
    state.consent = null;
    state.streaming = false;
    state.title = 'Nowa rozmowa';
    renderTurns();
    renderHead();
    el.prompt.focus();
  },

  turnStart() {
    const t = ensureAssistantTurn();
    t.streaming = true;
    t.text = '';
    state.streaming = true;
    state.stickToBottom = true;
    renderTurns();
    renderHead();
  },

  // Szybka sciezka: dopisuje do istniejacego wezla tekstowego zamiast przerysowywac.
  delta(m) {
    const t = ensureAssistantTurn();
    t.streaming = true;
    state.streaming = true;
    t.text += m.text;
    if (stream.text) {
      stream.text.nodeValue = t.text;
      scrollToBottom();
    } else {
      renderTurns();
    }
  },

  text(m) {
    const t = ensureAssistantTurn();
    t.text = m.text;
    t.streaming = false;
    renderTurns();
  },

  toolStart(m) {
    const t = ensureAssistantTurn();
    t.tools.push({ id: m.id, label: m.label, status: 'running', metric: m.metric || '', expanded: false });
    renderTurns();
    renderHead();
  },

  toolDone(m) {
    const tool = findTool(m.id);
    if (!tool) return;
    tool.status = 'done';
    tool.label = m.label || tool.label;
    tool.metric = m.metric || '';
    tool.input = m.input || '';
    tool.result = m.result || '';
    renderTurns();
  },

  toolError(m) {
    const tool = findTool(m.id);
    if (!tool) return;
    tool.status = 'error';
    tool.label = m.label || tool.label;
    tool.reason = m.reason || '';
    renderTurns();
    renderHead();
  },

  consent(m) {
    state.consent = m.consent;
    const t = ensureAssistantTurn();
    t.consent = m.consent;
    t.streaming = false;
    state.streaming = false;
    state.stickToBottom = true;
    renderTurns();
    renderHead();
  },

  consentResolved() {
    state.consent = null;
    const t = currentAssistantTurn();
    if (t) t.consent = null;
    renderTurns();
    renderHead();
  },

  engineError(m) {
    const t = ensureAssistantTurn();
    t.error = m.error || {};
    t.streaming = false;
    state.streaming = false;
    renderTurns();
    renderHead();
  },

  done() {
    const t = currentAssistantTurn();
    if (t) t.streaming = false;
    state.streaming = false;
    renderTurns();
    renderHead();
  },
};

function findTool(id) {
  for (let i = state.turns.length - 1; i >= 0; i--) {
    const tools = state.turns[i].tools || [];
    for (const t of tools) if (t.id === id) return t;
  }
  return null;
}

function onHostMessage(raw) {
  let m;
  try { m = typeof raw === 'string' ? JSON.parse(raw) : raw; }
  catch { return; }
  const fn = handlers[m.type];
  if (fn) fn(m);
}

if (webview) {
  webview.addEventListener('message', (e) => onHostMessage(e.data));
}

// ------------------------------------------------------------------ start

// Na start i wtedy, gdy historia jest za krotka - host podmienia je na
// podpowiedzi wyprowadzone z wczesniejszych pytan (wiadomosc `suggestions`).
const EXAMPLES = [
  'Ile godzin zaraportowałem w tym tygodniu?',
  'Co czeka na code review?',
  'Błędy z produkcji od wczoraj',
];

// Katalog roboczy niesie teraz chip w nagłówku, więc nie powtarzamy go tutaj.
function renderEmptyLead() {
  clear(el.emptyLead);
  el.emptyLead.appendChild(document.createTextNode(
    'Sięgnę po zgłoszenia w HubSpocie, godziny w Clockify, pull requesty w Azure DevOps, '
    + 'logi z monitoringu i dane z bazy.'));

  clear(el.emptyExamples);
  (state.suggestions.length ? state.suggestions : EXAMPLES).forEach((text) => {
    const b = node('button', 'example', text);
    b.type = 'button';
    // Podpowiedź to gotowe pytanie - klik wysyła je od razu.
    b.addEventListener('click', () => {
      el.prompt.value = text;
      submitPrompt();
    });
    el.emptyExamples.appendChild(b);
  });
}

function renderAll() {
  renderLimits();
  renderUpdate();
  renderAccount();
  renderHead();
  renderEmptyLead();
  renderTurns();
}

renderAll();
syncComposer();
host.send('ready');

// ------------------------------------------------ podglad w przegladarce
// Poza WebView2 (czyli gdy plik otwiera sie wprost w przegladarce) wstrzykuje
// dane demonstracyjne, zeby dalo sie obejrzec kazdy stan bez uruchamiania apki.
// W WebView2 ten blok nie wykonuje sie wcale.

if (!webview) {
  const demo = new URLSearchParams(location.search).get('stan') || 'gotowa';
  window.__demo = onHostMessage;

  onHostMessage({
    type: 'init',
    anchor: 'top',
    title: 'Godziny Nordkraft · tydzień 38',
    context: { name: '~/assistant', path: 'C:\\Users\\sebas\\.claude\\assistant' },
    models: {
      current: 'opus[1m]',
      active: 'claude-opus-5-5[1m]',
      options: [
        { value: 'opus[1m]', name: 'Opus · 1M', description: 'Najmocniejszy, z kontekstem 1M tokenów' },
        { value: 'opus', name: 'Opus', description: 'Najmocniejszy' },
        { value: 'sonnet', name: 'Sonnet', description: 'Szybszy i tańszy na co dzień' },
        { value: 'haiku', name: 'Haiku', description: 'Najszybszy, do prostych pytań' },
      ],
    },
    limits: {
      window5h: { usedPct: 12, elapsedPct: 33, reset: 'reset 03:10' },
      window7d: { usedPct: 59, elapsedPct: 66, reset: 'reset czw 10:00' },
    },
    account: { email: 'sebastian.westfal@optimakers.pl', refreshedAgo: 'odświeżono 3m temu' },
    update: demo === 'aktualizacja'
      ? { text: 'nowa wersja 2.1.0 - zaktualizuj', actionable: true }
      : null,
  });

  onHostMessage({ type: 'show', visible: true });
  onHostMessage({ type: 'history', items: [
    { id: 'h1', title: 'Godziny Nordkraft · tydzień 38', meta: 'dziś, 14:02 · 6 tur' },
    { id: 'h2', title: 'Błędy 5xx po wdrożeniu 2.14', meta: 'wczoraj · 11 tur' },
    { id: 'h3', title: 'Deale HubSpot bez aktywności', meta: 'poniedziałek · 4 tury' },
  ]});

  const ask = (text) => { state.turns.push({ id: 'u0', role: 'user', text }); };

  const MD = [
    'W tym tygodniu na Nordkraft poszło **41 h 20 min**, czyli o 3 h 05 min więcej niż w poprzednim.',
    '',
    '## Rozbicie na zadania',
    '',
    '| Zadanie | Czas | Udział |',
    '|---|---|---|',
    '| API · integracja HubSpot | 18:10 | 44 % |',
    '| Frontend · widok raportów | 14:45 | 36 % |',
    '| Spotkania | 08:25 | 20 % |',
    '',
    '- Dwa wpisy nie mają przypisanego zadania (łącznie 1 h 15 min).',
    '- Czwartek jest pusty — sprawdź, czy timer nie został zatrzymany.',
    '',
    '```sql',
    'SELECT task, SUM(duration_min) AS mins',
    'FROM time_entries',
    "WHERE project = 'Nordkraft'",
    "  AND day BETWEEN '2026-09-14' AND '2026-09-20'",
    'GROUP BY task',
    'ORDER BY mins DESC;  -- 3 wiersze',
    '```',
  ].join('\n');

  if (demo === 'pusty') {
    onHostMessage({ type: 'reset' });
  } else if (demo === 'strumien') {
    ask('Zsumuj godziny z Clockify na Nordkraft za ten tydzień.');
    onHostMessage({ type: 'turnStart' });
    onHostMessage({ type: 'toolStart', id: 't1', label: 'Clockify · pobieram wpisy z tygodnia…', metric: '1,8 s' });
    const S = 'Zestawiłem tydzień 14–20 września. Na Nordkraft poszło 41 h 20 min, najwięcej w środę i piątek. ';
    let i = 0;
    setInterval(() => {
      if (i >= S.length) return;
      onHostMessage({ type: 'delta', text: S.slice(i, i + 2) });
      i += 2;
    }, 36);
  } else if (demo === 'zgoda') {
    ask('Wypchnij gotowe zmiany na zdalne repo.');
    onHostMessage({ type: 'consent', consent: {
      id: 'c1',
      lead: 'Zmiany są gotowe na gałęzi `feature/clockify-sync`. Zanim wypchnę je na zdalne repozytorium, potrzebuję zgody.',
      title: 'git push — wypchnięcie 3 commitów',
      effect: 'Wyśle gałąź `feature/clockify-sync` do `origin`. Operacja jest widoczna dla zespołu i uruchomi pipeline CI.',
      facts: [
        { label: 'Narzędzie', value: 'git.push' },
        { label: 'Katalog', value: 'D:\\repos\\opti-crm-api' },
        { label: 'Zdalne', value: 'origin → dev.azure.com/optimakers' },
      ],
    }});
  } else if (demo === 'blad') {
    ask('Pokaż błędy 5xx z produkcji od wczoraj.');
    onHostMessage({ type: 'engineError', error: {
      title: 'Zerwane połączenie z silnikiem',
      text: 'Tura została przerwana po 6 s bez odpowiedzi. Treść pytania jest zachowana — możesz ponowić bez przepisywania.',
      diagnostic: 'ERR_ENGINE_TIMEOUT · 2026-09-22 14:07:11 · próba 1/3',
    }});
  } else if (demo === 'narzedzie-blad') {
    ask('Pobierz work items ze sprintu 41.');
    onHostMessage({ type: 'toolStart', id: 't9', label: 'Azure DevOps · pobieram work items…' });
    onHostMessage({ type: 'toolError', id: 't9',
      label: 'Azure DevOps · nie udało się pobrać work items',
      reason: 'HTTP 401 · token PAT wygasł 2026-09-18' });
  } else {
    ask('Zsumuj godziny z Clockify na Nordkraft za ten tydzień i porównaj z poprzednim.');
    onHostMessage({ type: 'toolStart', id: 't1', label: 'Clockify · pobieram wpisy…' });
    onHostMessage({ type: 'toolDone', id: 't1',
      label: 'Clockify · pobrano wpisy z tygodnia',
      metric: '2,1 s · 38 wpisów',
      input: '{\n  "workspace": "Optimakers",\n  "from": "2026-09-14",\n  "to": "2026-09-20",\n  "project": "Nordkraft"\n}',
      result: '38 wpisów · 41 h 20 min\nNordkraft/API        18 h 10 min\nNordkraft/Frontend   14 h 45 min\nNordkraft/Spotkania   8 h 25 min' });
    onHostMessage({ type: 'text', text: MD });
    onHostMessage({ type: 'done' });
  }
}
