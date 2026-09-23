// Markdown -> DOM. Wspolne dla okna rozmowy i dla dymka z odpowiedzia: jeden
// parser, jeden zestaw klas, jedno miejsce do poprawiania. Roznia sie tylko
// arkuszem i tym, jak kazde z nich rysuje blok kodu (opcja `code`).
//
// Tresc odpowiedzi modelu jest DANYMI, nie kodem: wszystko trafia do DOM-u
// przez textContent albo budowanie wezlow, nigdy przez innerHTML.

'use strict';

function node(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text !== undefined && text !== null) n.textContent = text;
  return n;
}

function clear(n) { while (n.firstChild) n.removeChild(n.firstChild); }

const FENCE = /^```([A-Za-z0-9_+-]*)\s*$/;

/**
 * Dzieli tekst na bloki i buduje wezly. Zwraca element .md
 * @param {string} src
 * @param {{code?: (code: string, lang: string) => Element}} [opts]
 *        `code` rysuje blok kodu; bez niej powstaje goły <pre>.
 */
function renderMarkdown(src, opts) {
  const code = (opts && opts.code) || ((body) => node('pre', null, body));
  const root = node('div', 'md');
  const lines = String(src).split('\n');
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];

    // blok kodu
    const fence = line.match(FENCE);
    if (fence) {
      const lang = fence[1] || '';
      const body = [];
      i++;
      while (i < lines.length && !/^```\s*$/.test(lines[i])) body.push(lines[i++]);
      i++; // zamykajaca linia
      root.appendChild(code(body.join('\n'), lang));
      continue;
    }

    // naglowek
    const head = line.match(/^(#{1,6})\s+(.*)$/);
    if (head) {
      const h = node('h2');
      inline(h, head[2]);
      root.appendChild(h);
      i++;
      continue;
    }

    // tabela
    if (/^\s*\|.*\|\s*$/.test(line) && i + 1 < lines.length && /^\s*\|[\s:|-]+\|\s*$/.test(lines[i + 1])) {
      const rows = [];
      while (i < lines.length && /^\s*\|.*\|\s*$/.test(lines[i])) rows.push(lines[i++]);
      root.appendChild(table(rows));
      continue;
    }

    // lista
    if (/^\s*[-*]\s+/.test(line)) {
      const ul = node('ul');
      while (i < lines.length && /^\s*[-*]\s+/.test(lines[i])) {
        const li = node('li');
        const span = node('span');
        inline(span, lines[i].replace(/^\s*[-*]\s+/, ''));
        li.appendChild(span);
        ul.appendChild(li);
        i++;
      }
      root.appendChild(ul);
      continue;
    }

    // pusta linia
    if (!line.trim()) { i++; continue; }

    // akapit
    const buf = [];
    while (i < lines.length && lines[i].trim() && !FENCE.test(lines[i]) &&
           !/^(#{1,6})\s+/.test(lines[i]) && !/^\s*[-*]\s+/.test(lines[i]) &&
           !/^\s*\|.*\|\s*$/.test(lines[i])) {
      buf.push(lines[i++]);
    }
    const p = node('p');
    inline(p, buf.join('\n'));
    root.appendChild(p);
  }

  return root;
}

/** Inline: **pogrubienie** i `kod`. Wszystko pozostale jako czysty tekst. */
function inline(parent, src) {
  const re = /\*\*([^*]+)\*\*|`([^`]+)`/g;
  let last = 0, m;
  while ((m = re.exec(src)) !== null) {
    if (m.index > last) parent.appendChild(document.createTextNode(src.slice(last, m.index)));
    if (m[1] !== undefined) parent.appendChild(node('strong', null, m[1]));
    else parent.appendChild(node('code', 'inline', m[2]));
    last = re.lastIndex;
  }
  if (last < src.length) parent.appendChild(document.createTextNode(src.slice(last)));
}

function table(rows) {
  const cells = (r) => r.trim().replace(/^\||\|$/g, '').split('|').map((c) => c.trim());
  const headCells = cells(rows[0]);
  const bodyRows = rows.slice(2).map(cells);

  const t = node('table');
  const thead = node('thead');
  const tr = node('tr');
  // Kolumna jest liczbowa, gdy wszystkie jej komorki w korpusie wygladaja na liczby.
  const numeric = headCells.map((_, c) =>
    bodyRows.length > 0 && bodyRows.every((r) => r[c] !== undefined && /^[\d\s.,:%+-]+$/.test(r[c])));

  headCells.forEach((h, c) => {
    const th = node('th', numeric[c] ? 'num' : null);
    inline(th, h);
    tr.appendChild(th);
  });
  thead.appendChild(tr);
  t.appendChild(thead);

  const tbody = node('tbody');
  bodyRows.forEach((r) => {
    const row = node('tr');
    headCells.forEach((_, c) => {
      const isLast = c === headCells.length - 1;
      const td = node('td', numeric[c] ? (isLast ? 'num muted' : 'num') : null);
      inline(td, r[c] !== undefined ? r[c] : '');
      row.appendChild(td);
    });
    tbody.appendChild(row);
  });
  t.appendChild(tbody);
  return t;
}
