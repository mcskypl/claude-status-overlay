# Handoff: Okno asystenta — poziom 4 widgetu „Token Status Widget (notch)"

## Przegląd

Czwarty poziom istniejącej pastylki przy krawędzi ekranu: okno asystenta AI
obsługiwane tekstem, z pełnym dostępem do narzędzi firmowych (HubSpot, Clockify,
Azure DevOps, monitoring aplikacji, baza SQL). Otwiera je globalny skrót
`Ctrl+Shift+Spacja` albo menu pastylki.

Poziom 4 to **dwa niezależne okna obok siebie**, oddzielone przerwą 8 px:
okno sesji i limitów (392 × 720) po lewej i okno rozmowy (640 × 720) po prawej.
Okno rozmowy można schować i rozsunąć; wtedy zostaje samo okno sesji.

Docelowe środowisko: **WebView2 wewnątrz aplikacji .NET na Windows**.
Bez zasobów zewnętrznych — żadnego CDN, żadnych fontów z sieci. Tylko motyw
ciemny, interfejs po polsku. Okno jest nieprzezroczyste; nie planuj efektów
wymagających półprzezroczystego tła za oknem.

## O plikach w tej paczce

Pliki w tej paczce są **referencją projektową wykonaną w HTML** — prototypem
pokazującym zamierzony wygląd i zachowanie, a nie kodem produkcyjnym do
skopiowania. Zadaniem jest **odtworzenie tych widoków w docelowym środowisku**
z użyciem jego własnych wzorców i bibliotek. Tutaj docelowym środowiskiem jest
WebView2 w .NET, więc warstwa prezentacji faktycznie będzie HTML/CSS — ale
strukturę, ładowanie zasobów i komunikację z hostem należy napisać zgodnie
z konwencjami istniejącej aplikacji, nie kopiując prototypu.

Prototyp korzysta z runtime'u `support.js` (renderowanie komponentu i
przełączniki stanów). W produkcji **nie jest potrzebny** — służy wyłącznie do
obejrzenia projektu w przeglądarce.

## Wierność

**Hi-fi.** Wszystkie kolory, rozmiary, wagi fontów, promienie, cienie, odstępy
i czasy animacji są ostateczne i podane co do wartości w `SPECYFIKACJA.md`.
Interfejs należy odtworzyć piksel w piksel. Treść demonstracyjna (nazwy sesji,
zapytania, wyniki narzędzi) jest przykładowa.

## Pliki

| Plik | Zawartość |
|---|---|
| `Okno asystenta.dc.html` | Prototyp — otwiera się w przeglądarce, u góry przełączniki 13 stanów, skali (100/125/150 %) i kotwic pastylki |
| `SPECYFIKACJA.md` | Pełna specyfikacja: wymiary, odstępy, typografia, kolory, promienie, cienie, czasy animacji, tokeny |
| `support.js` | Runtime prototypu (tylko do podglądu, nie do produkcji) |

`SPECYFIKACJA.md` jest dokumentem nadrzędnym — zawiera wszystkie wartości
potrzebne do implementacji bez zaglądania w kod prototypu. Poniżej tylko to,
czego w niej nie ma: mapa widoków, stan aplikacji i kontrakt z hostem .NET.

## Widoki (przełączniki w prototypie)

| # | Widok | Co pokazuje | Sekcja specyfikacji |
|---|---|---|---|
| 1 | Pusty | okno bez rozmowy: prompt z kursorem, podpowiedź, aktywny katalog | §6 |
| 2 | Pisanie | prompt wielolinijkowy, pasek skrótów, aktywny przycisk wysyłki | §5 |
| 3 | Odpowiedź w trakcie | strumień znak po znaku, kursor bursztynowy, „Pracuję…", przycisk Przerwij | §7a |
| 4 | Odpowiedź gotowa | markdown: nagłówek, akapit, lista, tabela, blok kodu z podświetleniem i kopiowaniem | §7b |
| 5 | Narzędzie | zwinięty wiersz + rozwinięcie z argumentami i wynikiem | §9 |
| 5b | Narzędzie pracuje | spinner bursztynowy, licznik czasu | §9 |
| 5c | Narzędzie · błąd | obrys czerwony, przyczyna, przycisk Ponów | §9 |
| 6 | Karta zgody | zatrzymana tura, trzy wyjścia: Zezwól raz / Zezwól zawsze / Odrzuć | §10 |
| 7 | Błąd silnika | zerwane połączenie, diagnostyka, Ponów turę | §11 |
| 8 | Kontekst | chip katalogu + popover z ostatnimi repozytoriami | §8 |
| 9 | Historia | nakładka z listą rozmów | §3 |
| 10 | Mikrofon | trzy stany na przyszłość (gotowy, nasłuchuje, przetwarza) | §12 |
| 11 | Przejście z pastylki | wzrost okien z kotwicy górnej, dolnej i bocznej | §13 |

Przełączniki są narzędziem prototypu. W produkcji te widoki to stany jednego
okna, nie osobne ekrany.

## Interakcje

**Klawiatura**
- `Ctrl+Shift+Spacja` — pokaż / ukryj okno rozmowy (skrót globalny, rejestrowany po stronie .NET)
- `Enter` — wyślij
- `Shift+Enter` — nowa linia
- `Esc` — schowaj okno; w trakcie odpowiedzi: przerwij turę; przy karcie zgody: Odrzuć
- `Enter` przy karcie zgody — Zezwól raz

**Kliknięcia**
- chip kontekstu → popover z ostatnimi repozytoriami; wybór zmienia katalog roboczy
- wiersz narzędzia → rozwinięcie / zwinięcie (chevron obraca się 180° w 380 ms)
- `‹` / `›` w nagłówku okna sesji → schowanie i rozsunięcie okna rozmowy
- `☰` → nakładka historii; `+` → nowa rozmowa; `✕` → schowanie okna
- **Kopiuj** w bloku kodu → schowek, etykieta zmienia się na „Skopiowano" na 1400 ms

**Animacje** — komplet czasów i krzywych w §2 specyfikacji. Reguła:
każdy morfing kształtu 380 ms `cubic-bezier(.22,1,.36,1)`, każde wejście treści
`opacity` 220 ms z opóźnieniem 60 ms + `scale(.94→1)`.

**Auto-scroll** — rozmowa przewija się do dołu przy nowej turze; przewinięcie
w górę o ponad 40 px wstrzymuje auto-scroll do końca bieżącej tury.

## Stan aplikacji

| Zmienna | Typ | Opis |
|---|---|---|
| `chatOpen` | bool | czy okno rozmowy jest rozsunięte |
| `conversationId` | string \| null | bieżąca rozmowa; `null` = stan pusty |
| `messages[]` | tura | `{ role, text, tools[], consent?, error? }` |
| `streaming` | bool | trwa odpowiedź; steruje kursorem, kropką stanu i przyciskiem Przerwij |
| `streamBuffer` | string | tekst dopisywany znak po znaku |
| `tools[].status` | enum | `running` \| `done` \| `error` |
| `tools[].expanded` | bool | domyślnie `false`; wymuszone `true` dla `running` i `error` |
| `consent` | obiekt \| null | `{ tool, title, effect, cwd, remote }` — obecność zatrzymuje turę |
| `consentRules[]` | lista | reguły „Zezwól zawsze", klucz: narzędzie + katalog |
| `context.path` | string | katalog roboczy |
| `context.recent[]` | lista | ostatnie repozytoria z czasem użycia |
| `sessions[]` | lista | `{ name, state, age }` — state: `pracuje` \| `gotowe` \| `bezczynny` |
| `limits` | obiekt | `{ window5h: {used, resetAt}, window7d: {used, resetAt} }` |
| `anchor` | enum | `top` \| `bottom` \| `side` — z pozycji pastylki |
| `micState` | enum | `disabled` (v1) \| `ready` \| `listening` \| `processing` |

Kropka stanu w nagłówku i pierścień pastylki są **sterowane tym samym stanem**:
bursztyn pulsujący (pracuje), zielony (gotowe), niebieski migający co 1 s
(czeka na decyzję), czerwony migający co 1,6 s (błąd), `ink-22` (bezczynne).

## Kontrakt z hostem .NET

Do ustalenia po stronie zespołu, ale WebView2 musi dostać co najmniej:

- **z hosta do UI**: pozycja i kotwica pastylki, stan sesji i limitów,
  strumień tokenów odpowiedzi, zdarzenia narzędzi (start / wynik / błąd),
  żądania zgody, błędy silnika
- **z UI do hosta**: wysłanie promptu, przerwanie tury, decyzja zgody
  (raz / zawsze / odrzuć), zmiana katalogu, otwarcie dialogu wyboru katalogu,
  schowanie okna, kopiowanie do schowka

Zgody typu „Zezwól zawsze" zapamiętuje host, per narzędzie i katalog.

## Zasoby

Brak zasobów zewnętrznych. Ikony w projekcie to znaki tekstowe
(`+`, `☰`, `✕`, `↵`, `▾`, `‹`, `›`) i proste kształty CSS (kropki, pierścienie,
paski poziomu głosu). Fonty tylko systemowe:

```
--font-ui:   'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif
--font-mono: 'Cascadia Mono', Consolas, monospace
```

Jeśli w aplikacji istnieje już zestaw ikon, można nim zastąpić znaki tekstowe —
zachowując rozmiary pól klikalnych (28 × 28 px w nagłówku, 22 × 22 px przy
zwijaniu) i kolory z tokenów.

## Skalowanie

Interfejs musi wyglądać poprawnie przy 100 %, 125 % i 150 %. Wszystkie wymiary
są logiczne (DIP); nie ma obramowań cieńszych niż 1 px ani wysokości bez zapasu
nad interlinią. Szczegóły i zachowanie na wąskich ekranach: §14 specyfikacji.
