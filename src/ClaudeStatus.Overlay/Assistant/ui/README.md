# Okno asystenta — warstwa widoku

Implementacja handoffu `design_handoff_okno_asystenta/` (poziom 4 pastylki).
Trzy pliki, zero zależności zewnętrznych — żadnego CDN, fontu z sieci ani
biblioteki npm. Ładowane do WebView2 przez
`SetVirtualHostNameToFolderMapping`, więc adres w kontrolce to
`https://assistant.invalid/index.html`, a katalog mapowany jest ten.

| Plik | Rola |
|---|---|
| `index.html` | struktura obu okien; `Content-Security-Policy` blokuje wszystko poza własnymi zasobami |
| `assistant.css` | tokeny i komponenty, wartości wprost ze `SPECYFIKACJA.md` |
| `assistant.js` | stan, renderowanie, markdown, most do hosta |

## Podgląd bez aplikacji

`index.html` otwiera się wprost w przeglądarce. Gdy `window.chrome.webview`
nie istnieje, `assistant.js` wstrzykuje dane demonstracyjne — nic z tego nie
wykonuje się w WebView2. Stan wybiera parametr `?stan=`:

```
index.html?stan=gotowa           markdown, tabela, blok kodu, wywołanie narzędzia
index.html?stan=strumien         odpowiedź w trakcie, kursor, Przerwij
index.html?stan=zgoda            karta zgody
index.html?stan=blad             błąd silnika
index.html?stan=narzedzie-blad   narzędzie zakończone błędem
index.html?stan=pusty            stan pusty
```

W konsoli działa też `__demo({type: "...", ...})` — ta sama funkcja, którą
karmi host, więc każdą wiadomość z tabel poniżej da się wywołać ręcznie.

## Bezpieczeństwo treści

Odpowiedzi modelu, argumenty i wyniki narzędzi są **danymi, nie kodem**.
Wszystko trafia do DOM-u przez `textContent` albo budowanie węzłów; w całym
`assistant.js` nie ma ani jednego `innerHTML`. Renderer markdownu składa
węzły sam — nie dokleja HTML-a ze stringów. Przy zmianach trzymaj tę zasadę:
jedno `innerHTML` z tekstem modelu wystarczy, żeby wynik narzędzia wykonał
się jako skrypt.

## Most z hostem

Host wysyła `webView.PostWebMessageAsString(json)`, UI odbiera przez
`chrome.webview.addEventListener('message')`. W drugą stronę UI woła
`chrome.webview.postMessage(json)`, host czyta w `WebMessageReceived`.
Każda wiadomość to obiekt z polem `type`.

### UI → host

| `type` | Pola | Kiedy |
|---|---|---|
| `ready` | — | po załadowaniu UI; host dopiero wtedy wysyła `init` |
| `prompt` | `text` | wysłanie tury |
| `interrupt` | — | Przerwij albo `Esc` w trakcie odpowiedzi |
| `consent` | `id`, `decision`: `once` \| `always` \| `deny` | decyzja z karty zgody |
| `newConversation` | — | `+` |
| `setModel` | `model` (pusty = z ustawień Claude Code) | wybór z listy modeli w nagłówku |
| `history` | — | otwarcie nakładki historii (host odsyła `history`) |
| `openConversation` | `id` | wybór rozmowy z historii |
| `activateSession` | `id` | klik w wiersz sesji — host aktywuje okno edytora |
| `toggleChat` | `open` | `‹` / `›`; host zmienia szerokość okna |
| `retryTool` | `id` | „Ponów" przy narzędziu z błędem |
| `retryTurn` | `id` | „Ponów turę" przy błędzie silnika |
| `errorDetails` | `id` | „Szczegóły" |
| `refreshUsage` | — | pierścień przy „odświeżono…" |
| `hide` | — | `Esc` albo `✕` |
| `copy` | `text` | tylko gdy `navigator.clipboard` zawiedzie |

### Host → UI

| `type` | Pola | Efekt |
|---|---|---|
| `init` | `anchor`, `title`, `context`, `models`, `suggestions`, `sessions`, `limits`, `account` | stan początkowy |
| `show` | `visible` | zdejmuje/zakłada klasę `closed` (animacja wzrostu z pastylki) |
| `anchor` | `anchor`: `top` \| `bottom` \| `side` | `transform-origin` i kierunek wzrostu |
| `sessions` | `sessions[]` | `{id, name, state, age, path}`; `state`: `pracuje` \| `gotowe` \| `czeka` \| `blad` \| `bezczynny` |
| `limits` | `limits` | `{window5h, window7d}`, każde `{usedPct, elapsedPct, reset}` |
| `account` | `account` | `{email, refreshedAgo}` |
| `context` | `context` | `{name, path}` — katalog roboczy; stały, tylko do pokazania |
| `models` | `models` | `{current, active, options[{value,name,description}]}` — `current` = wybór (`null` = z ustawień), `active` = pełny identyfikator sesji, gdy znany |
| `suggestions` | `items[]` | podpowiedzi na pusty ekran z wcześniejszych pytań; pusta lista = domyślne przykłady |
| `history` | `items[]` | `{id, title, meta}` |
| `title` | `title` | nazwa rozmowy w nagłówku |
| `reset` | — | nowa rozmowa: czyści turę, wraca do stanu pustego |
| `turnStart` | — | otwiera turę asystenta, włącza kursor strumienia |
| `delta` | `text` | dopisuje fragment — ścieżka szybka, bez przerysowania |
| `text` | `text` | pełna treść tury; przełącza render na markdown |
| `toolStart` | `id`, `label`, `metric` | wiersz narzędzia w stanie „pracuje" |
| `toolDone` | `id`, `label`, `metric`, `input`, `result` | wiersz zwija się, dane trafiają do rozwinięcia |
| `toolError` | `id`, `label`, `reason` | obrys czerwony, przycisk Ponów |
| `consent` | `consent` | `{id, lead, title, effect, facts[{label,value}]}` — zatrzymuje turę |
| `consentResolved` | — | host sam zdjął kartę (np. reguła dopisana gdzie indziej) |
| `engineError` | `error` | `{title, text, diagnostic}` |
| `done` | — | koniec tury: gaśnie kursor i „Pracuję…" |

### Kolejność, która ma znaczenie

- Host czeka na `ready`, zanim wyśle `init`. UI nie buforuje wiadomości
  przychodzących przed załadowaniem skryptu.
- `delta` bez wcześniejszego `turnStart` też zadziała (tura powstanie sama),
  ale wtedy pierwszy fragment wymusza pełne przerysowanie.
- `toolDone` i `toolError` szukają narzędzia po `id` wstecz przez wszystkie
  tury — `id` musi być unikalne w obrębie rozmowy.

## Decyzje podjęte przy implementacji

Trzy miejsca, w których handoff zostawiał swobodę:

1. **`Enter` przy karcie zgody.** Specyfikacja chce, żeby `Enter` znaczyło
   „zezwól raz", ale też żeby pole promptu zostało aktywne (można dopisać
   uwagę zamiast decydować). Te dwie rzeczy się gryzą. Rozstrzygnięcie:
   `Enter` decyduje tylko przy **pustym** polu; gdy coś w nim jest, wysyła
   wiadomość. `Esc` odrzuca zawsze.
2. **Licznik „N aktywnych".** Liczy sesje, które nie są bezczynne. Prototyp
   pokazywał 5 przy pięciu sesjach, z których jedna była bezczynna — słowo
   „aktywnych" byłoby wtedy nieprawdziwe. Zmiana to jedna linia w
   `renderSessions()`, gdyby liczba miała jednak obejmować wszystkie.
3. **Przerwa 8 px między oknami.** `body` ma tło przezroczyste, a każdy panel
   własne `#000`, promień i cień — dzięki temu ten sam dokument obsłuży
   zarówno jedno okno z przezroczystą przerwą, jak i dwa osobne okna. Wybór
   należy do hosta; UI nie zakłada żadnego z nich.

## Czego tu jeszcze nie ma

Warstwa widoku jest kompletna względem handoffu. Poza nią zostaje strona C#:
okno hostujące WebView2, skrót globalny, kotwiczenie przy pastylce i
tłumaczenie protokołu mostka (`assistant-bridge/`) na wiadomości z tabel
powyżej.
