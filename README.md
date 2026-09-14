# Claude Status Overlay (C# / .NET 10)

Czarna, zawsze widoczna linia przyklejona do krawędzi ekranu, która na trzy
sposoby mówi, co robi Claude Code:

| Poziom | Kiedy widać | Co pokazuje |
|---|---|---|
| **Brzeg** | zawsze | cienka linia (148×5 px) - proporcje limitów 5 h / 7 dni jako dwa paski rosnące ku barwnemu znacznikowi stanu, który jest granicą między nimi (paski nie spotykają się na środku) |
| **Pasek** | po najechaniu kursorem (o ile włączone w menu) | mały pierścień stanu, wartość (czas albo nazwa stanu) i oba mierniki w linii z procentami |
| **Panel** | po kliknięciu, do kolejnego kliknięcia | pełna lista sesji (projekt, stan, wiek) i mierniki limitów z terminem resetu oraz przyciskiem ręcznego odświeżenia |

Trzy poziomy przenikają się płynnym morfingiem (380 ms); kotwica lewa/prawa
krawędź obraca brzeg i pasek do układu pionowego.

| Znacznik | Stan | Kiedy |
|---|---|---|
| 🟠 bursztyn (pulsuje) | `pracuje` | wysłałeś prompt, Claude pracuje |
| 🟢 zielony | `gotowe` | Claude skończył odpowiedź |
| 🔵 niebieski (miga, 1 s) | `czeka na Ciebie` + dźwięk | prośba o zgodę na narzędzie / pytanie |
| 🔴 czerwony (miga, 1,6 s) | `błąd` + dźwięk | tura przerwana błędem API / limitem |
| ⚪ sam tor | `bezczynny` / brak sesji | nic się nie dzieje |

Mierniki limitów świecą na czerwono, gdy zostało poniżej 15 %, niezależnie od
tego, które to okno.

To przepisanie wersji PowerShellowej (w `legacy/`) na C#, z drugim podejściem
do wyglądu opartym na zaktualizowanym prototypie w `design_handoff_token_widget/`.
Format plików stanu i konfiguracja zostały te same; zniknął serwer HTTP
i powiadomienia push — widget działa wyłącznie lokalnie.

## Struktura

```
src/
  ClaudeStatus.Core/      logika bez UI (używana przez hook i nakładkę)
    ClaudePaths           ścieżki (~/.claude, CLAUDE_CONFIG_DIR)
    Sessions/             pliki stanu, monitor w tle, kolejność, zmiany stanów
    Liveness/             "czy stan jest jeszcze prawdziwy" (transkrypt, procesy narzędzi)
    Usage/                limity 5 h / 7 dni: API Anthropic (token OAuth, tylko odczyt) + cache z ~/.claude.json
    Hooks/                payload hooka, filtr szumu, rejestracja w settings.json
  ClaudeStatus.Hook/      ClaudeStatusHook.exe — wywoływany przez hooki Claude Code
  ClaudeStatus.Overlay/   ClaudeStatusOverlay.exe — widget (WinForms, okno warstwowe)
    App/                  konfiguracja, log, pojedyncza instancja
    Interop/              UpdateLayeredWindow, szukanie okna edytora
    Rendering/            metryki projektu, paleta, fonty, brzeg/pasek/panel, cień, kompozycja
    Animation/            easing (cubic-bezier), morfing kształtu, niezależne fade'y, dojazd pasków
    Placement/            kotwice i krawędzie ekranu
    UI/                   okno, menu, dźwięki
Install.ps1               build + instalacja
legacy/                   poprzednia wersja PowerShellowa (serwer, ntfy, demo)
design_handoff_token_widget/  prototyp HTML — źródło wszystkich wymiarów i kolorów
```

Zasada podziału: **Core nie wie nic o rysowaniu**, a **renderery nic o stanie
aplikacji** — dostają gotowy `FrameInput` (czas, obraz sesji, wartości
animacji) i zwracają bitmapę. Dzięki temu klatka jest czystą funkcją wejścia.

## Jak to działa

Claude Code ma system **hooków** — komend uruchamianych przy zdarzeniach sesji.
`ClaudeStatusHook.exe --install` dopisuje do `~/.claude/settings.json` sześć
hooków, które przy każdym zdarzeniu zapisują mały plik JSON w
`~/.claude/status/<session_id>.json`:

| Zdarzenie Claude Code | Zapisywany stan |
|---|---|
| `SessionStart` | bezczynny |
| `UserPromptSubmit` | pracuje |
| `Notification` | czeka na Ciebie |
| `Stop` | gotowe |
| `StopFailure` | błąd |
| `SessionEnd` | (kasuje wpis) |

Hooki są `async`, więc nie spowalniają Claude'a. Nakładka obserwuje katalog
(`FileSystemWatcher` + odczyt co ~0,5 s w tle) i rysuje stan.

Claude Code nie ma zdarzenia „zgoda udzielona", więc po kliknięciu *Allow* stan
zostałby czerwony do końca tury. `LiveStateResolver` szuka dowodów, że tura
znowu leci: transkrypt urósł po alercie albo narzędzie odpalone po alercie
(proces-dziecko Claude Code, młodszy od alertu, widziany dwa razy) nadal działa.

## Limity 5 h i 7 dni

Nakładka pyta o nie **co 5 minut** (menu → *Odświeżaj limity*: co minutę,
5, 10, 15, 30 minut) ten sam endpoint, z którego korzysta `/usage`
w Claude Code i panel „Account & Usage" w VS Code
(`GET https://api.anthropic.com/api/oauth/usage`), tokenem OAuth z
`~/.claude/.credentials.json`. Częstsze pytanie potrafi skończyć się
`HTTP 429` na dłuższą chwilę, więc domyślne 5 minut jest tu celowo
zachowawcze - od ręki i tak można pobrać kołową strzałką w panelu. Zasady:

- token jest **tylko czytany** — nakładka nigdy go nie odświeża (rotacja refresh
  tokena zepsułaby sesję CLI) ani nie zapisuje; gdy wygaśnie, czeka, aż Claude
  Code sam podmieni plik;
- token idzie wyłącznie do `api.anthropic.com`, w nagłówku `Authorization`;
- wyłączenie punktu **Limity 5 h / 7 dni** w menu wyłącza też cały ruch sieciowy;
- po błędzie kolejna próba za 2 min, a przy rzadszym ustawieniu - dopiero po
  wybranym odstępie (błąd nigdy nie przyspiesza pytania); gdy API milczy dłużej
  niż godzinę, dane dostają znak `?`, po dobie znikają.

Źródłem awaryjnym jest `cachedUsageUtilization` z `~/.claude.json` — ale ten
wpis odświeża tylko terminalowe TUI `claude` (sesje w VS Code go nie ruszają),
więc sam w sobie bywa wielogodzinny. Nakładka bierze zawsze nowsze z obu.
Po minięciu terminu zerowania okno pokazuje 0 % zużycia. Procenty i paski
używają tej samej konwencji co `/usage` i VS Code: **ile zużyto**, pasek
rośnie razem ze zużyciem.

Na obu miernikach (5 h i 7 dni) biała kreska pokazuje, **ile z okna czasowego
już minęło** - gdy wypełnienie zużyciem jest przed kreską, palisz limit
wolniej, niż leci czas; gdy za nią, szybciej.

Panel pokazuje też pod miernikami e-mail zalogowanego konta
(`oauthAccount.emailAddress` z `~/.claude.json`) i wiek ostatniego pobrania
limitów ("odświeżono 2m temu") - obie wartości znikają razem z limitami,
gdy są wyłączone w menu. Obok wieku siedzi **kołowa strzałka**: klik pobiera
limity od razu, nie czekając na kolejną minutę. Ikona kręci się, dopóki nie
przyjdą nowe dane (i przestaje po 8 s, gdy API milczy).

## Instalacja

Wymagania: Windows 10/11, [.NET SDK 10](https://dotnet.microsoft.com/download)
(do zbudowania; do samego działania wystarczy .NET Desktop Runtime 10).

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Autostart
```

Instalator buduje oba exe (`dotnet publish`), kopiuje je do
`%USERPROFILE%\.claude\status-overlay`, dopisuje hooki (robiąc kopię
`settings.json.bak-<data>` i **usuwając hooki starej wersji PowerShellowej**),
opcjonalnie dodaje skrót do autostartu i uruchamia nakładkę. Jest idempotentny.

Potem **zamknij i otwórz ponownie sesje Claude Code** — hooki ładują się przy
starcie sesji.

Deinstalacja (z katalogu repozytorium):

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Uninstall
```

Usuwa hooki z `settings.json` (zostawiając Twoje własne), skrót z autostartu
i katalog z plikami.

## Obsługa

- **Najechanie myszką** — rozwija brzeg w pasek (podgląd z licznikami), zjazd
  kursorem go zwija. Po wyłączeniu „Rozwijaj po najechaniu" najechanie nic
  nie robi - liczy się tylko klik.
- **Lewy przycisk + przeciągnięcie** — przesuwanie (pozycja jest zapamiętywana,
  kotwica przełącza się na „Dowolna"). Po włączeniu „Zablokuj przesuwanie"
  przeciągnięcie nie robi nic - ani nie rusza widgetu, ani nie liczy się jako
  klik; reszta (najechanie, klik, menu) działa normalnie.
- **Lewy przycisk, klik** — przełącza pełny panel z listą sesji; kolejny klik
  albo **klik gdziekolwiek indziej na ekranie** go zamyka (powrót do paska,
  jeśli kursor wciąż jest na widgecie, inaczej do brzegu) - jak każdy popover.
  Klik w konkretny wiersz panelu aktywuje okno edytora tej sesji, a klik
  w kołową strzałkę przy „odświeżono ... temu" pobiera limity od ręki.
- **Prawy przycisk** — menu: pozycja (8 krawędzi/rogów + dowolna), odklejona od
  krawędzi, zablokuj przesuwanie, dźwięki, rozwijanie po najechaniu,
  limity 5 h / 7 dni, jak często je odświeżać (co 1 / 5 / 10 / 15 / 30 minut -
  wyszarzone przy wyłączonych limitach), zawsze na wierzchu, wyczyść zakończone
  sesje, zamknij.

Ustawienia: `%USERPROFILE%\.claude\status-overlay.config.json`
(`X`, `Y`, `Anchor`, `Detached`, `Locked`, `Sound`, `TopMost`, `Hover`,
`Usage`, `UsageIntervalSeconds`) — zgodne ze starą wersją (`Locked`
i `UsageIntervalSeconds` doszły później: brak w pliku = odblokowane i 5 minut).
Błędy lądują w `%USERPROFILE%\.claude\status-overlay.log`.

Wiersz poleceń:

```
ClaudeStatusOverlay.exe          uruchom (jedna instancja naraz)
ClaudeStatusOverlay.exe --exit   zamknij działającą nakładkę
ClaudeStatusHook.exe --state <idle|working|done|attention|error|end> [--note "..."]
ClaudeStatusHook.exe --install | --uninstall
```

## Płynność

- Wszystkie animacje są sterowane zegarem (`Stopwatch`), nie licznikiem
  klatek — morfing kształtu trwa dokładnie 380 ms z krzywą
  `cubic-bezier(.22,1,.36,1)` niezależnie od obciążenia. Trzy poziomy
  (brzeg/pasek/panel) przenikają niezależnie, każdy własnym fade'em
  (`FadeAnimation`), a kształt powłoki (`ShapeMorph`) po prostu animuje
  bieżące piksele do nowego celu - tak jak CSS `transition: width, height,
  border-radius` - więc przełączenie w trakcie animacji nie powoduje skoku.
- Przy ciągłej animacji (morfing, obrót łuku, miganie, dojazd pasków) pętla
  tyka co 15 ms (~64 fps). Poza tym tylko sonduje kursor co 50 ms, a rysuje
  dopiero, gdy coś się zmieniło (sekunda zegara - tylko gdy widoczny jest
  tekst z wiekiem, nowe dane, wiersz pod kursorem) — w spoczynku ~0–1 % CPU.
- Pozycja, rozmiar i obraz okna zmieniają się w jednym wywołaniu
  `UpdateLayeredWindow`, więc panel rosnący „w górę" (kotwica dolna) nie skacze.
- Odczyt dysku i odpytywanie procesów dzieją się w wątku monitora; wątek UI
  bierze tylko gotowy, niezmienny `SessionSnapshot`.
- Cień jest liczony jak w przeglądarce (`0 18px 40px -14px`: kształt
  zmniejszony o 14 px, przesunięty o 18 px, Gauss σ = 20 px jako 3× box blur
  na kanale alfa), raz na kształt i trzymany w cache; fonty, pędzle i bitmapy
  warstw są wielokrotnego użytku, odtwarzane tylko przy zmianie DPI
  (per-monitor v2).

## Demo

Skrypt `legacy/Demo-ClaudeStatus.ps1` pisze te same pliki stanu, więc działa
z nową nakładką:

```powershell
powershell -ExecutionPolicy Bypass -File .\legacy\Demo-ClaudeStatus.ps1 -NoOverlay
```

(`-NoOverlay`, bo skrypt próbowałby uruchomić starą wersję; nowa musi już działać).

## Diagnostyka

**Sprawdzenie, czy hook zapisuje:**

```powershell
'{"session_id":"test","cwd":"C:\\repo\\Testowy"}' |
  & "$env:USERPROFILE\.claude\status-overlay\ClaudeStatusHook.exe" --state working
Get-ChildItem "$env:USERPROFILE\.claude\status"
```

Powinien pojawić się `test.json`, a na pastylce kręcący się bursztynowy łuk.
Posprzątanie: `'{"session_id":"test"}' | & ...\ClaudeStatusHook.exe --state end`.

**Pusty okrąg mimo pracującego Claude'a** — sesja otwarta przed instalacją
hooków. Zamknij ją i otwórz nową.

**Za dużo czerwonych alertów** — `Notification` odpala się przy różnych typach
powiadomień; filtr jest w `Core/Hooks/HookProcessor.cs` (`NotificationFilter`).
