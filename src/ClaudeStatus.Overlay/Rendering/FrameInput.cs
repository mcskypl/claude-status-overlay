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
/// klatka jest czystą funkcją tego rekordu. Trzy poziomy widgetu (brzeg / pasek
/// / panel) przenikają niezależnie - stąd osobna alfa dla każdego, a nie jeden
/// wspólny współczynnik jak przy starym morfingu pastylka/panel. Kształt
/// powłoki (szerokość/wysokość/promień) liczy sam <see cref="WidgetRenderer"/>
/// na podstawie <see cref="TargetStage"/> - on jeden zna naturalne rozmiary
/// każdego poziomu (mierzony tekst paska, wysokość panelu zależna od sesji).
/// </summary>
/// <param name="TimeMs">Zegar animacji (od startu nakładki) - do pulsowania i migania.</param>
/// <param name="Now">Czas ścienny do liczenia wieku sesji i terminów resetu.</param>
/// <param name="HoverRow">Indeks podświetlonego wiersza panelu albo -1.</param>
/// <param name="HoverRefresh">Czy kursor stoi na przycisku ręcznego odświeżenia limitów.</param>
/// <param name="UsageRefreshing">Czy czekamy na ręcznie zamówione limity - ikona odświeżania się wtedy kręci.</param>
/// <param name="Update">Pasek o nowej wersji na dole panelu albo null.</param>
/// <param name="HoverUpdate">Czy kursor stoi na tym pasku.</param>
/// <param name="TargetStage">Poziom, do którego aktualnie zmierza morfing kształtu.</param>
/// <param name="Edge">Krawędź ekranu, do której widget jest przyklejony - decyduje, które rogi są płaskie.</param>
/// <param name="Vertical">Układ pionowy (kotwica lewa/prawa krawędź) zamiast poziomego.</param>
/// <param name="RestAlpha">Widoczność warstwy brzegu (cienkiej linii).</param>
/// <param name="SlimAlpha">Widoczność warstwy paska (podgląd po najechaniu).</param>
/// <param name="PanelAlpha">Widoczność warstwy panelu.</param>
/// <param name="PanelScale">Skala panelu przy wejściu/wyjściu (0.94 -&gt; 1).</param>
public sealed record FrameInput(
    double TimeMs,
    DateTime Now,
    SessionSnapshot Snapshot,
    SessionSummary Summary,
    UsageSnapshot? Usage,
    MeterValues Meters,
    int HoverRow,
    bool HoverRefresh,
    bool UsageRefreshing,
    UpdateBanner? Update,
    bool HoverUpdate,
    WidgetStage TargetStage,
    Edge Edge,
    bool Vertical,
    float RestAlpha,
    float SlimAlpha,
    float PanelAlpha,
    float PanelScale)
{
    public IReadOnlyList<SessionInfo> Sessions => Snapshot.Sessions;

    public bool UsageStale => Usage?.Stale ?? false;

    public int MeterCount => Usage?.WindowCount ?? 0;
}
