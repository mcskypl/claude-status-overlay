using ClaudeStatus.Core.Sessions;
using ClaudeStatus.Overlay.Model;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>Akcenty z projektu (oklch przeliczone na sRGB) i biel z alfą.</summary>
public static class Palette
{
    public static readonly Color Blue = Color.FromArgb(96, 170, 243);      // oklch(0.72 0.13 250) - akcent, okno 5h, "czeka na mnie"
    public static readonly Color Green = Color.FromArgb(98, 187, 120);     // oklch(0.72 0.13 150) - okno 7 dni, "gotowe"
    public static readonly Color Amber = Color.FromArgb(232, 170, 78);     // oklch(0.78 0.13 75)  - "pracuje"
    public static readonly Color Danger = Color.FromArgb(233, 80, 77);     // oklch(0.64 0.19 25)  - "błąd", limit < 15%
    public static readonly Color Black = Color.Black;

    /// <summary>rgba(255,255,255,alpha)</summary>
    public static Color White(double alpha) => Color.FromArgb(Alpha(alpha), 255, 255, 255);

    /// <summary>Kolor z podmienioną alfą.</summary>
    public static Color With(this Color c, double alpha) => Color.FromArgb(Alpha(alpha), c.R, c.G, c.B);

    /// <summary>
    /// Kolor miernika limitu: normalny (niebieski/zielony) dopóki zostało dość,
    /// czerwony poniżej progu <see cref="Design.UsageDangerFreeBelow"/> - próg
    /// liczony od tej samej (wygładzonej) wartości procentowej, która rysuje pasek,
    /// żeby kolor nie odklejał się od animacji.
    /// </summary>
    public static Color ForUsage(double usedPercent, Color normal) =>
        (100.0 - usedPercent) < Design.UsageDangerFreeBelow ? Danger : normal;

    private static int Alpha(double a) => (int)Math.Round(255 * Math.Clamp(a, 0, 1));
}

/// <summary>Jak zachowuje się pierścień w pastylce / kropka w panelu.</summary>
public enum Glyph
{
    /// <summary>Sam tor, bez wypełnienia.</summary>
    Track,
    /// <summary>Obracający się, pulsujący łuk.</summary>
    Spin,
    /// <summary>Pełny pierścień.</summary>
    Full,
    /// <summary>Pełny pierścień, miga (twardo, bez płynnego zanikania).</summary>
    Blink,
}

/// <summary>
/// Wygląd stanu: kolor, kropka, etykieta i sposób rysowania. Stany migające
/// mają własne tempo (<see cref="BlinkCycleMs"/>) - błąd miga wolniej niż
/// prośba o zgodę, żeby dało się je odróżnić kątem oka.
/// </summary>
public sealed record StateStyle(Color Color, Color Dot, string Label, Glyph Glyph, int BlinkCycleMs = 0)
{
    public static readonly StateStyle Idle = new(Palette.White(0.40), Palette.White(0.16), "bezczynny", Glyph.Track);
    public static readonly StateStyle Working = new(Palette.Amber, Palette.Amber, "pracuje", Glyph.Spin);
    public static readonly StateStyle Done = new(Palette.Green, Palette.Green, "gotowe", Glyph.Full);
    public static readonly StateStyle Attention = new(Palette.Blue, Palette.Blue, "czeka na Ciebie", Glyph.Blink, 1000);
    public static readonly StateStyle Error = new(Palette.Danger, Palette.Danger, "błąd", Glyph.Blink, 1600);

    public static StateStyle For(SessionState state) => state switch
    {
        SessionState.Working => Working,
        SessionState.Done => Done,
        SessionState.Attention => Attention,
        SessionState.Error => Error,
        _ => Idle,
    };

    /// <summary>Krótki tekst do paska/panelu: "czeka" / "błąd" zamiast czasu dla stanów wymagających uwagi.</summary>
    public string ShortValue(TimeSpan elapsed) => this switch
    {
        _ when this == Attention => "czeka",
        _ when this == Error => "błąd",
        _ when this == Idle => "",
        _ => Formats.Elapsed(elapsed),
    };

    /// <summary>Czy migający glif jest akurat w fazie "widoczny" o danym czasie zegara.</summary>
    public bool IsBlinkOnAt(double timeMs) =>
        BlinkCycleMs <= 0 || (long)(timeMs / (BlinkCycleMs / 2.0)) % 2 == 0;
}
