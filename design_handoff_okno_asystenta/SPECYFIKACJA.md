# Okno asystenta — poziom 4 widgetu „Token Status Widget (notch)"

Handoff implementacyjny. Wszystkie wartości podane w px przy skalowaniu 100 %.
WebView2 skaluje je automatycznie przy 125 % i 150 % — w projekcie nie ma
wartości, które by się przy tym rozjeżdżały (brak `1px` hairline'ów poza
obramowaniami, brak sztywnych wysokości pod tekst).

Prototyp: `Okno asystenta.dc.html` (otwiera się w przeglądarce, przełączniki
u góry pokazują wszystkie stany z listy).

---

## 1. Okno

| Parametr | Wartość |
|---|---|
| Szerokość | **1040 px** łącznie — dwa osobne okna: sesje **392 px** + przerwa **8 px** + rozmowa **640 px** |
| Wysokość | **720 px** (stała) |
| Tło | `#000` (nieprzezroczyste) |
| Promień rogów | 8 px (rogi okna systemowego) |
| Cień | `0 18px 40px -14px rgba(0,0,0,.85)` + `0 0 0 1px rgba(255,255,255,.08)` |
| Otwarcie | `Ctrl+Shift+Spacja` lub menu pastylki |
| Zamknięcie | `Esc` (chowa, nie kasuje rozmowy), `✕` w nagłówku |
| Odstęp od pastylki | **8 px** |

Poziom 4 to **dwa niezależne okna** stojące obok siebie z przerwą **8 px**
(taką samą, jak odstęp od pastylki). Każde ma własne tło `#000`, promień 8 px,
cień `0 18px 40px -14px rgba(0,0,0,.85)` i obrys `0 0 0 1px rgba(255,255,255,.08)` —
nie ma między nimi wspólnej ramki ani linii podziału.

- **Okno sesji** 392 × 720 px (po lewej) — sesje i limity z poziomu 3 pastylki.
- **Okno rozmowy** 640 × 720 px (po prawej) — chowane i rozsuwane przyciskiem
  `‹` / `›` w nagłówku okna sesji (i skrótem `Ctrl+Shift+Spacja`). Po schowaniu
  zostaje samo okno sesji o szerokości 392 px; rozsunięcie animuje się
  morfingiem 380 ms `cubic-bezier(.22,1,.36,1)`, treść wchodzi 220 ms
  z opóźnieniem 60 ms i `scale(.94→1)`.

Podział pionowy prawej kolumny: nagłówek 44 px (stały) → rozmowa (elastyczna,
przewijana) → kompozytor (stały, 88–176 px zależnie od liczby linii).

Lewa kolumna: nagłówek 44 px → lista sesji (elastyczna, przewijana) →
mierniki limitów (stałe, 100 px) → stopka 32 px.

---

## 2. Tokeny

### Kolory

| Token | Wartość | Użycie |
|---|---|---|
| `bg` | `#000` | tło okna, popoverów, nagłówka |
| `ink-90` | `rgba(255,255,255,.9)` | tekst odpowiedzi, nazwy, nagłówki |
| `ink-82` | `rgba(255,255,255,.82)` | tekst dłuższych akapitów w kartach |
| `ink-72` | `rgba(255,255,255,.72)` | tekst drugorzędny, etykiety przycisków |
| `ink-42` | `rgba(255,255,255,.42)` | podpowiedzi, metadane |
| `ink-38` | `rgba(255,255,255,.38)` | komentarze w kodzie, mikrofon nieaktywny |
| `ink-34` | `rgba(255,255,255,.34)` | pasek skrótów, nagłówki sekcji |
| `ink-30` | `rgba(255,255,255,.3)` | liczniki, czasy narzędzi |
| `ink-22` | `rgba(255,255,255,.22)` | obramowania przycisków drugorzędnych, kropka bezczynna |
| `track` | `rgba(255,255,255,.10)` | linie podziału, obramowania kart |
| `track-14` | `rgba(255,255,255,.14)` | obramowanie chipów i popoverów |
| `fill-04` | `rgba(255,255,255,.04)` | tło wiersza narzędzia |
| `fill-05` | `rgba(255,255,255,.05)` | tło pola promptu, chipa kontekstu, karty zgody |
| `fill-06` | `rgba(255,255,255,.06)` | dymek użytkownika, hover |
| `fill-12` | `rgba(255,255,255,.12)` | przycisk „Ponów turę", obramowanie pola w spoczynku |
| `blue` | `oklch(.72 .13 250)` | akcent, karta zgody, mikrofon nasłuchuje, zaznaczenie |
| `green` | `oklch(.72 .13 150)` | narzędzie zakończone, aktywny kontekst, string w kodzie |
| `amber` | `oklch(.78 .13 75)` | praca w toku, kursor strumienia |
| `red` | `oklch(.64 .19 25)` | **wyłącznie** błąd; hover przycisku zamknięcia |
| `on-blue` | `#08101c` | tekst na pełnym niebieskim (przycisk „Zezwól raz") |

### Promienie

| Element | Promień |
|---|---|
| Brzeg pastylki (poziom 1) | 3 px |
| Pasek (poziom 2) | 11 px |
| Panel sesji (poziom 3) | 28 px |
| Okno asystenta (poziom 4) | 8 px |
| Karty, wiersze narzędzi, przyciski, pole promptu, popovery | 8 px |
| Znaczniki, kwadratowe kropki, paski poziomu głosu | 2 px |

### Typografia

Bez zasobów zewnętrznych. Dwa stosy:

```
--font-ui:   'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif
--font-mono: 'Cascadia Mono', Consolas, monospace
```

| Rola | Rozmiar / waga / interlinia | Kolor |
|---|---|---|
| Tekst rozmowy (obie strony) | 14 / 400 / 1.55 | `ink-90` |
| Wyróżnienie w tekście (`**`) | 14 / 600 | `ink-90` |
| Nagłówek w markdownie (`##`) | 15 / 600 / 1.4 | `ink-90` |
| Tytuł karty zgody | 16 / 600 / 1.4 | `ink-90` |
| Opis karty zgody | 13 / 400 / 1.55 | `ink-72` |
| Nazwa rozmowy (nagłówek) | 13 / 600 | `ink-90` |
| Wiersz narzędzia | 13 / 400 | `ink-72` |
| Tabela — nagłówek | 13 / 600 | `ink-72` |
| Tabela — komórki | 13 / 400 (liczby mono) | `ink-90` / `ink-42` |
| Kod w bloku | 13 / 400 / 1.65 mono | `ink-90` |
| Argumenty i wynik narzędzia | 12 / 400 / 1.65 mono | `ink-72` |
| Chip kontekstu | 12 / 500 mono | `ink-72` |
| Metadane, czasy, etykiety pól | 12 / 400 | `ink-34` / `ink-30` |
| Pasek skrótów pod promptem | 11 / 400 mono | `ink-34`, nazwa klawisza `ink-42` |
| Etykiety sekcji (wersaliki) | 11 / 600, tracking .08–.1 em | `ink-34` |

Minimalny rozmiar tekstu w oknie: **11 px** (tylko mono, tylko metadane).

### Animacje

| Ruch | Czas / krzywa |
|---|---|
| Morfing kształtu (pastylka → okno, rozwijanie narzędzia, chevron) | **380 ms** `cubic-bezier(.22,1,.36,1)` |
| Pojawianie się treści | `opacity` **220 ms**, opóźnienie **60 ms**, równolegle `scale(.94 → 1)` |
| Puls bursztynu (praca) | 1200 ms `ease-in-out`, `opacity 1→.45`, `scale 1→.82` |
| Miganie niebieskiego (czeka na decyzję) | 1000 ms, `steps(1,end)` |
| Miganie czerwonego (błąd) | 1600 ms, `steps(1,end)` |
| Kursor tekstowy i kursor strumienia | 1000 ms, `steps(1,end)` |
| Spinner narzędzia | 800 ms liniowo |
| Paski poziomu głosu | 700 ms `ease-in-out`, przesunięcia fazowe 0 / 120 / 240 / 360 ms |
| Hover / zmiana obramowania | 220 ms `ease` |

---

## 3. Nagłówek okna

Wysokość **44 px**, `padding: 0 8px 0 14px`, dolna linia `1px track`.
Elementy w jednym rzędzie, `gap: 10px`:

1. **Kropka stanu** 7 × 7 px, promień 50 %. Kolor i animacja dokładnie jak
   pierścień pastylki: bursztyn pulsujący (pracuje), zielony (gotowe),
   niebieski migający co 1 s (czeka na mnie), czerwony migający co 1,6 s (błąd),
   `ink-22` bez animacji (bezczynne).
2. **Nazwa rozmowy** — 13/600 `ink-90`, `max-width: 230px`, ucinana wielokropkiem.
   Domyślnie „Nowa rozmowa", po pierwszej turze nadawana automatycznie.
3. **Chip kontekstu** (patrz §8).
4. Rozpychacz.
5. **`+` nowa rozmowa** — 28 × 28 px, promień 8, tło przezroczyste,
   hover `fill-06` → ikona `ink-90`.
6. **`☰` historia** — 28 × 28 px, otwiera nakładkę historii.
7. **`✕` zamknij** — 28 × 28 px, ikona `ink-42`; hover tło `red`, ikona `#fff`.
   To jedyne miejsce poza błędem, gdzie pojawia się czerwień (konwencja okna Windows).

**Historia** to nakładka na obszar rozmowy (nie osobne okno): tło `#000`,
`padding: 14px 16px`, wiersz = tytuł 13/`ink-90` + meta 12/`ink-34`,
`padding: 10px 12px`, promień 8, hover `fill-06`. Wejście: pojawianie się treści.

---

## 3b. Lewa kolumna — sesje i limity

Szerokość **392 px** (dokładnie tyle, co panel sesji z poziomu 3 pastylki),
osobne okno (promień 8, własny cień). Zawartość jest tą samą treścią, co w panelu sesji —
po otwarciu okna asystenta nie znika, tylko przesuwa się na lewo od rozmowy.

**Nagłówek** — 44 px, `padding: 0 16px`, dolna linia `1px track`:
„SESJE" 11/600 wersaliki tracking .14 em `ink-34` po lewej,
„5 aktywnych" 11 mono `ink-42` po prawej, a za nim przycisk zwijania rozmowy
22 × 22 px (promień 8, obrys `1px track-14`, glif `‹` / `›` `ink-42`,
hover tło `fill-06` + `ink-90`).

**Lista sesji** — `padding: 8px`, `gap: 2px`, wiersz 32 px, `padding: 0 8px`,
promień 8, hover `fill-06`. Układ wiersza (`gap: 10px`):
kropka 8 px · nazwa 13/`ink-90` (ucinana) · stan 12 mono · wiek 12 mono
`ink-30` w kolumnie 26 px wyrównanej do prawej.
Kolory stanów: pracuje = `amber` + puls 1200 ms, gotowe = `green`,
bezczynny = `ink-22` (kropka) i `ink-30` (tekst).

**Mierniki limitów** — górna linia `1px track`, `padding: 14px 16px 10px`,
`gap: 14px`. Wiersz (`gap: 14px`): etykieta 38 px 12 mono `ink-42` ·
tor `height 4px`, promień 2, tło `rgba(255,255,255,.12)` z wypełnieniem
(`blue` dla okna 5 h, `green` dla okna 7 dni) i znacznikiem tempa
1 × 10 px `ink-22` · blok 88 px wyrównany do prawej:
procent 12/600 `ink-90` nad czasem resetu 11 mono `ink-30`;
blok ma szerokość 106 px i `white-space: nowrap` w obu liniach.

**Stopka** — 38 px, `padding: 0 16px 8px`: adres konta 11 mono `ink-30`
po lewej, „odświeżono N s temu" 11 mono `ink-30` i pierścień odświeżania
10 px po prawej.

---

## 4. Rozmowa — siatka i gęstość

| Odstęp | Wartość |
|---|---|
| Padding obszaru rozmowy | `20px 24px 8px` |
| Między turami (użytkownik ↔ asystent) | **28 px** |
| Wewnątrz tury (akapity, tabela, blok kodu) | **20 px** |
| Między elementami listy | 8 px |
| Tekst | 14 px / interlinia 1.55 |

**Wiadomość użytkownika** — dymek wyrównany do prawej, `max-width: 78%`,
tło `fill-06`, promień 8, `padding: 10px 14px`, tekst `ink-90`.

**Odpowiedź asystenta** — bez dymka, pełna szerokość kolumny, tekst `ink-90`.
Rozróżnienie niesie sam kształt; nie ma etykiet „Ty" / „Asystent".

Zaznaczanie tekstu włączone wszędzie; `::selection` = `blue` przy alfie .35.

Przewijanie: pionowe, tylko obszar rozmowy. Pasek systemowy WebView2
(nakładkowy, cienki). Przy nowej turze auto‑scroll do dołu; przewinięcie
w górę o ponad 40 px wstrzymuje auto‑scroll do końca tury.

---

## 5. Kompozytor

Kontener: `padding: 10px 16px 14px`, górna linia `1px track`, `gap: 8px`.

**Pole promptu**
- min. wysokość **44 px**, max. **132 px** (ok. 5 linii), potem wewnętrzne przewijanie
- `padding: 9px 10px 9px 14px`, promień 8, tło `fill-05`
- obramowanie `1px ink-22` gdy puste, `1px rgba(255,255,255,.22)` + poświata
  `0 0 0 3px oklch(.72 .13 250 / .10)` gdy aktywne
- tekst 14/1.55 `ink-90`, kursor 1 × 16 px, miga 1 s
- wyrównanie pionowe: `center` przy jednej linii, `flex-end` przy wielu
  (przyciski zostają przy dolnej krawędzi)

**Klawiatura**
- `Enter` — wyślij
- `Shift+Enter` — nowa linia
- `Esc` — schowaj okno (w karcie zgody: odrzuć; w trakcie odpowiedzi: przerwij turę)

**Przyciski po prawej**, `gap: 6px`, oba 28 × 28 px, promień 8:
- mikrofon — w v1 nieaktywny: `opacity .38`, kursor `default`,
  tooltip „Dyktowanie — wkrótce"
- wyślij `↵` — tło `fill-12` / ikona `ink-30` gdy pole puste;
  tło `blue` / ikona `on-blue` gdy jest treść

**Pasek skrótów** pod polem: 11 px mono `ink-34`, `gap: 14px`,
kolejność `Enter wyślij · Shift+Enter nowa linia · Esc schowaj`,
po prawej licznik `n / 4000` (pojawia się dopiero po wpisaniu znaku).

---

## 6. Stan pusty

Treść wyśrodkowana pionowo, `padding-bottom: 40px`, `gap: 10px`:

1. „O co chodzi?" — 20/600 `ink-90`
2. zdanie o dostępnych narzędziach i aktywnym katalogu — 14/1.55 `ink-42`,
   `max-width: 440px`, nazwa katalogu w mono `ink-72`
3. dwie przykładowe komendy — 13 `ink-34`, odstęp od bloku wyżej 8 px

Pole promptu jest aktywne, kursor miga. Nic więcej.

---

## 7. Odpowiedź

### 7a. W trakcie (strumień)

- Tekst dopisuje się znak po znaku (prototyp: 2 znaki / 36 ms ≈ 55 zn./s).
- Na końcu tekstu **kursor strumienia**: prostokąt 7 × 15 px w kolorze `amber`,
  miga 1 s. To on odróżnia strumień od gotowej odpowiedzi.
- Pod tekstem pasek tury, `gap: 10px`:
  kropka 8 px `amber` pulsująca 1200 ms · napis „Pracuję…" 12/`ink-34` ·
  przycisk **Przerwij** (wys. 24 px, obramowanie `ink-22`, kwadracik 7 × 7 px
  promień 2, po prawej `Esc` w 11 mono `ink-30`).
- Wywołania narzędzi, które jeszcze trwają, są widoczne nad tekstem (§9).

### 7b. Gotowa — markdown

Kolejne bloki oddzielone 20 px:

- **Akapit** 14/1.55 `ink-90`, `**pogrubienie**` = waga 600.
- **Nagłówek** 15/600 `ink-90`.
- **Lista** — bez punktorów; myślnik `—` w `ink-30` w kolumnie 12 px,
  odstęp do tekstu 10 px, między pozycjami 8 px.
- **Tabela** — `display: grid`, obramowanie `1px track`, promień 8, `overflow: hidden`.
  Wiersz nagłówka tło `fill-05`, 13/600 `ink-72`. Komórki `padding: 8px 12px`,
  linia górna `1px track`. Liczby mono, wyrównane do prawej; kolumna udziałów `ink-42`.
- **Blok kodu** — obramowanie `1px track`, promień 8, tło `fill-04`.
  Pasek tytułowy 30 px: język 11 mono `ink-34` po lewej, przycisk **Kopiuj**
  po prawej (22 px, `ink-42`, hover tło `fill-06` + `ink-90`, po kliknięciu
  napis „Skopiowano" na 1400 ms). Kod `padding: 12px`, 13/1.65 mono,
  poziome przewijanie zamiast zawijania.
  Podświetlenie: słowa kluczowe `blue`, ciągi znaków `green`,
  komentarze `ink-38`, reszta `ink-90`. Liczby `amber` (nieużyte w przykładzie).

---

## 8. Przełącznik kontekstu

Chip w nagłówku, bo katalog obowiązuje całą rozmowę, a nie pojedynczą wiadomość.

- wysokość 24 px, `padding: 0 8px`, promień 8, obramowanie `1px track-14`,
  tło `fill-05`, tekst 12/500 mono `ink-72`
- kwadracik stanu 5 × 5 px promień 2: `green` (katalog dostępny),
  `ink-22` (niedostępny / brak indeksu)
- strzałka `▾` 9 px, `opacity .6`
- hover: obramowanie `rgba(255,255,255,.3)`, tekst `ink-90`

**Popover** — 286 px szerokości, tło `#000`, obramowanie `1px track-14`,
promień 8, cień panelu, `padding: 6px`, wejście: pojawianie się treści.
Nagłówek „Ostatnie repozytoria" 11 wersaliki `ink-34`. Pozycja: kwadracik stanu,
nazwa mono 13, po prawej czas 11 `ink-30`; `padding: 8px`, promień 8,
hover `fill-06`. Aktywna pozycja ma zielony kwadracik i tekst `ink-90`.
Na dole separator 1 px + „Wskaż inny katalog…" (otwiera dialog systemowy).

---

## 9. Wywołanie narzędzia

Wiersz na pełną szerokość kolumny, wysokość nagłówka **32 px**,
`padding: 0 12px`, promień 8, tło `fill-04`, obramowanie `1px track`.
Układ: znacznik stanu 8 px · nazwa „Narzędzie · co zrobiło" 13/`ink-72` ·
rozpychacz · metryka 12 mono `ink-30` · chevron 9 px.

| Stan | Znacznik | Tekst | Obramowanie |
|---|---|---|---|
| Pracuje | pierścień 10 px `amber`, obrót 800 ms | „Clockify · pobieram wpisy z tygodnia…" + licznik czasu | `track` |
| Gotowe | kropka 8 px `green` | „Clockify · pobrano wpisy z tygodnia" + „2,1 s · 38 wpisów" | `track` |
| Błąd | kropka 8 px `red`, miga 1,6 s | „Azure DevOps · nie udało się pobrać work items" + przycisk **Ponów** | `red` przy alfie .5 |

**Zachowanie domyślne:** wiersze są zwinięte; rozwijają się same, gdy narzędzie
pracuje lub zwróciło błąd. Kliknięcie przełącza — chevron obraca się o 180°
w 380 ms, zawartość wjeżdża animacją pojawiania się treści.

**Rozwinięty wiersz błędu** pokazuje pod nagłówkiem jedną linię przyczyny
(12 mono `ink-42`, `padding: 0 12px 10px`).

**Rozwinięty wiersz gotowy** ma sekcję `padding: 12px`, linię górną `1px track`,
`gap: 10px`: etykieta „Argumenty" (11 wersaliki `ink-34`) → JSON 12 mono
`ink-72` → etykieta „Wynik" → wynik 12 mono, `max-height: 120px` z przewijaniem.

---

## 10. Karta zgody

Kluczowy ekran. Żyje **w strumieniu rozmowy**, w miejscu, w którym tura się
zatrzymała — przewija się razem z treścią, nie blokuje reszty okna.
Nad kartą zostaje zdanie asystenta wyjaśniające, po co ta operacja.

Ranga wizualna ponad resztą rozmowy bez użycia czerwieni:

- obramowanie `1px oklch(.72 .13 250 / .55)`, **lewa krawędź 2 px pełnego `blue`**
- poświata `0 0 0 3px oklch(.72 .13 250 / .10)` — to ona łapie kąt oka
- tło `fill-05`, promień 8, `padding: 16px 18px`, `gap: 14px`
- wejście: pojawianie się treści (220 ms / 60 ms / scale .94→1)
- kropka 8 px `blue` **migająca co 1 s** — ten sam rytm, co stan
  „czeka na mnie" w pastylce; obok etykieta „CZEKA NA TWOJĄ DECYZJĘ"
  (11/600 wersaliki, tracking .1 em, kolor `blue`)

Treść:

1. tytuł operacji — 16/600 `ink-90` (np. „git push — wypchnięcie 3 commitów")
2. skutek jednym zdaniem — 13/1.55 `ink-72`
3. siatka faktów `auto 1fr`, `gap: 6px 14px`, 12 px:
   etykieta `ink-34` / wartość mono `ink-72` — **Narzędzie**, **Katalog**, **Zdalne**
   (dla operacji plikowych: ścieżka; dla SQL: baza i tabela)
4. rząd przycisków, wysokość 32 px, promień 8, `gap: 8px`:
   - **Zezwól raz** — tło `blue`, tekst `on-blue` 13/600
   - **Zezwól zawsze** — obramowanie `blue` .55, tekst `blue` 13/500,
     hover tło `blue` przy alfie .12
   - rozpychacz
   - **Odrzuć** — obramowanie `ink-22`, tekst `ink-72` 13/500 (wyrównany do prawej,
     żeby nie sąsiadował z akcją domyślną)
5. linia skrótów 11 mono `ink-30`:
   „Enter = zezwól raz · Esc = odrzuć · »zawsze« zapamiętuje regułę dla tego katalogu"

Gdy karta jest widoczna, kropka stanu w nagłówku i pierścień pastylki
przechodzą w niebieski migający co 1 s, a pole promptu pozostaje aktywne
(można dopisać uwagę zamiast decydować).

---

## 11. Błąd silnika

Karta w strumieniu, obramowanie `1px red` przy alfie .5, tło `fill-04`,
promień 8, `padding: 14px 16px`, `gap: 12px`:

1. kropka 8 px `red` migająca 1,6 s + tytuł 14/600 `ink-90`
2. wyjaśnienie 13/1.55 `ink-72` z informacją, że treść pytania jest zachowana
3. linia diagnostyczna 12 mono `ink-42`: kod błędu · znacznik czasu · numer próby
4. przyciski 30 px: **Ponów turę** (tło `fill-12`, tekst `ink-90` 13/600)
   i **Szczegóły** (obramowanie `ink-22`, tekst `ink-72`)

Czerwień występuje wyłącznie tutaj i w wierszu narzędzia z błędem.

---

## 12. Mikrofon

Przycisk 28 × 28 px (promień 8) stoi w kompozytorze po lewej od „wyślij".
W v1 nieaktywny: `opacity .38`, kursor `default`, tooltip „Dyktowanie — wkrótce".

Stany na przyszłość (w prototypie pokazane w skali 32 px dla czytelności):

| Stan | Wygląd |
|---|---|
| Gotowy | obramowanie `1px track-14`, tło `fill-05`, ikona `ink-72` |
| Nasłuchuje | obramowanie `blue` .55, tło `blue` .14, poświata `0 0 0 3px blue/.10`; zamiast ikony **4 paski 2 px** (promień 2, `gap: 2px`) skalowane pionowo animacją 700 ms z fazami 0/120/240/360 ms — amplituda sterowana realnym poziomem sygnału |
| Przetwarza mowę | obramowanie `amber` .5, tło `amber` .12, pierścień 12 px `amber` obracający się 800 ms |

Po rozpoznaniu tekst wchodzi do pola promptu jako zwykła treść do edycji —
nie wysyła się sam.

---

## 13. Przejście z pastylki

Pastylka **nie znika** — zostaje na krawędzi jako kotwica i wskaźnik stanu.
Okno pojawia się w odległości **8 px** od jej krawędzi.

| Kotwica | Pozycja okna | `transform-origin` | Kierunek wzrostu |
|---|---|---|---|
| Górna | wyśrodkowane w poziomie względem pastylki, 8 px pod nią | `top center` | w dół |
| Dolna | wyśrodkowane w poziomie, 8 px nad nią | `bottom center` | w górę |
| Boczna (lewa) | 8 px na prawo, wyśrodkowane w pionie względem pastylki | `left center` | w prawo |

Para okien pozostaje **pozioma 1040 × 720 niezależnie od kotwicy** (z przerwą
8 px w środku) —
zmienia się wyłącznie kierunek rozwinięcia i oś wyrównania.

Sekwencja:

1. `t = 0 ms` — okno w skali startowej odpowiadającej pastylce
   (`scale(.6, .03)` dla kotwic poziomych, `scale(.04, .54)` dla bocznej),
   `opacity 0`.
2. `0 → 380 ms` — morfing rozmiaru i przesunięcia,
   `cubic-bezier(.22,1,.36,1)`.
3. `60 → 280 ms` — treść: `opacity 0 → 1` w 220 ms, równolegle `scale(.94 → 1)`.

Zamknięcie: ta sama sekwencja odwrócona, treść gaśnie w 120 ms bez opóźnienia,
kształt zwija się w 380 ms.

Jeżeli pastylka stoi zbyt blisko krawędzi ekranu, żeby zmieścić 1040 px
wyśrodkowane, okno przykleja się do krawędzi roboczego obszaru z marginesem
12 px, a `transform-origin` przesuwa się na rzut środka pastylki.

---

## 14. Skalowanie 100 / 125 / 150 %

Prototyp ma przełącznik skali u góry. Zasady:

- Wszystkie wymiary podane w px są wymiarami logicznymi (DIP) — WebView2
  mnoży je przez współczynnik systemowy. Nie ma wartości zależnych od pikseli
  fizycznych.
- Obramowania 1 px przy 125 % dają 1,25 px fizycznego; to jedyne miejsce,
  gdzie widać zaokrąglenie. Nie stosować obramowań cieńszych niż 1 px.
- Wysokości pod tekst (nagłówek 44, wiersz narzędzia 32, przyciski 30/32)
  mają zapas ≥ 8 px ponad interlinię, więc przy 150 % nic się nie ucina.
- Para okien 1040 × 720 przy 150 % zajmuje 1560 × 1080 fizycznych px — wymaga
  ekranu szerszego niż 1560 px; poniżej tej szerokości okna otwierają się
  nałożone na siebie (rozmowa na wierzchu, sesje pod skrótem `‹`);
  przy wysokości roboczej poniżej 1080 px okno skraca się do
  `min(720, obszar_roboczy - 24)` i przewija rozmowę.
