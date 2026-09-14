using System.Drawing.Imaging;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// Brzeg: domyślny, zawsze widoczny poziom widgetu - cienka linia (148×5 px,
/// odwrócona w pionie na kotwicy lewej/prawej krawędzi) bez żadnego tekstu.
/// Dzieli się na dwa paski: jeden to okno 5 h, drugi 7 dni - każdy rośnie od
/// swojego skrajnego końca ku środkowi i zatrzymuje się na znaczniku stanu,
/// który jest granicą między nimi (nie spotykają się na środku).
/// Pośrodku siedzi więc mały, barwny znacznik najważniejszej sesji - pulsuje
/// przy pracy, miga przy błędzie/prośbie o zgodę, stoi w miejscu przy reszcie.
/// </summary>
public sealed class RestRenderer : IDisposable
{
    private readonly LayerBitmap _layer = new();

    public Bitmap Render(FontSet fonts, ResourceCache resources, FrameInput frame)
    {
        var s = fonts.Scale;
        var vertical = frame.Vertical;
        var w = s.PxInt(vertical ? Design.RestH : Design.RestW);
        var h = s.PxInt(vertical ? Design.RestW : Design.RestH);

        var bitmap = _layer.Ensure(w, h, PixelFormat.Format32bppRgb);
        using var c = Canvas.ForLayer(bitmap, resources, Palette.Black);

        var five = frame.Meters.FiveHour ?? (float)(frame.Usage?.FiveHour?.Used ?? 0);
        var seven = frame.Meters.SevenDay ?? (float)(frame.Usage?.SevenDay?.Used ?? 0);
        var hasUsage = frame.Usage?.HasAnyWindow ?? false;

        // paski nie dochodzą do środka - zatrzymują się na znaczniku stanu, bo to on
        // jest granicą między oknem 5 h a 7 dni
        var bar = BarLen(s, vertical ? h : w);

        if (vertical)
        {
            if (hasUsage && frame.Usage!.FiveHour is not null)
            {
                c.VBar(0, 0, w, bar, five, Palette.ForUsage(five, Palette.Blue), Design.RestTrackAlpha, false, BarAnchor.Start);
            }
            else
            {
                c.FillRounded(Palette.White(Design.RestTrackAlpha), 0, 0, w, bar, w / 2f);
            }

            if (hasUsage && frame.Usage!.SevenDay is not null)
            {
                c.VBar(0, h - bar, w, bar, seven, Palette.ForUsage(seven, Palette.Green), Design.RestTrackAlpha, false, BarAnchor.End);
            }
            else
            {
                c.FillRounded(Palette.White(Design.RestTrackAlpha), 0, h - bar, w, bar, w / 2f);
            }
        }
        else
        {
            if (hasUsage && frame.Usage!.FiveHour is not null)
            {
                c.Bar(0, 0, bar, h, five, Palette.ForUsage(five, Palette.Blue), Design.RestTrackAlpha, false, BarAnchor.Start);
            }
            else
            {
                c.FillRounded(Palette.White(Design.RestTrackAlpha), 0, 0, bar, h, h / 2f);
            }

            if (hasUsage && frame.Usage!.SevenDay is not null)
            {
                c.Bar(w - bar, 0, bar, h, seven, Palette.ForUsage(seven, Palette.Green), Design.RestTrackAlpha, false, BarAnchor.End);
            }
            else
            {
                c.FillRounded(Palette.White(Design.RestTrackAlpha), w - bar, 0, bar, h, h / 2f);
            }
        }

        DrawMarker(c, s, frame, w, h, vertical);
        return bitmap;
    }

    /// <summary>
    /// Długość jednego paska limitu: od końca linii do znacznika (z odstępem).
    /// Liczona z faktycznej długości warstwy, nie ze stałej, żeby oba paski były
    /// równe co do piksela niezależnie od zaokrągleń DPI.
    /// </summary>
    private static float BarLen(DpiScale s, float len)
        => Math.Max(0f, (len - s.Px(Design.RestMarkerLen)) / 2f - s.Px(Design.RestMarkerGap));

    /// <summary>Barwny znacznik stanu na środku - jedyny element brzegu, który mówi coś o sesji.</summary>
    private static void DrawMarker(Canvas c, DpiScale s, FrameInput frame, int w, int h, bool vertical)
    {
        var style = frame.Summary.Style;
        var color = style.Color.With(StateAnimation.RingAlpha(style, frame.TimeMs));
        var (x, y, mw, mh, r) = MarkerRect(s, w, h, vertical);
        c.FillRounded(color, x, y, mw, mh, r);
    }

    /// <summary>Prostokąt znacznika (środek brzegu) we współrzędnych warstwy brzegu.</summary>
    private static (float X, float Y, float W, float H, float R) MarkerRect(DpiScale s, float w, float h, bool vertical)
    {
        var markerLen = s.Px(Design.RestMarkerLen);
        return vertical
            ? (0, h / 2f - markerLen / 2f, w, markerLen, Math.Min(w, markerLen) / 2f)
            : (w / 2f - markerLen / 2f, 0, markerLen, h, Math.Min(markerLen, h) / 2f);
    }

    /// <summary>
    /// Migająca poświata wokół znacznika - tylko dla stanów "czeka na Ciebie"/"błąd" (te same,
    /// co migają), żeby dało się je zauważyć kątem oka zanim ktoś sięgnie kursorem po pasek.
    /// Rysowana na zewnętrznej powłoce (z zapasem cienia), bo sam brzeg jest za cienki (5 px),
    /// żeby pomieścić rozlewającą się poświatę - gaśnie razem z miganiem znacznika.
    /// </summary>
    public static void DrawGlow(Canvas c, DpiScale s, FrameInput frame, float ox, float oy, float w, float h)
    {
        var style = frame.Summary.Style;
        if (style.Glyph != Glyph.Blink || !style.IsBlinkOnAt(frame.TimeMs) || frame.RestAlpha <= 0) return;

        var (x, y, mw, mh, r) = MarkerRect(s, w, h, frame.Vertical);
        var step = s.Px(Design.RestGlowStep);
        var alpha = Design.RestGlowAlpha * frame.RestAlpha;

        for (var k = Design.RestGlowRings; k >= 1; k--)
        {
            var g = step * k;
            c.FillRounded(style.Color.With(alpha), ox + x - g, oy + y - g, mw + 2 * g, mh + 2 * g, r + g);
        }
    }

    public void Dispose() => _layer.Dispose();
}
