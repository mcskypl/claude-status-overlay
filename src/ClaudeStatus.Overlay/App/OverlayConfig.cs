using System.Text.Json.Serialization;
using ClaudeStatus.Overlay.Placement;

namespace ClaudeStatus.Overlay.App;

/// <summary>
/// Ustawienia użytkownika. Klucze i wartości są zgodne z wersją PowerShellową,
/// więc istniejący status-overlay.config.json wczytuje się bez zmian.
/// </summary>
public sealed class OverlayConfig
{
    /// <summary>Pozycja widgetu (lewy górny róg treści) dla kotwicy <see cref="WidgetAnchor.Free"/>.</summary>
    public int X { get; set; } = 60;

    public int Y { get; set; } = 60;

    [JsonConverter(typeof(LenientAnchorConverter))]
    public WidgetAnchor Anchor { get; set; } = WidgetAnchor.TopCenter;

    /// <summary>Odsunięta od krawędzi o 18 px, wszystkie rogi zaokrąglone.</summary>
    public bool Detached { get; set; }

    /// <summary>
    /// Blokada pozycji: przeciąganie nic nie robi (kotwica zostaje taka, jaka jest),
    /// reszta myszy - najechanie, klik, menu - działa dalej.
    /// </summary>
    public bool Locked { get; set; }

    public bool Sound { get; set; } = true;

    public bool TopMost { get; set; } = true;

    /// <summary>Rozwijanie po najechaniu; wyłączone = rozwija klik.</summary>
    public bool Hover { get; set; } = true;

    /// <summary>Paski i mierniki limitów 5 h / 7 dni.</summary>
    public bool Usage { get; set; } = true;

    /// <summary>
    /// Co ile sekund nakładka sama pyta o limity (menu daje 1 / 5 / 10 / 15 / 30 minut).
    /// Rzadziej = mniej ruchu do API, które przy częstym pytaniu odpowiada 429;
    /// ręczne odświeżenie z panelu i tak omija ten odstęp.
    /// </summary>
    public int UsageIntervalSeconds { get; set; } = (int)DefaultUsageInterval.TotalSeconds;

    public static readonly TimeSpan DefaultUsageInterval = TimeSpan.FromMinutes(5);

    /// <summary>Odstęp z konfiguracji, przycięty do rozsądnego zakresu (ręcznie zepsuty plik nie zaleje API).</summary>
    [JsonIgnore]
    public TimeSpan UsageInterval => TimeSpan.FromSeconds(Math.Clamp(UsageIntervalSeconds, 30, 6 * 3600));

    /// <summary>
    /// Codzienne sprawdzanie, czy na GitHubie nie ma nowszego wydania.
    /// Wyłączone = zero ruchu w sieci w tej sprawie (poza „Sprawdź teraz" z menu).
    /// </summary>
    public bool Updates { get; set; } = true;
}
