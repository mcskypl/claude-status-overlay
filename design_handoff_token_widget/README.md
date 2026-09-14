# Handoff: Token Status Widget (notch) — v2, trzy poziomy

## Overview
Widget paska statusu: przyklejona do krawędzi ekranu czarna "pastylka" w
stylu notcha. Ma teraz **trzy poziomy** zamiast dwóch:

1. **Brzeg** (`stan: brzeg`) — domyślny, zawsze widoczny: cienka linia bez
   żadnego tekstu, tylko proporcje limitów i barwny znacznik stanu.
2. **Pasek** (`stan: pasek`) — podgląd po najechaniu kursorem: mały
   pierścień, wartość (czas albo nazwa stanu) i oba mierniki w linii.
3. **Panel** (`stan: panel`) — po kliknięciu: pełna lista sesji i mierniki
   z terminem resetu (bez zmian względem poprzedniej wersji).

Najechanie **nie** otwiera od razu panelu — tylko pasek. Panel wymaga kliku
i zostaje otwarty, dopóki nie kliknie się ponownie (zjazd kursorem go nie
zamyka).

## About the Design Files
Pliki w tej paczce to **referencja projektowa wykonana w HTML** — prototyp
pokazujący wygląd i zachowanie, a nie kod produkcyjny do skopiowania.
`Token Widget.dc.html` otwiera się bezpośrednio w przeglądarce; logika
komponentu jest w bloku `<script data-dc-script>` na końcu pliku. Sekcje
poniżej nagłówka widgetu ("stany sesji", "dwa okna, dwie wartości",
"poziomy limitu") to **dokumentacja tokenów**, nie część widgetu — pokazują
dokładne kolory i progi, których widget używa, ale same nie są renderowane
w aplikacji.

## Fidelity
**High-fidelity.** Kolory, typografia, odstępy i animacje są finalne.
Tło pulpitu i pasek menu w prototypie to tylko scena poglądowa.

## Poziomy

### 1. Brzeg (148 × 5 px poziomo, 5 × 148 px pionowo)
- Tło `#000`, promień rogu 3 px (płaski od strony krawędzi ekranu, tak jak
  poprzednio). Cień **dużo subtelniejszy** niż reszta: `0 4px 14px -6px
  rgba(0,0,0,.7)` (zamiast `0 18px 40px -14px rgba(0,0,0,.85)`).
- Dzieli się na pół: pierwsza połowa to okno 5 h, rośnie od swojego
  skrajnego końca (lewo/góra) ku środkowi; druga to okno 7 dni, rośnie od
  przeciwnego końca (prawo/dół) ku środkowi. Tor `rgba(255,255,255,.13)`.
- Pośrodku: znacznik stanu najważniejszej sesji, 12 px wzdłuż dłuższej osi,
  na pełną grubość paska, promień 3 px. Kolor i animacja jak pierścień w
  pasku (patrz niżej) - pulsuje przy pracy, miga przy błędzie/prośbie
  o zgodę, stoi w miejscu przy reszcie stanów.

### 2. Pasek (318 × 22 px poziomo w projekcie; w aplikacji szerokość mierzona
z rzeczywistej treści, wysokość zawsze 22 px)
- Tło `#000`, promień 11 px (pełne zaokrąglenie, `= wysokość / 2`). Cień jak
  w panelu: `0 18px 40px -14px rgba(0,0,0,.85)`.
- Poziomo: pierścień 11×11 px → gap 9 → wartość (Mono 11 px 500) → gap 9 →
  separator 1×10 px `rgba(255,255,255,.14)` → gap 9 → `5h [pasek 34×3] 35%`
  → gap 9 → `7d [pasek 34×3] 72%`. Padding 11 px po bokach.
- Wartość: czas („6m”) dla pracuje/gotowe; **nazwa stanu** („czeka”, „błąd”)
  dla stanów wymagających uwagi — kolorowana jak pierścień, żeby rzucała się
  w oczy bardziej niż goły czas.
- Pionowo (kotwica lewa/prawa krawędź): bez tekstu wartości (za wąsko na
  czytelny napis) — pierścień → separator poziomy 10×1 px → pionowy pasek
  5 h (3×64 px, wypełnienie rośnie od dołu) → pionowy pasek 7 dni → liczba
  aktywnych sesji na końcu.

### 3. Panel (392 × 292 px) — bez zmian
Nagłówek `SESJE · N aktywne`, wiersze sesji (kropka, nazwa, stan, wiek),
separator, dwa mierniki z etykietą, paskiem, procentem i terminem resetu.
Szczegóły układu jak w poprzedniej wersji handoffu.

## Kolory stanów
| Stan | Kolor | Pierścień/kropka | Miganie |
|---|---|---|---|
| pracuje | bursztyn `oklch(.78 .13 75)` | częściowy łuk (234°), pulsuje 1→.45→1 co 2,4 s | — |
| gotowe | zielony `oklch(.72 .13 150)` | pełny, bez animacji | — |
| czeka na mnie | **akcent** `oklch(.72 .13 250)` (domyślnie ten sam niebieski, co okno 5 h) | pełny | twarde, co 1 s, dolna faza .25 alfy |
| błąd | **czerwony** `oklch(.64 .19 25)` (nowy, bardziej nasycony niż stary akcent czerwony) | pełny | twarde, co 1,6 s (wolniej niż "czeka") |
| bezczynny | `rgba(255,255,255,.4)` | sam tor | — |

„Czeka na mnie” celowo dostał kolor **akcentu** (ten sam, co miernik 5 h),
nie osobny czerwony — akcent jest tym, co user wybiera; twardy czerwony
zostaje zarezerwowany wyłącznie dla błędu, żeby nie było dwóch znaczeń
czerwieni.

## Próg limitu
Pasek i procent miernika (5 h i 7 dni, w pasku i w panelu, oraz proporcje
w brzegu) świecą na **czerwono** (`oklch(.64 .19 25)`, ten sam co błąd),
gdy zostało **poniżej 15 %** — niezależnie od tego, które to okno. Powyżej
progu: niebieski dla 5 h, zielony dla 7 dni.

## Interactions & Behavior
- Najechanie kursorem: brzeg → pasek (`opacity` 160/200 ms, bez skalowania).
- Kliknięcie: przełącza pasek/brzeg ↔ panel (`opacity` 220 ms z opóźnieniem
  60 ms + `scale(.94→1)`, `transform-origin: 50% 0%` - panel rośnie od góry).
- Morfing kształtu (`width`, `height`, `border-radius`) zawsze 380 ms
  `cubic-bezier(.22,1,.36,1)`, niezależnie od tego, między którymi dwoma
  poziomami się przełącza.

## Pozycjonowanie przy krawędzi
Jak poprzednio: `pozycja` (góra/lewo/prawo/dół) + `odklejona` (bool).
Kotwica lewa/prawa krawędź **obraca treść brzegu i paska o 90°** (patrz
wyżej) - panel zawsze zostaje poziomy 392×292, niezależnie od kotwicy.

## Design Tokens
- Tło `#000`; tekst `#fff` z alfą: 0.9 / 0.82 / 0.72 / 0.42 / 0.38 / 0.34 / 0.3 / 0.22.
- Tor paska: `rgba(255,255,255,.10–.14)` (brzeg nieco ciemniejszy, .13).
- Akcenty: niebieski `oklch(.72 .13 250)`, zielony `oklch(.72 .13 150)`,
  bursztyn `oklch(.78 .13 75)`, **czerwony `oklch(.64 .19 25)`** (błąd + próg 15 %).
- Radius: 3 (brzeg), 11 (pasek), 28 (panel), 8/2 (drobne elementy panelu).
- Cień: `0 4px 14px -6px rgba(0,0,0,.7)` (brzeg) albo
  `0 18px 40px -14px rgba(0,0,0,.85)` (pasek, panel) - nieanimowany,
  przełącza się razem ze zmianą poziomu.

## Files
- `Token Widget.dc.html` — prototyp (scena pulpitu + widget, przełączniki
  poziomu/pozycji, plus sekcje dokumentujące kolory stanów, progi limitu
  i przykładowe kombinacje dwóch okien).
- `support.js`, `uploads/` — zaplecze narzędzia projektowego (referencyjne
  zrzuty ekranu wklejone podczas iteracji) - nieistotne dla implementacji.
