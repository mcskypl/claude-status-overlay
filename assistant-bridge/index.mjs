// Most miedzy nakladka (C#) a Claude Agent SDK.
//
// Protokol: po jednej linii JSON w kazda strone. stdout nalezy WYLACZNIE do
// protokolu - wszystko, co diagnostyczne, idzie na stderr, inaczej nakladka
// dostaje smieci zamiast wiadomosci.
//
//   C# -> most:  {t:"prompt", text}          nowa tura
//                {t:"permission", id, allow, always}
//                {t:"interrupt"}
//                {t:"model", model}          zmiana modelu w biegu (null = z ustawien)
//                {t:"close"}
//
//   most -> C#:  {t:"ready", session, cwd, model, mcp:[{name,status}], tools}
//                {t:"models", models:[{value,name,description}]}
//                {t:"model", model}          model przelaczony
//                {t:"delta", text}           strumien odpowiedzi
//                {t:"text", text}            gotowy blok tekstu
//                {t:"thinking"}              model zaczal myslec
//                {t:"tool", id, name, input} wywolanie narzedzia
//                {t:"permission", id, title, tool, input, server, reason}
//                {t:"done", session, turns, usd}
//                {t:"error", message}

import { query } from '@anthropic-ai/claude-agent-sdk';
import { createInterface } from 'node:readline';
import { readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

// ---------------------------------------------------------------- argumenty

function arg(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i !== -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

const cwd = arg('cwd', join(homedir(), '.claude', 'assistant'));
const resume = arg('resume', undefined);
const model = arg('model', undefined);

// Claude Code zainstalowany u uzytkownika. Wydanie nakladki nie niesie wlasnego
// claude.exe (to ~230 MB), wiec nakladka podaje sciezke do tego, ktory jest.
// Bez niej SDK siega po binarke z node_modules - tak jest przy pracy ze zrodel.
const claudeExe = process.env.CLAUDE_STATUS_CLAUDE_EXE || undefined;

// ------------------------------------------------------------------ wyjscie

function send(obj) {
  process.stdout.write(JSON.stringify(obj) + '\n');
}

function log(...parts) {
  process.stderr.write(parts.join(' ') + '\n');
}

/** tool_result.content bywa stringiem albo lista blokow - panel chce jednego tekstu. */
function flattenToolResult(content) {
  if (typeof content === 'string') return content;
  if (!Array.isArray(content)) return '';
  return content
    .map((b) => (typeof b === 'string' ? b : b?.type === 'text' ? b.text : ''))
    .filter(Boolean)
    .join('\n');
}

// -------------------------------------------------------------- serwery MCP
//
// Czytane wprost z ~/.claude.json (klucz `mcpServers` na najwyzszym poziomie)
// i podawane jawnie. Nie zakladamy, ze SDK samo siegnie do magazynu CLI -
// `settingSources` ladują pliki settings.json, a serwery MCP mieszkaja gdzie
// indziej. Jawne podanie jest tansze w diagnozie niz cudza heurystyka.

function readUserMcpServers() {
  try {
    const raw = readFileSync(join(homedir(), '.claude.json'), 'utf8');
    const servers = JSON.parse(raw).mcpServers;
    if (!servers || typeof servers !== 'object') return {};
    log(`[most] MCP z ~/.claude.json: ${Object.keys(servers).join(', ') || 'brak'}`);
    return servers;
  } catch (err) {
    log(`[most] nie udalo sie odczytac ~/.claude.json: ${err.message}`);
    return {};
  }
}

// ------------------------------------------------------- kolejka wejsciowa
//
// query() chce asynchronicznego iteratora, a prompty przychodza z stdin w
// nieznanym momencie. Kolejka z odlozonym resolve robi z jednego drugie:
// gdy nic nie czeka, generator stoi na obietnicy zamiast krecic petla.

function makeQueue() {
  const waiting = [];
  const pending = [];
  let closed = false;

  return {
    push(item) {
      if (closed) return;
      const resolve = waiting.shift();
      if (resolve) resolve({ value: item, done: false });
      else pending.push(item);
    },
    close() {
      closed = true;
      while (waiting.length) waiting.shift()({ value: undefined, done: true });
    },
    async *[Symbol.asyncIterator]() {
      while (true) {
        if (pending.length) {
          yield pending.shift();
          continue;
        }
        if (closed) return;
        const next = await new Promise((resolve) => waiting.push(resolve));
        if (next.done) return;
        yield next.value;
      }
    },
  };
}

const prompts = makeQueue();

function userMessage(text) {
  return {
    type: 'user',
    message: { role: 'user', content: text },
    parent_tool_use_id: null,
    session_id: '',
  };
}

// --------------------------------------------------------------------- zgody
//
// canUseTool wstrzymuje ture do czasu decyzji z panelu. `suggestions` to
// gotowe reguly "nie pytaj wiecej o to samo" - oddane z powrotem jako
// updatedPermissions z destination userSettings sprawiaja, ze regule
// dopisuje SDK, a nie my recznie w cudzym settings.json.

const awaitingDecision = new Map();
let permissionSeq = 0;

function canUseTool(toolName, input, options) {
  const id = `p${++permissionSeq}`;
  send({
    t: 'permission',
    id,
    title: options.title ?? null,
    tool: toolName,
    input,
    server: options.mcpServer ? options.mcpServer.name : null,
    source: options.mcpServer ? options.mcpServer.source : null,
    reason: options.decisionReason ?? null,
  });

  return new Promise((resolve) => {
    const decide = (answer) => {
      awaitingDecision.delete(id);
      if (!answer.allow) {
        resolve({ behavior: 'deny', message: answer.message ?? 'Odrzucone w panelu.' });
        return;
      }
      const result = { behavior: 'allow' };
      if (answer.always && options.suggestions?.length) {
        result.updatedPermissions = options.suggestions.map((s) =>
          s.destination ? { ...s, destination: 'userSettings' } : s);
      }
      resolve(result);
    };

    awaitingDecision.set(id, decide);
    options.signal?.addEventListener('abort', () => decide({ allow: false, message: 'Tura przerwana.' }),
      { once: true });
  });
}

// ------------------------------------------------------------------- wejscie

const session = { id: resume ?? null };
let run = null;

createInterface({ input: process.stdin }).on('line', (line) => {
  const text = line.trim();
  if (!text) return;

  let msg;
  try {
    msg = JSON.parse(text);
  } catch {
    log(`[most] pominieto niepoprawny JSON: ${text.slice(0, 120)}`);
    return;
  }

  switch (msg.t) {
    case 'prompt':
      prompts.push(userMessage(msg.text));
      break;
    case 'permission': {
      const decide = awaitingDecision.get(msg.id);
      if (decide) decide(msg);
      else log(`[most] decyzja dla nieznanego zadania: ${msg.id}`);
      break;
    }
    case 'interrupt':
      run?.interrupt().catch((err) => log(`[most] interrupt: ${err.message}`));
      break;
    // Zmiana modelu nie restartuje sesji - SDK przelacza go od nastepnego
    // zapytania do API, a rozmowa zostaje ta sama. Pusty model wraca do tego
    // z settings.json.
    case 'model':
      run?.setModel(msg.model || undefined)
        .then(() => send({ t: 'model', model: msg.model || null }))
        .catch((err) => send({ t: 'error', message: `Nie udalo sie zmienic modelu: ${err.message}` }));
      break;
    case 'close':
      prompts.close();
      break;
    default:
      log(`[most] nieznany typ wiadomosci: ${msg.t}`);
  }
});

// -------------------------------------------------------------------- petla

async function main() {
  run = query({
    prompt: prompts,
    options: {
      cwd,
      resume: resume ?? undefined,
      model: model ?? undefined,
      pathToClaudeCodeExecutable: claudeExe,
      settingSources: ['user', 'project', 'local'],
      mcpServers: readUserMcpServers(),
      canUseTool,
      includePartialMessages: true,
      permissionMode: 'default',
    },
  });

  for await (const message of run) {
    switch (message.type) {
      case 'system':
        if (message.subtype === 'init') {
          session.id = message.session_id;
          send({
            t: 'ready',
            session: message.session_id,
            cwd: message.cwd,
            model: message.model ?? null,
            mcp: message.mcp_servers ?? [],
            tools: message.tools?.length ?? 0,
          });
          // Ta sama lista, co w /model - zalezy od konta, wiec nie trzymamy
          // jej na sztywno. Blad nie jest powodem, zeby psuc ture.
          run.supportedModels()
            .then((models) => send({
              t: 'models',
              models: models.map((m) => ({ value: m.value, name: m.displayName, description: m.description ?? '' })),
            }))
            .catch((err) => log(`[most] supportedModels: ${err.message}`));
        }
        break;

      case 'stream_event': {
        const ev = message.event;
        if (ev.type === 'content_block_delta' && ev.delta?.type === 'text_delta') {
          send({ t: 'delta', text: ev.delta.text });
        } else if (ev.type === 'content_block_start' && ev.content_block?.type === 'thinking') {
          send({ t: 'thinking' });
        }
        break;
      }

      case 'assistant':
        for (const block of message.message.content ?? []) {
          if (block.type === 'text') send({ t: 'text', text: block.text });
          else if (block.type === 'tool_use') {
            send({ t: 'tool', id: block.id, name: block.name, input: block.input });
          }
        }
        break;

      // Wyniki narzedzi wracaja jako wiadomosc uzytkownika z blokami tool_result -
      // to CLI dopisuje je do rozmowy, nie uzytkownik.
      case 'user':
        for (const block of message.message?.content ?? []) {
          if (block.type !== 'tool_result') continue;
          send({
            t: 'tool_result',
            id: block.tool_use_id,
            error: block.is_error === true,
            text: flattenToolResult(block.content),
          });
        }
        break;

      case 'result':
        send({
          t: 'done',
          session: message.session_id ?? session.id,
          turns: message.num_turns ?? null,
          usd: message.total_cost_usd ?? null,
          error: message.is_error ? (message.subtype ?? 'error') : null,
        });
        break;
    }
  }
}

main().catch((err) => {
  send({ t: 'error', message: err?.message ?? String(err) });
  log(err?.stack ?? String(err));
  process.exit(1);
});
