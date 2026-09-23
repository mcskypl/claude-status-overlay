using ClaudeStatus.Core.Sessions;
using ClaudeStatus.Core.Usage;
using ClaudeStatus.Overlay.Model;
using ClaudeStatus.Overlay.Placement;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>Wygładzone wartości pasków limitów (procent zużyty); null = pasek nie istnieje.</summary>
public readonly record struct MeterValues(float? FiveHour, float? SevenDay);

/// <summary>
/// Wszystko, czego potrzebuje jedna klatka. Renderery niczego nie liczą same
/// z zegara ani nie sięgają do stanu aplikacji - dostają gotowe liczby, więc
/// klatka jest czystą funkcją tego rekordu. Oba poziomy widgetu (brzeg i pasek)
/// przenikają niezależnie - stąd osobna alfa dla każdego. Kształt powłoki
/// (szerokość/wysokość/promień) liczy sam <see cref="WidgetRenderer"/> na
/// podstawie <see cref="TargetStage"/> - on jeden zna naturalne rozmiary
/// każdego poziomu (mierzony tekst paska).
/// </summary>
/// <param name="TimeMs">Zegar animacji (od startu nakładki) - do pulsowania i migania.</param>
/// <param name="Now">Czas ścienny do liczenia wieku sesji i terminów resetu.</param>
/// <param name="UsageRefreshing">Czy czekamy na ręcznie zamówione limity.</param>
/// <param name="TargetStage">Poziom, do którego aktualnie zmierza morfing kształtu.</param>
/// <param name="Edge">Krawędź ekranu, do której widget jest przyklejony - decyduje, które rogi są płaskie.</param>
/// <param name="Vertical">Układ pionowy (kotwica lewa/prawa krawędź) zamiast poziomego.</param>
/// <param name="RestAlpha">Widoczność warstwy brzegu (cienkiej linii).</param>
/// <param name="SlimAlpha">Widoczność warstwy paska (podgląd po najechaniu).</param>
public sealed record FrameInput(
    double TimeMs,
    DateTime Now,
    SessionSnapshot Snapshot,
    SessionSummary Summary,
    UsageSnapshot? Usage,
    MeterValues Meters,
    bool UsageRefreshing,
    WidgetStage TargetStage,
    Edge Edge,
    bool Vertical,
    float RestAlpha,
    float SlimAlpha)
{
    /// <summary>
    /// Czy pod widgetem stoi okno szkła. Wtedy powłoka staje się półprzezroczysta -
    /// ostatnim przebiegiem po bitmapie, już po narysowaniu tekstu.
    /// </summary>
    public bool Glass { get; init; }

    /// <summary>
    /// Klatka sprowadzona do tego, od czego zależą piksele warstw. Znika krycie
    /// poszczególnych poziomów - to miesza dopiero kompozycja, a nie same warstwy -
    /// a czas ścienny jest obcięty do sekundy, bo tyle wynosi rozdzielczość
    /// każdego napisu, który z niego powstaje.
    ///
    /// Reszta pól zostaje, także ta, której dana warstwa nie czyta: nadmiarowe pole
    /// w kluczu kosztuje najwyżej jedno rysowanie więcej, a pole pominięte -
    /// nieodświeżony napis. Dlatego klucz jest całą klatką z kilkoma wyjątkami,
    /// a nie listą tego, co akurat przyszło do głowy.
    ///
    /// Tego, co zmienia się z upływu czasu w obrębie sekundy - obrotu, pulsowania
    /// i migania - pilnuje osobno <see cref="AnimationPhase"/>.
    /// </summary>
    public FrameInput ContentKey() => this with
    {
        TimeMs = 0,
        Now = new DateTime(Now.Ticks - Now.Ticks % TimeSpan.TicksPerSecond, Now.Kind),
        RestAlpha = 0,
        SlimAlpha = 0,
    };

    public IReadOnlyList<SessionInfo> Sessions => Snapshot.Sessions;

    public bool UsageStale => Usage?.Stale ?? false;

    public int MeterCount => Usage?.WindowCount ?? 0;
}
