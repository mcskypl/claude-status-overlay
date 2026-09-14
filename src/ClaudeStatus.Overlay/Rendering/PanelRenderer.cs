using System.Drawing.Imaging;
using ClaudeStatus.Core.Usage;
using ClaudeStatus.Overlay.Model;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// Rozwinięty panel: nagłówek SESJE · N aktywne, wiersze sesji (kropka,
/// projekt, stan, wiek) i pod separatorem mierniki limitów 5 h / 7 dni.
/// </summary>
public sealed class PanelRenderer : IDisposable
{
    private readonly LayerBitmap _layer = new();

    /// <summary>Wysokość zależy od liczby sesji, mierników, wiersza z kontem i paska aktualizacji.</summary>
    public static int MeasureHeight(DpiScale s, FrameInput frame)
    {
        var n = Math.Max(1, frame.Sessions.Count);
        var h = Design.PadT + Design.HeadH + Design.HeadGap + n * Design.RowH + (n - 1) * Design.RowGap;
        if (frame.MeterCount > 0)
        {
            h += Design.SepT + 1 + Design.SepB + frame.MeterCount * Design.MeterH + (frame.MeterCount - 1) * Design.MeterGap;
        }
        if (HasCaption(frame)) h += Design.MeterGap + Design.CaptionH;
        if (frame.Update is not null) h += Design.MeterGap + Design.UpdateH;
        h += Design.PadB;
        return s.PxInt(h);
    }

    /// <summary>Czy panel pokazuje wiersz z kontem i czasem ostatniego odświeżenia limitów.</summary>
    public static bool HasCaption(FrameInput frame) =>
        frame.MeterCount > 0 && (frame.Snapshot.Account?.Email is not null || frame.Usage?.FetchedAt is not null);

    public static float RowsTop(DpiScale s) => s.Px(Design.PadT + Design.HeadH + Design.HeadGap);

    /// <summary>Środek paska o nowej wersji - ostatni wiersz panelu.</summary>
    private static float UpdateCenterY(DpiScale s, float panelH) => panelH - s.Px(Design.PadB) - s.Px(Design.UpdateH) / 2;

    /// <summary>
    /// Środek wiersza z kontem i czasem odświeżenia - liczony od dołu panelu,
    /// ponad paskiem aktualizacji, jeśli akurat jest.
    /// </summary>
    private static float CaptionCenterY(DpiScale s, FrameInput frame, float panelH)
    {
        var bottom = panelH - s.Px(Design.PadB);
        if (frame.Update is not null) bottom -= s.Px(Design.UpdateH) + s.Px(Design.MeterGap);
        return bottom - s.Px(Design.CaptionH) / 2;
    }

    /// <summary>Klikalny wiersz aktualizacji (cała szerokość panelu); pusty, gdy nie ma o czym mówić.</summary>
    public static RectangleF UpdateRowRect(DpiScale s, FrameInput frame, float panelW, float panelH)
    {
        if (frame.Update is null) return RectangleF.Empty;

        var x0 = s.Px(Design.PadX) - s.Px(Design.RowBleed);
        var x1 = panelW - s.Px(Design.PadX) + s.Px(Design.RowBleed);
        var h = s.Px(Design.UpdateH);
        return new RectangleF(x0, UpdateCenterY(s, panelH) - h / 2, x1 - x0, h);
    }

    /// <summary>
    /// Przycisk ręcznego odświeżenia limitów - w prawym dolnym rogu, tuż za napisem
    /// "odświeżono ... temu". Pusty prostokąt, gdy nie ma czego odświeżać (limity
    /// wyłączone albo jeszcze nigdy nie pobrane). Wymiary liczone z rozmiaru panelu,
    /// więc obszar klikalny jest dokładnie tam, gdzie ikona.
    /// </summary>
    public static RectangleF RefreshButtonRect(DpiScale s, FrameInput frame, float panelW, float panelH)
    {
        if (!HasCaption(frame) || frame.Usage?.FetchedAt is null) return RectangleF.Empty;

        var btn = s.Px(Design.RefreshBtn);
        var cy = CaptionCenterY(s, frame, panelH);
        return new RectangleF(panelW - s.Px(Design.PadX) - btn, cy - btn / 2, btn, btn);
    }

    /// <summary>Indeks wiersza pod punktem (w układzie panelu) albo -1.</summary>
    public static int RowIndexAt(DpiScale s, PointF point, int sessionCount)
    {
        var top = RowsTop(s);
        var pitch = s.Px(Design.RowH) + s.Px(Design.RowGap);
        var offset = point.Y - top;
        if (offset < 0) return -1;

        var index = (int)Math.Floor(offset / pitch);
        if (index >= sessionCount) return -1;
        if (offset - index * pitch > s.Px(Design.RowH)) return -1;   // w przerwie między wierszami
        return index;
    }

    public Bitmap Render(FontSet fonts, ResourceCache resources, FrameInput frame)
    {
        var s = fonts.Scale;
        var hasCaption = HasCaption(frame);
        var w = s.PxInt(Design.PanelW);
        var h = MeasureHeight(s, frame);
        var bitmap = _layer.Ensure(w, h, PixelFormat.Format32bppRgb);
        using var c = Canvas.ForLayer(bitmap, resources, Palette.Black);

        var x0 = s.Px(Design.PadX);
        var x1 = w - s.Px(Design.PadX);

        DrawHeader(c, fonts, frame, x0, x1);
        var y = DrawRows(c, fonts, frame, x0, x1);

        if (frame.MeterCount > 0 && frame.Usage is { } usage)
        {
            DrawMeters(c, fonts, frame, usage, x0, x1, y);
        }

        if (hasCaption) DrawCaption(c, fonts, frame, x0, x1, w, h);
        if (frame.Update is { } update) DrawUpdate(c, fonts, frame, update, x0, x1, w, h);

        return bitmap;
    }

    private static void DrawHeader(Canvas c, FontSet fonts, FrameInput frame, float x0, float x1)
    {
        var s = fonts.Scale;
        var cy = s.Px(Design.PadT) + s.Px(Design.HeadH) / 2;
        c.TextTracked("SESJE", fonts.Sans10, Palette.White(0.34), x0, cy, s.Px(Design.HeadTracking));

        var right = frame.Sessions.Count > 0 ? Formats.ActiveCount(frame.Sessions.Count) : "brak sesji";
        c.TextRight(right, fonts.Mono10, Palette.White(0.34), x1, cy);
    }

    /// <returns>Y pod ostatnim wierszem.</returns>
    private static float DrawRows(Canvas c, FontSet fonts, FrameInput frame, float x0, float x1)
    {
        var s = fonts.Scale;
        var y = RowsTop(s);
        var rowH = s.Px(Design.RowH);
        var dotCx = x0 + s.Px(7);
        var nameX = x0 + s.Px(Design.NameIndent);

        if (frame.Sessions.Count == 0)
        {
            var cy = y + rowH / 2;
            c.FillCircle(Palette.White(0.16), dotCx, cy, s.Px(Design.DotR));
            c.Text("brak aktywnych sesji", fonts.Sans13, Palette.White(0.4), nameX, cy);
            return y + rowH;
        }

        for (var i = 0; i < frame.Sessions.Count; i++)
        {
            var session = frame.Sessions[i];
            var style = StateStyle.For(session.State);
            var cy = y + rowH / 2;

            if (i == frame.HoverRow)
            {
                var bleed = s.Px(Design.RowBleed);
                c.FillRounded(Palette.White(0.05), x0 - bleed, y, x1 - x0 + 2 * bleed, rowH, s.Px(Design.RowR));
            }

            DrawDot(c, s, style, frame.TimeMs, dotCx, cy);

            // wiek | stan | nazwa (od prawej, nazwa dostaje resztę)
            var age = Formats.Elapsed(frame.Now - session.Timestamp);
            var ageW = Math.Max(s.Px(26), c.MeasureWidth(age, fonts.Mono11));
            c.TextRight(age, fonts.Mono11, Palette.White(0.3), x1, cy);

            var stateW = c.MeasureWidth(style.Label, fonts.Sans12);
            var stateX = x1 - ageW - s.Px(10) - stateW;
            c.Text(style.Label, fonts.Sans12, style.Color, stateX, cy);

            c.TextClipped(session.DisplayName, fonts.Sans13, Palette.White(0.9), nameX, cy, stateX - s.Px(10) - nameX);

            y += rowH + s.Px(Design.RowGap);
        }

        return y - s.Px(Design.RowGap);
    }

    /// <summary>Kropka z poświatą (brak dla bezczynnej; migająca gaśnie razem z pierścieniem, we własnym tempie).</summary>
    private static void DrawDot(Canvas c, DpiScale s, StateStyle style, double timeMs, float cx, float cy)
    {
        var dot = style.Dot;
        var glow = style.Glyph != Glyph.Track;
        if (style.Glyph == Glyph.Blink && !style.IsBlinkOnAt(timeMs))
        {
            dot = style.Color.With(Design.BlinkDimAlpha);
            glow = false;
        }

        if (glow)
        {
            for (var k = Design.DotGlowRings; k >= 1; k--)
            {
                c.FillCircle(style.Color.With(0.05), cx, cy, s.Px(Design.DotR) + s.Px(Design.DotGlowStep) * k);
            }
        }
        c.FillCircle(dot, cx, cy, s.Px(Design.DotR));
    }

    /// <summary>Mierniki limitów; wiersz pod nimi ustawia się sam od dołu panelu, więc nic nie zwracamy.</summary>
    private static void DrawMeters(Canvas c, FontSet fonts, FrameInput frame, UsageSnapshot usage, float x0, float x1, float y)
    {
        var s = fonts.Scale;

        y += s.Px(Design.SepT);
        c.FillRect(Palette.White(0.08), x0, y, x1 - x0, s.Px(1));
        y += s.Px(1) + s.Px(Design.SepB);

        var mh = s.Px(Design.MeterH);
        var trackX = x0 + s.Px(Design.MeterLabelW) + s.Px(Design.MeterColGap);
        var rightX = x1 - s.Px(Design.MeterRightW);
        var trackW = rightX - s.Px(Design.MeterColGap) - trackX;

        var first = true;
        foreach (var (label, window, color, value, stage) in Meters(usage, frame.Meters, frame.Now))
        {
            if (!first) y += s.Px(Design.MeterGap);
            first = false;

            var cy = y + mh / 2;
            c.Text(label, fonts.Mono11, Palette.White(0.42), x0, cy);
            var barY = cy - s.Px(2);
            var barH = s.Px(4);
            c.Bar(trackX, barY, trackW, barH, value, Palette.ForUsage(value, color), 0.10, usage.Stale);
            if (stage is { } frac) c.BarTick(trackX, barY, trackW, barH, frac);

            // prawa kolumna: procent nad terminem resetu; poniżej progu - na czerwono
            float lineA = c.LineHeight(fonts.Mono11);
            float lineB = c.LineHeight(fonts.Sans95);
            var blockTop = cy - (lineA + s.Px(1) + lineB) / 2;
            c.TextRight(Formats.UsageUsed(window), fonts.Mono11, Palette.ForUsage(value, Palette.White(0.82)), x1, blockTop + lineA / 2);

            // projekt ma tu tracking .02em - przy 9,5 px to 0,19 px, poniżej rozdzielczości GDI
            var reset = Formats.UsageReset(window, frame.Now, usage.Stale);
            c.TextRight(reset, fonts.Sans95, Palette.White(0.3), x1, blockTop + lineA + s.Px(1) + lineB / 2);

            y += mh;
        }
    }

    /// <summary>
    /// Konto zalogowane w Claude Code, wiek ostatniego pobrania limitów i przycisk
    /// ręcznego odświeżenia - ostatni wiersz panelu, przyklejony do jego dołu.
    /// </summary>
    private static void DrawCaption(Canvas c, FontSet fonts, FrameInput frame, float x0, float x1, float w, float h)
    {
        var s = fonts.Scale;
        var cy = CaptionCenterY(s, frame, h);

        var email = frame.Snapshot.Account?.Email;
        if (!string.IsNullOrEmpty(email))
        {
            c.TextClipped(email, fonts.Sans95, Palette.White(0.3), x0, cy, (x1 - x0) * 0.55f);
        }

        if (frame.Usage?.FetchedAt is not { } fetchedAt) return;

        var button = RefreshButtonRect(s, frame, w, h);
        c.TextRight("odświeżono " + Formats.Elapsed(frame.Now - fetchedAt) + " temu", fonts.Sans95, Palette.White(0.3),
            button.Left - s.Px(Design.RefreshGap), cy);
        DrawRefreshButton(c, s, frame, button);
    }

    /// <summary>
    /// Kołowa strzałka w kółku - podświetla się pod kursorem, a gdy leci ręcznie
    /// zamówione pobranie, kręci się tym samym tempem co łuk "pracuje".
    /// </summary>
    private static void DrawRefreshButton(Canvas c, DpiScale s, FrameInput frame, RectangleF button)
    {
        if (button.IsEmpty) return;

        var cx = button.Left + button.Width / 2;
        var cy = button.Top + button.Height / 2;
        if (frame.HoverRefresh)
        {
            c.FillRounded(Palette.White(0.08), button.Left, button.Top, button.Width, button.Height, button.Width / 2);
        }

        var spin = frame.UsageRefreshing ? StateAnimation.SpinStartDeg(frame.TimeMs) : 0f;
        var color = Palette.White(frame.UsageRefreshing ? 0.72 : frame.HoverRefresh ? 0.62 : 0.34);
        c.RefreshGlyph(color, cx, cy, s.Px(Design.RefreshIconR), s.Px(Design.RefreshStroke), spin);
    }

    /// <summary>
    /// Pasek o nowej wersji: kropka w kolorze akcentu, napis i - podczas
    /// pobierania - cienki pasek postępu tuż pod nim. Cały wiersz jest klikalny,
    /// więc pod kursorem podświetla się jak wiersz sesji.
    /// </summary>
    private static void DrawUpdate(Canvas c, FontSet fonts, FrameInput frame, UpdateBanner update,
        float x0, float x1, float w, float h)
    {
        var s = fonts.Scale;
        var row = UpdateRowRect(s, frame, w, h);
        var cy = row.Top + row.Height / 2;

        if (frame.HoverUpdate && update.Actionable)
        {
            c.FillRounded(Palette.White(0.05), row.Left, row.Top, row.Width, row.Height, s.Px(Design.RowR));
        }

        var dotCx = x0 + s.Px(Design.DotR);
        c.FillCircle(update.Actionable ? Palette.Blue : Palette.White(0.35), dotCx, cy, s.Px(Design.UpdateDotR));

        var textX = x0 + s.Px(Design.NameIndent) - s.Px(4);
        var color = update.Actionable
            ? Palette.White(frame.HoverUpdate ? 0.95 : 0.78)
            : Palette.White(0.55);
        c.TextClipped(update.Text, fonts.Sans12, color, textX, cy, x1 - textX);

        if (update.Progress is { } p && p >= 0)
        {
            var barY = row.Bottom - s.Px(Design.UpdateBarH);
            c.Bar(textX, barY, x1 - textX, s.Px(Design.UpdateBarH), Math.Clamp(p * 100, 0, 100), Palette.Blue, 0.10, false);
        }
    }

    private static IEnumerable<(string Label, UsageWindow Window, Color Color, float Value, float? Stage)> Meters(
        UsageSnapshot usage, MeterValues meters, DateTime now)
    {
        if (usage.FiveHour is { } five)
            yield return ("5 h", five, Palette.Blue, meters.FiveHour ?? (float)five.Used, Formats.FiveHourStage(five, now));
        if (usage.SevenDay is { } seven)
            yield return ("7 dni", seven, Palette.Green, meters.SevenDay ?? (float)seven.Used, Formats.SevenDayStage(seven, now));
    }

    public void Dispose() => _layer.Dispose();
}
