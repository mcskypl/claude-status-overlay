// Podpowiedzi na pusty ekran asystenta, wyprowadzone z wczesniejszych pytan.
//
// Jednorazowy proces: czyta transkrypty sesji asystenta, prosi maly model o
// kilka krotkich, porzadnie napisanych propozycji i wypisuje jedna linie JSON
// na stdout: {suggestions: ["...", ...]}. Nie zapisuje wlasnej sesji - inaczej
// to pytanie trafiloby do historii, z ktorej za chwile bralibysmy podpowiedzi.
//
//   node suggest.mjs --cwd <katalog asystenta> [--model haiku]

import { query } from '@anthropic-ai/claude-agent-sdk';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

function arg(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i !== -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

const cwd = arg('cwd', join(homedir(), '.claude', 'assistant'));
const model = arg('model', 'haiku');

const MAX_FILES = 40;
const MAX_PROMPTS = 60;
const MIN_PROMPTS = 3;

function done(suggestions) {
  process.stdout.write(JSON.stringify({ suggestions }) + '\n');
}

// --------------------------------------------------------------- historia
//
// Claude Code trzyma transkrypty w ~/.claude/projects/<cwd>, gdzie kazdy znak
// spoza [A-Za-z0-9] zamienia sie w '-'. Liczy sie tylko to, co napisal
// czlowiek: tresc jako zwykly tekst (wyniki narzedzi przychodza jako lista
// blokow) i dluzsza niz kilka znakow - "dodaj" czy "tak" nie mowia nic o tym,
// o co sie pyta.

function readPrompts() {
  const dir = join(homedir(), '.claude', 'projects', cwd.replace(/[^A-Za-z0-9]/g, '-'));
  let files;
  try {
    files = readdirSync(dir)
      .filter((f) => f.endsWith('.jsonl'))
      .map((f) => ({ path: join(dir, f), mtime: statSync(join(dir, f)).mtimeMs }))
      .sort((a, b) => b.mtime - a.mtime)
      .slice(0, MAX_FILES);
  } catch {
    return [];
  }

  const seen = new Set();
  const prompts = [];
  for (const file of files) {
    for (const line of readFileSync(file.path, 'utf8').split('\n')) {
      if (!line.includes('"type":"user"')) continue;
      let entry;
      try { entry = JSON.parse(line); } catch { continue; }
      const text = entry?.message?.content;
      if (entry.type !== 'user' || entry.isSidechain || typeof text !== 'string') continue;

      const clean = text.trim().replace(/\s+/g, ' ');
      const key = clean.toLowerCase();
      if (clean.length < 12 || clean.startsWith('<') || seen.has(key)) continue;
      seen.add(key);
      prompts.push(clean.slice(0, 300));
    }
    if (prompts.length >= MAX_PROMPTS) break;
  }
  return prompts.slice(0, MAX_PROMPTS);
}

// ------------------------------------------------------------------ model

const SYSTEM = [
  'Układasz podpowiedzi na pusty ekran asystenta pracy. Asystent ma dostęp do',
  'HubSpota (zgłoszenia), Clockify (godziny), Azure DevOps (pull requesty),',
  'kalendarza Google, monitoringu aplikacji i lokalnej bazy SQL.',
  '',
  'Dostajesz wcześniejsze pytania użytkownika, od najnowszych. Wybierz z nich',
  'najczęstsze i najbardziej użyteczne tematy i napisz dokładnie 3 podpowiedzi:',
  '- po polsku, poprawnie, z polskimi znakami i bez literówek,',
  '- krótkie (do ~45 znaków), jako pytanie albo polecenie w 1. osobie;',
  '  pytanie kończy się znakiem zapytania,',
  '- ogólne i wielokrotnego użytku: bez konkretnych numerów, linków, nazwisk',
  '  i jednorazowych dat - "na piątek 12:00" zamień na "na jutro" albo pomiń,',
  '- nie przepisuj pytań 1:1; różne tematy, bez powtórzeń,',
  '- pomiń pytania testowe i o samego asystenta (np. o model).',
  '',
  'Odpowiedz wyłącznie tablicą JSON z trzema napisami, bez komentarza.',
].join('\n');

async function suggest(prompts) {
  const run = query({
    prompt: 'Wcześniejsze pytania:\n' + prompts.map((p) => '- ' + p).join('\n'),
    options: {
      cwd,
      model,
      systemPrompt: SYSTEM,
      // Zadnych narzedzi ani serwerow MCP - samo laczenie z konektorami
      // claude.ai potrafi trwac dluzej niz cala odpowiedz.
      tools: [],
      mcpServers: {},
      strictMcpConfig: true,
      settings: { disableClaudeAiConnectors: true },
      settingSources: [],
      persistSession: false,
      maxTurns: 1,
    },
  });

  let text = '';
  for await (const message of run) {
    if (message.type === 'result' && typeof message.result === 'string') text = message.result;
  }

  // Model bywa sklonny owinac odpowiedz w blok kodu - bierzemy sama tablice.
  const match = text.match(/\[[\s\S]*\]/);
  if (!match) throw new Error('odpowiedz bez tablicy JSON: ' + text.slice(0, 200));
  const list = JSON.parse(match[0]);
  return list
    .filter((s) => typeof s === 'string' && s.trim())
    .map((s) => s.trim().slice(0, 80))
    .slice(0, 3);
}

const prompts = readPrompts();
if (prompts.length < MIN_PROMPTS) {
  done([]);
} else {
  suggest(prompts)
    .then(done)
    .catch((err) => {
      process.stderr.write(`[podpowiedzi] ${err?.message ?? err}\n`);
      process.exit(1);
    });
}
