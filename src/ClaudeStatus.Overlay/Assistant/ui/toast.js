// Dymek „odpowiedz gotowa" - warstwa widoku. Odliczanie, pauza pod kursorem
// i pasek postepu naleza do strony; host tylko pokazuje okno, ustawia jego
// rozmiar i chowa je, gdy strona zglosi koniec.
//
//   host -> UI : answer {text, seconds}, close
//   UI  -> host: ready, size {height}, open, expired, hover {over}
//
// Tresc odpowiedzi jest DANYMI, nie kodem: idzie do DOM-u przez textContent
// i budowanie wezlow, nigdy przez innerHTML.

'use strict';

const webview = window.chrome && window.chrome.webview ? window.chrome.webview : null;

function send(type, payload) {
  const msg = Object.assign({ type }, payload || {});
  if (webview) webview.postMessage(JSON.stringify(msg));
  else console.info('[host]', msg);
}

const el = {
  card: document.getElementById('card'),
  progress: document.getElementById('progress'),
  countdown: document.getElementById('countdown'),
  body: document.getElementById('body'),
  hint: document.getElementById('hint'),
};

// ------------------------------------------------------------------ tresc
//
// Ten sam parser, co w oknie rozmowy (markdown.js) - tabela ma tu wygladac jak
// tabela, a nie jak akapit pelen kresek. Rozni sie tylko blok kodu: w dymku to
// goly <pre>, bez przycisku kopiowania i bez podswietlania, bo to zapowiedz,
// a nie miejsce do pracy z kodem. Za dlugie gasnie u dolu maska.

function renderBody(text) {
  clear(el.body);

  el.body.appendChild(renderMarkdown(text, {
    code: (body) => node('pre', null, body),
  }));
}

// -------------------------------------------------------------- odliczanie

let duration = 8000;
let endsAt = 0;
let left = 0;
let paused = false;
let raf = 0;

function tick(now) {
  if (paused) {
    endsAt = now + left;
    raf = requestAnimationFrame(tick);
    return;
  }

  left = Math.max(0, endsAt - now);
  const part = duration > 0 ? left / duration : 0;

  el.progress.style.width = (part * 100).toFixed(2) + '%';
  el.countdown.textContent = (Math.ceil(left / 1000) || 0) + 's';

  if (left <= 0) {
    // Tresc przenika, a host w tym samym czasie zwija ksztalt okna. Czekanie
    // z wiadomoscia zostawialoby na ekranie samo rozmyte szklo bez zawartosci.
    document.body.classList.add('closing');
    send('expired');
    return;
  }

  raf = requestAnimationFrame(tick);
}

function start(text, seconds) {
  cancelAnimationFrame(raf);

  renderBody(text);
  document.body.classList.remove('closing');

  duration = Math.max(1, seconds || 8) * 1000;
  left = duration;
  endsAt = performance.now() + duration;
  paused = false;
  el.hint.textContent = 'auto-ukrycie';

  // Wysokosc znamy dopiero po zlozeniu tresci - host dopasowuje do niej okno.
  //
  // Mierzymy SYNCHRONICZNIE, bez requestAnimationFrame. W chwili pomiaru okno
  // jest jeszcze schowane, a schowany WebView2 nie rysuje klatek, wiec rAF nie
  // odpalilby sie ani razu i host czekalby na wysokosc w nieskonczonosc.
  // getBoundingClientRect wymusza przeliczenie ukladu niezaleznie od rysowania.
  send('size', { height: Math.ceil(el.card.getBoundingClientRect().height) });

  raf = requestAnimationFrame(tick);
}

// ------------------------------------------------------------- zdarzenia UI

// Najechanie wstrzymuje odliczanie: czytasz, wiec dymek czeka.
document.documentElement.addEventListener('mouseenter', () => {
  paused = true;
  el.hint.textContent = 'pauza';
  send('hover', { over: true });
});

document.documentElement.addEventListener('mouseleave', () => {
  paused = false;
  el.hint.textContent = 'auto-ukrycie';
  send('hover', { over: false });
});

document.addEventListener('click', () => send('open'));

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') send('expired');
});

// ---------------------------------------------------------- wiadomosci hosta

const handlers = {
  answer(m) { start(m.text || '', m.seconds); },
  close() { cancelAnimationFrame(raf); document.body.classList.add('closing'); },
};

if (webview) {
  webview.addEventListener('message', (e) => {
    let m;
    try { m = typeof e.data === 'string' ? JSON.parse(e.data) : e.data; }
    catch { return; }

    const fn = handlers[m.type];
    if (fn) fn(m);
  });
}

send('ready');

// ------------------------------------------------ podglad w przegladarce
// Poza WebView2 (plik otwarty wprost w przegladarce) wstrzykuje przykladowa
// odpowiedz, zeby dalo sie obejrzec dymek bez uruchamiania apki.

if (!webview) {
  document.body.style.width = '560px';
  start(
    'Na dzisiaj (wtorek, 22.09.2026) masz 3 pozycje:\n\n' +
    '| Godziny | Wydarzenie | Uwagi |\n' +
    '|---|---|---|\n' +
    '| całodniowe | **Home [miejsce pracy]** | praca zdalna |\n' +
    '| 08:00–16:00 | **Wsparcie dodatkowe [nieobecność]** | całodzienny blok |\n' +
    '| 09:15–09:30 | **[#353539365097] Daily DEV** | 6 zaproszonych, Twoja odpowiedź: **nie** |\n\n' +
    'Czyli realnie jedyne spotkanie to Daily DEV o 9:15 — a na nie masz obecnie ustawione „nie".',
    8);
}
