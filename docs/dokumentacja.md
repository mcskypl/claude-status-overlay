# Claude Status Overlay (C# / .NET 10)

Czarna, zawsze widoczna linia przyklejona do krawędzi ekranu, która na dwa
sposoby mówi, co robi Claude Code:

| Poziom | Kiedy widać | Co pokazuje |
|---|---|---|
| **Brzeg** | zawsze | cienka linia (148×5 px) - proporcje limitów 5 h / 7 dni jako dwa paski rosnące ku barwnemu znacznikowi stanu, który jest granicą między nimi (paski nie spotykają się na środku) |
| **Pasek** | po najechaniu kursorem (o ile włączone w menu) albo cały czas, gdy włączone „Zawsze rozwinięta" | mały pierścień stanu, wartość (czas albo nazwa stanu) i oba mierniki w linii z procentami |

Oba poziomy przenikają się płynnym morfingiem (380 ms, skracanym przy
zawracaniu); kotwica lewa/prawa krawędź obraca je do układu pionowego.

**Klik otwiera okno rozmowy** - jedno okno, nie panel obok. Limity 5 h / 7 dni
z terminami resetu siedzą w jego nagłówku, konto i ręczne odświeżanie w stopce.
Listy sesji nie ma: stan najważniejszej z nich pokazuje sama pastylka.

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
    Update/               wydania na GitHubie: sprawdzanie, pobranie instalatora, cicha aktualizacja
  ClaudeStatus.Hook/      ClaudeStatusHook.exe — wywoływany przez hooki Claude Code
  ClaudeStatus.Overlay/   ClaudeStatusOverlay.exe — widget (WinForms, okno warstwowe)
    App/                  konfiguracja, log, pojedyncza instancja
    Interop/              UpdateLayeredWindow, szukanie okna edytora
    Rendering/            metryki projektu, paleta, fonty, brzeg/pasek, cień, kompozycja
    Animation/            easing (cubic-bezier), morfing kształtu, niezależne fade'y, dojazd pasków
    Placement/            kotwice i krawędzie ekranu
    UI/                   okno, menu, dźwięki
installer/                skrypt Inno Setup (ClaudeStatusOverlay-Setup.exe)
tools/                    generator ikony, wydawanie wersji (tag + push)
Build-Release.ps1         instalator + paczka portable w publish\
Install.ps1               build + instalacja ze źródeł (bez instalatora)
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

### Sesja asystenta — bez pliku stanu

Asystent jest zwykłą sesją Claude Code, więc odpaliłby te same hooki. Dla niego
są **wyciszone**: nakładka startuje mostek ze zmienną `CLAUDE_STATUS_HOOK=off`,
a `ClaudeStatusHook.exe` z taką zmienną nie zapisuje nic. Zamiast pliku wiersz
na liście składa sama nakładka (`SessionMonitor.Companion`) z tego, co
przechodzi przez `AssistantEngine`: pytanie o zgodę, decyzja, koniec tury, błąd.

Powód jest ten sam, co wyżej, tylko ostrzejszy: przy asystencie decyzja o zgodzie
zapada **w oknie rozmowy**, więc żaden hook o niej nie wie, a ratunek z `LiveStateResolver`
nie zadziała, gdy tura kończy się na samej odmowie — wpis zostawał wtedy w stanie
„czeka na Ciebie" aż do wygaśnięcia po dobie i pastylka migała na niebiesko bez
powodu. Stan trzymany w pamięci ma komplet przejść, znika razem z aplikacją i nie
zostawia śmieci w `~/.claude/status`. Wpisy sprzed tej zmiany kasuje
`AssistantSession.PurgeStale` przy starcie.

Jeden wyjątek od dosłowności: „gotowe" to powiadomienie dla kogoś, kto nie patrzy.
Gdy tura kończy się przy otwartym oknie rozmowy, stan idzie wprost na „bezczynny" —
inaczej pastylka świeciłaby na zielono długo po przeczytaniu odpowiedzi.

## Limity 5 h i 7 dni

Nakładka pyta o nie **co 5 minut** (menu → *Odświeżaj limity*: co minutę,
5, 10, 15, 30 minut) ten sam endpoint, z którego korzysta `/usage`
w Claude Code i panel „Account & Usage" w VS Code
(`GET https://api.anthropic.com/api/oauth/usage`), tokenem OAuth z
`~/.claude/.credentials.json`. Częstsze pytanie potrafi skończyć się
`HTTP 429` na dłuższą chwilę, więc domyślne 5 minut jest tu celowo
zachowawcze - od ręki i tak można pobrać kołową strzałką w stopce rozmowy. Zasady:

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

Stopka okna rozmowy pokazuje e-mail zalogowanego konta
(`oauthAccount.emailAddress` z `~/.claude.json`) i wiek ostatniego pobrania
limitów ("odświeżono 2m temu") - obie wartości znikają razem z limitami,
gdy są wyłączone w menu. Obok wieku siedzi **kołowa strzałka**: klik pobiera
limity od razu, nie czekając na kolejną minutę. Ikona kręci się, dopóki nie
przyjdą nowe dane (i przestaje po 8 s, gdy API milczy).

## Instalacja

Pobierz `ClaudeStatusOverlay-Setup-<wersja>.exe` z
[wydań](https://github.com/mcskypl/claude-status-overlay/releases) i uruchom. Kreator ma dwa
kliknięcia: pyta tylko o autostart (i o usunięcie starej wersji PowerShellowej,
jeśli ją znajdzie). Nie wymaga uprawnień administratora.

Co robi:

- kopiuje pliki do `%LOCALAPPDATA%\Programs\Claude Status Overlay`,
- dopisuje hooki do `~/.claude/settings.json` (robiąc kopię
  `settings.json.bak-<data>` i **usuwając hooki starej wersji PowerShellowej**),
- opcjonalnie dodaje skrót do autostartu,
- uruchamia nakładkę,
- gdy brakuje **.NET Desktop Runtime 10**, proponuje pobranie go z microsoft.com
  i instaluje po cichu.

Potem **zamknij i otwórz ponownie sesje Claude Code** — hooki ładują się przy
starcie sesji.

Deinstalacja: *Ustawienia → Aplikacje → Claude Status Overlay → Odinstaluj*
(albo `unins000.exe` z katalogu instalacji). Deinstalator wypisuje hooki
z `settings.json`, zostawiając Twoje własne.

### Bez instalatora

W wydaniu jest też `ClaudeStatusOverlay-<wersja>-portable.zip` - rozpakuj
gdziekolwiek, uruchom `ClaudeStatusHook.exe --install` (hooki) i
`ClaudeStatusOverlay.exe`.

Ze źródeł (wymaga [.NET SDK 10](https://dotnet.microsoft.com/download)):

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Autostart   # build + instalacja do ~/.claude/status-overlay
powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1        # instalator + zip w publish\
```

## Aktualizacje

Nakładka raz na dobę pyta publiczne API GitHuba o najnowsze wydanie
(`/repos/mcskypl/claude-status-overlay/releases/latest`) - bez tokena, bez wysyłania czegokolwiek
o Tobie. Gdy wersja z tagu jest wyższa niż ta, która chodzi:

- w stopce okna rozmowy pojawia się wiersz **„nowa wersja 2.1.0 - zaktualizuj"**,
- w menu, na samej górze, pogrubione **„Zaktualizuj do 2.1.0"**.

Klik pobiera instalator z załączników wydania (z paskiem postępu w tym wierszu)
i uruchamia go po cichu: instalator zamyka nakładkę, podmienia pliki, odświeża
hooki i uruchamia nową wersję. Ustawienia i pozycja zostają.

Menu → *Aktualizacje* ma „Sprawdź teraz", przełącznik codziennego sprawdzania
(wyłączenie = zero ruchu w sieci) i numer bieżącej wersji.

## Obsługa

- **Najechanie myszką** — rozwija brzeg w pasek (podgląd z licznikami), zjazd
  kursorem go zwija. Po wyłączeniu „Rozwijaj po najechaniu" najechanie nic
  nie robi - liczy się tylko klik.
- **„Zawsze rozwinięta"** — pastylka stoi w pasku na stałe, od uruchomienia
  nakładki, dokładnie tak jak po najechaniu, tylko że zjazd kursorem jej nie
  zwija (brzegu nie widać wcale). „Rozwijaj po najechaniu" zostaje nietknięte -
  kursor po prostu nie ma już czego rozwijać, a ustawienie wraca do gry po
  zwinięciu pastylki.
- **Lewy przycisk + przeciągnięcie** — przesuwanie (pozycja jest zapamiętywana,
  kotwica przełącza się na „Dowolna"). Po włączeniu „Zablokuj przesuwanie"
  przeciągnięcie nie robi nic - ani nie rusza widgetu, ani nie liczy się jako
  klik; reszta (najechanie, klik, menu) działa normalnie.
- **Lewy przycisk, klik** — otwiera okno rozmowy; kolejny klik albo **klik
  gdziekolwiek indziej na ekranie** je zamyka - jak każdy popover. Okno wyrasta
  z kształtu pastylki i tą samą drogą w nią wraca. To samo robi
  `Ctrl+Shift+Spacja`. Klik w kołową strzałkę w stopce pobiera limity od ręki.
- **Prawy przycisk** — menu: pozycja (8 krawędzi/rogów + dowolna), odklejona od
  krawędzi, zablokuj przesuwanie, dźwięki, rozwijanie po najechaniu, zawsze
  rozwinięta, limity 5 h / 7 dni, jak często je odświeżać (co 1 / 5 / 10 / 15 / 30
  minut - wyszarzone przy wyłączonych limitach), zawsze na wierzchu, wyczyść
  zakończone sesje, aktualizacje, zamknij. Gdy czeka nowa wersja, na górze menu
  dochodzi pogrubione „Zaktualizuj do ...".

Ustawienia: `%USERPROFILE%\.claude\status-overlay.config.json`
(`X`, `Y`, `Anchor`, `Detached`, `Locked`, `Sound`, `TopMost`, `Hover`,
`AlwaysExpanded`, `Usage`, `UsageIntervalSeconds`, `Updates`) — zgodne ze starą
wersją (klucze dopisane później mają sensowne domyślne: odblokowane, 5 minut,
aktualizacje włączone, pastylka zwinięta). Błędy lądują
w `%USERPROFILE%\.claude\status-overlay.log`.

Wiersz poleceń:

```
ClaudeStatusOverlay.exe          uruchom (jedna instancja naraz)
ClaudeStatusOverlay.exe --exit   zamknij działającą nakładkę
ClaudeStatusHook.exe --state <idle|working|done|attention|error|end> [--note "..."]
ClaudeStatusHook.exe --install | --uninstall
```

Zmienna `CLAUDE_STATUS_HOOK=off` wycisza hook w całym poddrzewie procesów — tak
nakładka startuje mostek asystenta, żeby jego sesja nie pisała plików stanu.

## Płynność

- Wszystkie animacje są sterowane zegarem (`Stopwatch`), nie licznikiem
  klatek — morfing kształtu trwa 380 ms z krzywą `cubic-bezier(.22,1,.36,1)`
  niezależnie od obciążenia. Oba poziomy przenikają niezależnie, każdy własnym
  fade'em (`FadeAnimation`), a kształt powłoki (`ShapeMorph`) animuje bieżące
  piksele do nowego celu - tak jak CSS `transition: width, height,
  border-radius` - więc przełączenie w trakcie animacji nie powoduje skoku.
  **Zawrócenie skraca czas proporcjonalnie do pozostałej drogi** (z dolną
  granicą 35 % pełnego czasu): zjazd kursorem tuż po najeździe wraca szybko,
  zamiast mielić pełne 380 ms na dziesiątą część dystansu.
- Rytm klatek nadaje `DwmFlush` na osobnym wątku, nie `WM_TIMER` (ten ma ziarno
  15,6 ms i najniższy priorytet, przez co odstępy wychodziły 15/31 ms na
  przemian). Pełny ruch idzie w rytmie ekranu, a sam oddech znacznika stanu -
  rzadziej (33 ms), bo na kilku pikselach nikt nie odróżni 30 klatek od 83.
- Najechanie i zjechanie kursorem obsługuje zdarzenie (`WM_MOUSEMOVE` /
  `WM_MOUSELEAVE`), a nie samo odpytywanie - to ostatnie zostaje jako siatka
  bezpieczeństwa na wypadek zgubionego `WM_MOUSELEAVE`.
- Powierzchnia klatki to jedna trwała sekcja DIB (`DibSurface`): GDI+ rysuje
  wprost w pamięci, którą `UpdateLayeredWindow` czyta bez kopii. Wcześniej każda
  klatka robiła `GetHbitmap`, czyli nową sekcję i kopię całej powierzchni -
  **zmierzone 12,3 % → 3,4 % rdzenia** przy pulsującej sesji.
- Pozycja, rozmiar i obraz okna zmieniają się w jednym wywołaniu
  `UpdateLayeredWindow`, więc pastylka rosnąca „w górę" (kotwica dolna) nie skacze.
- Odczyt dysku i odpytywanie procesów dzieją się w wątku monitora; wątek UI
  bierze tylko gotowy, niezmienny `SessionSnapshot`.
- Cień jest liczony jak w przeglądarce (`0 18px 40px -14px`: kształt
  zmniejszony o 14 px, przesunięty o 18 px, Gauss σ = 20 px jako 3× box blur
  na kanale alfa), raz na kształt i trzymany w cache; fonty, pędzle i bitmapy
  warstw są wielokrotnego użytku, odtwarzane tylko przy zmianie DPI
  (per-monitor v2).

## Wydawanie nowej wersji

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Publish-Version.ps1 -Version 2.1.0
```

Skrypt podbija `<Version>` w `Directory.Build.props`, robi commit `wydanie 2.1.0`,
tag `v2.1.0` i wysyła jedno i drugie. Resztę robi workflow
[`release.yml`](../.github/workflows/release.yml) na GitHubie: buduje instalator
i paczkę portable, sprawdza, czy tag zgadza się z wersją w repozytorium,
i tworzy wydanie z załącznikami. Nakładki zauważą je przy najbliższym
sprawdzeniu (raz na dobę) albo po „Sprawdź teraz" z menu.

Lokalnie to samo bez publikowania: `.\Build-Release.ps1` (wynik w `publish\`).
Ikonę aplikacji generuje `tools\New-AppIcon.ps1` - jest w repozytorium gotowa,
skrypt przydaje się tylko przy zmianie wyglądu.

Repozytorium, z którego nakładka bierze aktualizacje, siedzi w jednym miejscu:
`UpdateSource.Repo` w [`src/ClaudeStatus.Core/Update/UpdateSource.cs`](../src/ClaudeStatus.Core/Update/UpdateSource.cs).
Po sforkowaniu wystarczy podmienić tam „właściciel/nazwa"; wartość zastępcza
z `OWNER` wyłącza sprawdzanie aktualizacji i wtedy nakładka nie rusza w tej
sprawie sieci.

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
