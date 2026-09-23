<div align="center">

<img src="docs/img/ikona.png" width="88" alt="">

# Claude Status Overlay

[![wersja](https://img.shields.io/github/v/release/mcskypl/claude-status-overlay?style=for-the-badge&label=wersja&color=2f81f7)](https://github.com/mcskypl/claude-status-overlay/releases/latest)
[![Windows](https://img.shields.io/badge/Windows-10%20i%2011-0078d4?style=for-the-badge)](#wymagania)

## [⬇&nbsp; Pobierz instalator](https://github.com/mcskypl/claude-status-overlay/releases/latest)

<img src="docs/img/brzeg.png" width="560" alt="Pasek statusu na krawędzi ekranu">

<sub>Tyle zajmuje na ekranie. 148 × 5 px, zawsze na wierzchu, nigdy w drodze.</sub>

</div>

<br>

## Trzy poziomy szczegółu

Nakładka rośnie dokładnie wtedy, kiedy jej potrzebujesz — i sama wraca do cienkiej linii.

<table>
<tr>
<td width="50%" valign="top">

**Najedź myszką → pasek**

<img src="docs/img/pasek.png" alt="Pasek">

Stan, czas i oba liczniki w jednej linii.

</td>
<td width="50%" valign="top">

**Kliknij → panel**

Wszystkie sesje naraz: projekt, stan, jak dawno. Klik w wiersz przełącza na okno edytora tej sesji.

</td>
</tr>
</table>

<div align="center">
<img src="docs/img/panel.png" width="430" alt="Panel ze wszystkimi sesjami i limitami">
</div>

Biała kreska na pasku limitu pokazuje, **ile z okna czasowego już minęło**. Wypełnienie przed kreską = palisz limit wolniej, niż leci czas. Za kreską = szybciej, niż powinieneś.

<br>

## Instalacja

1. **[Pobierz `ClaudeStatusOverlay-Setup.exe`](https://github.com/mcskypl/claude-status-overlay/releases/latest)** i uruchom.
2. Kreator pyta tylko o autostart. **Bez uprawnień administratora**, bez pytania o katalog.
3. **Zamknij i otwórz na nowo sesje Claude Code** — nakładka podpina się do nich przez hooki, które ładują się przy starcie sesji.

To wszystko. Instalator sam dopisuje hooki do `settings.json` (robiąc wcześniej kopię zapasową) i sam dociąga .NET Desktop Runtime 10, jeśli go nie masz.

> Wolisz bez instalatora? W każdym wydaniu jest też `...-portable.zip`.

<br>

## Aktualizuje się sama

Gdy pojawi się nowa wersja, nakładka daje znać — jeden klik i po sprawie: pobiera, podmienia, wraca na swoje miejsce razem z Twoimi ustawieniami.

<div align="center">
<img src="docs/img/menu.png" width="230" alt="Menu pod prawym przyciskiem">
</div>

Pod prawym przyciskiem siedzi cała reszta: pozycja przy dowolnej krawędzi lub rogu, blokada przesuwania, dźwięki, częstotliwość odświeżania limitów, „zawsze na wierzchu".

<br>

## Wymagania

- **Windows 10 lub 11** (64-bit)
- **[Claude Code](https://claude.com/claude-code)** — w terminalu, VS Code, JetBrains, obojętnie
- .NET Desktop Runtime 10 — *instalator zainstaluje go za Ciebie, jeśli trzeba*

## Prywatność

Nakładka nie ma serwera, konta ani telemetrii. Wychodzi z Twojego komputera tylko po dwie rzeczy, obie do wyłączenia jednym kliknięciem w menu:

- **limity** — `api.anthropic.com`, tym samym tokenem, którego już używa Claude Code (tylko do odczytu),
- **aktualizacje** — publiczne API GitHuba, raz na dobę, bez żadnych danych o Tobie.

<br>

---

<div align="center">

### Podoba się?

Nakładka jest darmowa i będzie. Jeśli oszczędziła Ci choć trochę czasu — postaw kawę:

<a href="https://buycoffee.to/pixelcodelab" target="_blank"><img src="https://buycoffee.to/static/img/share/share-button-dark--pl.png" style="width: 351px; height: 92px" alt="Postaw kawę dla Pixelcodelab na buycoffee.to"></a>

A jeśli nie — zostaw ⭐ na repo, to też pomaga.

<br>

**[⬇ Pobierz](https://github.com/mcskypl/claude-status-overlay/releases/latest)** · **[Zgłoś problem](https://github.com/mcskypl/claude-status-overlay/issues)** · **[Dokumentacja techniczna](docs/dokumentacja.md)**

</div>
