# Claude Status Overlay dla Windows

Czarna pastylka w stylu notcha (142×32 px), przyklejona do krawędzi ekranu i
zawsze na wierzchu. Pierścieniem mówi, co robi Claude Code — czy jeszcze
pracuje, czy już skończył — a dwoma mikro-paskami, ile zostało limitu 5 h
i 7 dni.

| Znacznik | Stan | Kiedy |
|---|---|---|
| 🟠 bursztynowy łuk (kręci się, pulsuje) | `pracuje` | wysłałeś prompt, Claude pracuje |
| 🟢 zielony pierścień | `gotowe` | Claude skończył odpowiedź |
| 🔴 czerwony pierścień (miga + dźwięk) | `czeka na Ciebie` | prośba o zgodę na narzędzie / pytanie |
| 🟣 fioletowy pierścień (miga) | `błąd` | tura przerwana błędem API / limitem |
| ⚪ sam tor pierścienia | `bezczynny` / brak sesji | nic się nie dzieje |

Obok pierścienia jest tylko czas od ostatniej zmiany stanu — od razu widać, że
coś wisi 8 minut. Przy kilku sesjach pastylka pokazuje najważniejszy stan
(czeka na Ciebie → pracuje → gotowe → bezczynny) i licznik `×2`, ile sesji
jest w tym stanie.

**Najedź myszką**, a pastylka morfuje (380 ms) w panel 392 px: nagłówek
`SESJE · N aktywne`, lista sesji (kropka stanu, projekt, stan, wiek) i pod
separatorem dwa mierniki limitów. Zjedź kursorem — wraca do pastylki.
Rozwijanie po najechaniu można wyłączyć w menu; wtedy rozwija i zwija klik.

## Limity 5 h i 7 dni

W zwiniętej pastylce, obok czasu, są dwa mikro-paski (26×3 px): niebieski —
**dostępny limit okna 5 h**, zielony — okna 7 dni. Pełny pasek to pełny
limit; pasek topnieje razem z limitem.

Po rozwinięciu, pod listą sesji, są dwa wiersze z torem, procentem dostępnego
limitu i godziną zerowania licznika:

```
5 h    ▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔                 45% wolne
                                          reset 13:23
7 dni  ▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔             58% wolne
                                          reset śr 11:23
```

Dane pochodzą z `~/.claude.json` (klucz `cachedUsageUtilization`) — to ten sam
odczyt, z którego korzysta `/usage` w Claude Code. Claude Code sam odświeża ten
wpis w trakcie pracy, więc **nakładka niczego nie wysyła do sieci i nie dotyka
tokenów** — czyta gotowy plik, i tylko wtedy, gdy ten się zmienił. Jeśli termin
zerowania okna już minął, nakładka pokazuje pełny limit, nie stary odczyt.
Gdy w pliku nie ma jeszcze danych (świeża instalacja) albo są starsze niż doba,
paski i wiersze po prostu się nie pokazują. Całość wyłącza punkt
**Limity 5 h / 7 dni** w menu.

## Jak to działa

Claude Code ma system **hooków** — komend uruchamianych przy zdarzeniach sesji.
Instalator dopisuje do `~/.claude/settings.json` sześć hooków, które przy każdym
zdarzeniu zapisują mały plik JSON w `%USERPROFILE%\.claude\status\<session_id>.json`:

| Zdarzenie Claude Code | Zapisywany stan |
|---|---|
| `SessionStart` | bezczynny |
| `UserPromptSubmit` | pracuje |
| `Notification` | czeka na Ciebie |
| `Stop` | gotowe |
| `StopFailure` | błąd |
| `SessionEnd` | (kasuje wpis) |

Nakładka co ~0,5 s czyta ten katalog i rysuje znacznik stanu. Hooki są ustawione jako
`async`, więc nie spowalniają Claude'a, i odpalają się raz na turę (nie przy
każdym wywołaniu narzędzia) — narzut jest praktycznie zerowy.

Claude Code nie ma zdarzenia „zgoda udzielona" (kolejność to `PreToolUse` →
`PermissionRequest` → pytanie → narzędzie), więc po kliknięciu *Allow* żaden hook
się nie odpala i stan zostałby czerwony do końca tury. Dlatego wspólna dla
nakładki i serwera biblioteka `ClaudeStatusState.ps1` szuka dowodów, że tura
znowu leci, i wtedy pokazuje „pracuje":

1. **transkrypt urósł po alercie** — `~/.claude/projects/<projekt>/<session_id>.jsonl`
   dostaje wpis przy każdej odpowiedzi, wyniku narzędzia i odmowie;
2. **narzędzie odpalone po alercie nadal działa** — to przypadek „zgoda na długi
   build": przez kilka minut do transkryptu nic nie trafia, ale proces narzędzia
   jest dzieckiem procesu Claude Code (`~/.claude/sessions/<pid>.json` mapuje
   sesję na pid). Proces musi być młodszy od alertu i przeżyć dwa sprawdzenia
   (>1,5 s), żeby nie liczyć krótkich procesów hooków ani serwerów MCP.

Kolejna prawdziwa prośba o zgodę zapisuje nowy znacznik czasu, więc znów zapala
się czerwone. Jedyna luka: pytanie o zgodę na narzędzie, które nie tworzy procesu
(np. bardzo długi `WebFetch`) — czerwone zgaśnie dopiero, gdy Claude coś dopisze.

Hooki z `~/.claude/settings.json` obowiązują wszystkie sesje Claude Code,
także te w rozszerzeniu VS Code.

## Instalacja

1. Rozpakuj cały ZIP do jednego katalogu (np. `C:\Users\<Ty>\Downloads\claude-status-overlay`).
2. Otwórz PowerShell w tym katalogu i uruchom:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-ClaudeStatusOverlay.ps1 -Autostart -WithServer
```

`-Autostart` dodaje skróty do autostartu Windows, `-WithServer` włącza podgląd
w sieci lokalnej (telefon). Pomiń je, jeśli wolisz uruchamiać ręcznie.
Uruchomiony jako administrator przyjmie też `-OpenFirewall`, który od razu
przepuszcza port 8787 w zaporze.

3. **Zamknij i otwórz ponownie sesje Claude Code w VS Code** — hooki ładują się
   przy starcie sesji.

Instalator:

- kopiuje pliki do `%USERPROFILE%\.claude\status-overlay`,
- robi kopię zapasową `settings.json` (`settings.json.bak-<data>`) i dopisuje hooki,
  **nie ruszając** istniejących ustawień ani innych hooków,
- jest idempotentny — możesz go uruchomić ponownie, nie zdubluje wpisów,
- uruchamia nakładkę.

## Podgląd na telefonie (sieć lokalna)

```powershell
powershell -ExecutionPolicy Bypass -File .\ClaudeStatusServer.ps1
```

albo kliknij **`Serwer.cmd`**. W konsoli pojawią się adresy — na telefonie w tej
samej sieci Wi-Fi otwierasz `http://<ip-komputera>:8787/`.

Strona jest zrobiona pod telefon: ciemna, duże karty, czerwony pulsujący baner
gdy któraś sesja czeka na decyzję, odświeżanie co 2 s. Tytuł karty przeglądarki
zmienia się na `(!) Claude — czeka`, więc widać to nawet z listy kart.
Przycisk „Dźwięk w przeglądarce" włącza sygnał przy zmianie stanu (przydatne,
gdy trzymasz stronę otwartą na drugim monitorze).

Serwer stoi na `TcpListener`, więc **nie wymaga uprawnień administratora**
(`HttpListener` wymagałby rezerwacji URL-a przez `netsh`). Przy pierwszym
uruchomieniu Windows może zapytać o zaporę — zezwól dla sieci prywatnych.
Jeśli okno nie wyskoczy, a telefon nie widzi strony, wykonaj raz jako
administrator:

```
netsh advfirewall firewall add rule name="Claude Status" dir=in action=allow protocol=TCP localport=8787
```

Przełączniki: `-Port 9000`, `-LocalOnly` (tylko localhost), `-Quiet`.
Endpoint `GET /api/status` zwraca surowy JSON, gdyby chciał go ktoś podpiąć
gdzie indziej.

## Powiadomienia push na telefon

Nakładka nie ma własnego kanału push, więc korzystamy z **ntfy** — darmowej
usługi bez konta.

```powershell
powershell -ExecutionPolicy Bypass -File .\Setup-Notifications.ps1
```

Skrypt losuje trudny do zgadnięcia temat, zapisuje konfigurację i od razu
wysyła powiadomienie testowe. Potem na telefonie:

1. zainstaluj aplikację **ntfy** (Google Play / App Store),
2. dodaj subskrypcję i wpisz temat wypisany przez skrypt.

Kliknięcie powiadomienia otwiera podgląd w sieci lokalnej (adres jest
wykrywany automatycznie).

**Kiedy telefon zadzwoni** (ustawienie domyślne):

| Sytuacja | Push |
|---|---|
| Claude prosi o zgodę na narzędzie | zawsze, priorytet wysoki |
| Tura padła na błędzie / limicie | zawsze, priorytet wysoki |
| Claude skończył | tylko gdy tura trwała dłużej niż 60 s |
| Start pracy, bezczynność | nigdy |

Zmiana progu: `-DoneAfterSeconds 120`, całkowite wyłączenie „gotowe":
`-DoneAfterSeconds -1`. Konfiguracja leży w
`%USERPROFILE%\.claude\status-notify.config.json` i można ją edytować ręcznie
(`notifyStates`, `notifyDoneAfterSeconds`, `webUrl`).

Inne przełączniki: `-Test` (wyślij testowe jeszcze raz), `-Disable`
(wyłącz, zachowując konfigurację), `-Topic wlasny-temat`.

> **Prywatność:** publiczny `ntfy.sh` nie wymaga konta, ale każdy, kto zna nazwę
> tematu, może czytać te powiadomienia — a w treści są nazwy Twoich projektów.
> Dlatego temat jest losowy (12 znaków). Jeśli to za mało, postaw własny serwer
> ntfy i uruchom `Setup-Notifications.ps1 -Server https://ntfy.twojadomena.pl -Token tk_xxx`.

## Demo (do pokazania innym)

Symuluje trzy równoległe sesje Claude Code — nie potrzeba ani zainstalowanych
hooków, ani uruchomionego Claude'a. Wystarczy kliknąć **`Demo.cmd`** albo:

```powershell
powershell -ExecutionPolicy Bypass -File .\Demo-ClaudeStatus.ps1
```

Demo samo uruchamia nakładkę (jeśli jeszcze nie działa) i przechodzi przez
pełny scenariusz: dwie sesje ruszają, jedna prosi o zgodę na `Bash`, druga
kończy, trzecia dostaje błąd API i jest ponawiana, na końcu sesje się zamykają.
W konsoli lecą komentarze do każdego kroku, więc jest co czytać na głos:

```
  10:14:22  working    OptiMES                     Wysyłasz prompt — Claude rusza do pracy
  10:14:26  attention  OptiMES                     OptiMES prosi o zgodę na uruchomienie testów
  10:14:31  working    OptiMES                     Klikasz "Allow" — praca leci dalej
```

Przełączniki:

| Przełącznik | Efekt |
|---|---|
| `-Speed 2` | dwa razy szybciej (`-Speed 0.5` — wolniej) |
| `-Once` | jeden przebieg zamiast pętli |
| `-WithServer` | uruchom też podgląd sieciowy (pokaz na telefonie) |
| `-Notify` | wysyłaj przy tym prawdziwe powiadomienia push |
| `-NoOverlay` | nie uruchamiaj nakładki automatycznie |
| `-Cleanup` | tylko posprzątaj pliki demo i wyjdź |

Na pokaz przed ludźmi najlepiej działa:

```powershell
.\Demo-ClaudeStatus.ps1 -WithServer -Notify
```

— pasek na ekranie, strona na telefonie i realne powiadomienia naraz.
(W demie push idzie przy każdym „gotowe", żeby było co pokazywać — próg 60 s
obowiązuje tylko prawdziwe sesje.)

Zatrzymanie: dowolny klawisz. Sesje demo mają identyfikatory z prefiksem
`demo-`, są trzymane osobno od prawdziwych i kasowane przy wyjściu (także po
Ctrl+C). Jeśli kiedyś zostaną, wyczyść je `-Cleanup`.

## Obsługa

- **Najechanie myszką** — rozwija pastylkę w panel sesji, zjazd kursorem ją zwija.
  Po wyłączeniu „Rozwijaj po najechaniu" panel otwiera i zamyka klik.
- **Lewy przycisk + przeciągnięcie** — przesuwanie pastylki (pozycja jest zapamiętywana).
- **Lewy przycisk, klik** — próbuje aktywować okno edytora, którego tytuł zawiera
  nazwę katalogu projektu (na pastylce: najważniejsza sesja, w panelu: klikniętą;
  wiersz pod kursorem jest podświetlony).
- **Prawy przycisk** — menu: pozycja, odklejona od krawędzi, dźwięki, rozwijanie
  po najechaniu, limity 5 h / 7 dni, zawsze na wierzchu, wyczyść zakończone
  sesje, zamknij.
- **Pozycja** — podmenu z krawędziami ekranu (góra po lewej / na środku / po
  prawej, lewa i prawa krawędź, dół po lewej / na środku / po prawej) albo
  „Dowolna". Przyklejona pastylka siedzi na samej krawędzi z płaskim bokiem
  od strony ekranu (jak notch) i trzyma się jej także po rozwinięciu — na dole
  panel rośnie w górę, po prawej w lewo. Kotwica dotyczy monitora, na którym
  pastylka aktualnie jest, i omija pasek zadań. Przeciągnięcie pastylki
  przełącza z powrotem na pozycję dowolną (wszystkie rogi zaokrąglone).
- **Odklejona od krawędzi** — odsuwa pastylkę o 18 px od krawędzi i zaokrągla
  wszystkie rogi.

Ustawienia leżą w `%USERPROFILE%\.claude\status-overlay.config.json`
(`X`, `Y`, `Anchor`, `Detached`, `Sound`, `TopMost`, `Hover`, `Usage`) — możesz je
edytować ręcznie. Wymiary są stałe wg projektu (pastylka 142×32, panel 392 px
szerokości, wysokość zależy od liczby sesji) i skalują się z DPI ekranu.
Błędy pętli rysowania lądują w `%USERPROFILE%\.claude\status-overlay.log`.

Wygląd (kolory, typografia, odstępy, animacje) odtwarza projekt z
`design_handoff_token_widget/` — prototyp HTML otwiera się w przeglądarce.
Nakładka używa IBM Plex Sans / IBM Plex Mono, jeśli są zainstalowane; w
przeciwnym razie Segoe UI i Cascadia Mono.

## Ręczne uruchomienie

```powershell
wscript "%USERPROFILE%\.claude\status-overlay\Start-Overlay.vbs"
```

(uruchamia bez okna konsoli; działa tylko jedna instancja naraz)

## Deinstalacja

```powershell
powershell -ExecutionPolicy Bypass -File "%USERPROFILE%\.claude\status-overlay\Install-ClaudeStatusOverlay.ps1" -Uninstall
```

Usuwa hooki z `settings.json` (zostawiając Twoje własne), skrót z autostartu
i katalog z plikami.

## Diagnostyka

**Nakładka pokazuje pusty okrąg mimo pracującego Claude'a** —
sesja została otwarta przed instalacją hooków. Zamknij ją i otwórz nową.

**Sprawdzenie, czy hook w ogóle zapisuje:**

```powershell
'{"session_id":"test","cwd":"C:\\repo\\Testowy"}' |
  powershell -NoProfile -ExecutionPolicy Bypass `
    -File "$env:USERPROFILE\.claude\status-overlay\claude-status-hook.ps1" -State working

Get-ChildItem "$env:USERPROFILE\.claude\status"
```

Powinien pojawić się `test.json`, a na pastylce kręcący się bursztynowy łuk (po najechaniu: wiersz „Testowy").

**Wszystkie sesje zlewają się w jedną pozycję „Claude"** — hook nie dostał JSON-a
na stdin i użył identyfikatora zastępczego. Wtedy w `settings.json` zamień formę
`command` + `args` na jedną linijkę:

```json
"command": "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"C:\\Users\\<Ty>\\.claude\\status-overlay\\claude-status-hook.ps1\" -State working"
```

(bez pola `args` — wtedy komenda idzie przez powłokę, która przekazuje stdin).

**Za dużo czerwonych alertów** — `Notification` odpala się przy różnych typach
powiadomień. Skrypt już odfiltrowuje logowanie i aktualizacje; kolejne typy
możesz dopisać do wyrażenia `$ignore` w `claude-status-hook.ps1`.

## Pliki

| Plik | Rola |
|---|---|
| `claude-status-hook.ps1` | wywoływany przez hooki, zapisuje stan sesji |
| `ClaudeStatusOverlay.ps1` | nakładka (okno warstwowe, pastylka w stylu notcha + panel) |
| `ClaudeStatusState.ps1` | wspólna logika „czy stan jest jeszcze prawdziwy" (transkrypt, procesy narzędzi) |
| `ClaudeStatusUsage.ps1` | odczyt limitów 5 h / 7 dni z `~/.claude.json` |
| `Start-Overlay.vbs` | uruchamia nakładkę bez okna konsoli |
| `Install-ClaudeStatusOverlay.ps1` | instalator / deinstalator |
| `ClaudeStatusServer.ps1` | serwer HTTP — podgląd na telefonie |
| `ClaudeStatusNotify.ps1` | wysyłka powiadomień push (ntfy) |
| `Setup-Notifications.ps1` | konfiguracja powiadomień |
| `Start-Server.vbs` | uruchamia serwer bez okna konsoli |
| `Serwer.cmd` | klikalny start serwera |
| `Demo-ClaudeStatus.ps1` | symulacja trzech sesji do pokazu |
| `Demo.cmd` | klikalny start dema |

Wymagania: Windows z PowerShell 5.1 (czyli każdy Windows 10/11) — nic nie trzeba
doinstalowywać.
