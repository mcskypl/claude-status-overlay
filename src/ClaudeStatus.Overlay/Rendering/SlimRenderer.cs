using System.Drawing.Imaging;
using ClaudeStatus.Overlay.Model;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// Układ paska (stage Slim) policzony raz na klatkę. <c>MainLen</c> to długość
/// wzdłuż dłuższej osi (szerokość w poziomie, wysokość w pionie) - stała
/// grubość <see cref="Design.SlimH"/> jest tą samą liczbą w obu orientacjach.
/// <c>ContentLen</c> to naturalna (niewymuszona) długość samej treści - w poziomie
/// pasek ma szerokość panelu (<see cref="Design.PanelW"/>), a treść siedzi
/// wyśrodkowana w tym, co zostaje.
/// </summary>
public sealed record SlimLayout(float MainLen, float ContentLen, bool Vertical, bool ShowMeters, int MeterCount);

/// <summary>
/// Pasek: podgląd po najechaniu kursorem, gdy widget jest jeszcze zwinięty do
/// brzegu. Pokazuje mały pierścień stanu, wartość (czas albo nazwa stanu) i -
/// gdy limity są włączone - oba mierniki w linii, z liczbą obok każdego paska.
/// W pionie (kotwica lewa/prawa krawędź) układ się obraca: bez tekstu wartości
/// (nie ma miejsca na czytelny napis w wąskim pasku), za to z liczbą aktywnych
/// sesji na końcu.
/// </summary>
public sealed class SlimRenderer : IDisposable
{
    private readonly LayerBitmap _layer = new();

    public SlimLayout Measure(Canvas measure, FontSet fonts, FrameInput frame)
    {
        var s = fonts.Scale;
        var showMeters = frame.Usage?.HasAnyWindow ?? false;
        var meterCount = frame.MeterCount;

        var contentLen = frame.Vertical
            ? MeasureVertical(measure, fonts, showMeters, meterCount, frame.Sessions.Count)
            : MeasureHorizontal(measure, fonts, frame, showMeters);

        // w poziomie pasek po najechaniu ma tę samą szerokość co panel po kliknięciu
        var mainLen = frame.Vertical ? contentLen : Math.Max(contentLen, s.Px(Design.PanelW));

        return new SlimLayout(mainLen, contentLen, frame.Vertical, showMeters, meterCount);
    }

    private static float MeasureHorizontal(Canvas measure, FontSet fonts, FrameInput frame, bool showMeters)
    {
        var s = fonts.Scale;
        var x = s.Px(Design.SlimPadX) + s.Px(Design.SlimRing);

        var value = frame.Summary.Style.ShortValue(frame.Now - (frame.Summary.Top?.Timestamp ?? frame.Now));
        if (value.Length > 0) x += s.Px(Design.SlimGap) + measure.MeasureWidth(value, fonts.Mono12);

        if (showMeters)
        {
            x += s.Px(Design.SlimGap) + s.Px(Design.SlimDividerW);
            if (frame.Usage!.FiveHour is not null) x += s.Px(Design.SlimGap) + MeasureMeterGroup(measure, fonts, "5h");
            if (frame.Usage.SevenDay is not null) x += s.Px(Design.SlimGap) + MeasureMeterGroup(measure, fonts, "7d");
        }

        return x + s.Px(Design.SlimPadX);
    }

    private static float MeasureMeterGroup(Canvas measure, FontSet fonts, string label)
    {
        var s = fonts.Scale;
        return measure.MeasureWidth(label, fonts.Mono12) + s.Px(Design.SlimMeterGap)
            + s.Px(Design.SlimMeterBarW) + s.Px(Design.SlimMeterGap)
            + measure.MeasureWidth("100%", fonts.Mono12);   // najszersza możliwa wartość - stabilna szerokość
    }

    private static float MeasureVertical(Canvas measure, FontSet fonts, bool showMeters, int meterCount, int sessionCount)
    {
        var s = fonts.Scale;
        var y = s.Px(Design.SlimPadX) + s.Px(Design.SlimRing);
        if (showMeters)
        {
            y += s.Px(Design.SlimGap) + s.Px(Design.SlimDividerW);
            for (var i = 0; i < meterCount; i++) y += s.Px(Design.SlimGap) + s.Px(Design.SlimVBarLen);
        }
        if (sessionCount > 0) y += s.Px(Design.SlimGap) + measure.LineHeight(fonts.Mono11);
        return y + s.Px(Design.SlimPadX);
    }

    public Bitmap Render(SlimLayout layout, FontSet fonts, ResourceCache resources, FrameInput frame)
    {
        var s = fonts.Scale;
        var thickness = s.PxInt(Design.SlimH);
        var w = layout.Vertical ? thickness : (int)Math.Ceiling(layout.MainLen);
        var h = layout.Vertical ? (int)Math.Ceiling(layout.MainLen) : thickness;

        var bitmap = _layer.Ensure(w, h, PixelFormat.Format32bppRgb);
        using var c = Canvas.ForLayer(bitmap, resources, Palette.Black);

        if (layout.Vertical) RenderVertical(c, fonts, frame, w, h);
        else RenderHorizontal(c, fonts, frame, w, h, layout.ContentLen);

        return bitmap;
    }

    private static void RenderHorizontal(Canvas c, FontSet fonts, FrameInput frame, int w, int h, float contentLen)
    {
        var s = fonts.Scale;
        var cy = h / 2f;
        var summary = frame.Summary;
        var style = summary.Style;

        // treść wyśrodkowana w pasku wymuszonym na szerokość panelu, nie przyklejona do lewej
        var x = Math.Max((w - contentLen) / 2f, s.Px(Design.SlimPadX));
        DrawRing(c, s, frame, style, x + s.Px(Design.SlimRing) / 2, cy);
        x += s.Px(Design.SlimRing);

        var value = style.ShortValue(frame.Now - (summary.Top?.Timestamp ?? frame.Now));
        if (value.Length > 0)
        {
            x += s.Px(Design.SlimGap);
            var color = style == StateStyle.Attention || style == StateStyle.Error ? style.Color : Palette.White(0.92);
            c.Text(value, fonts.Mono12, color, x, cy);
            x += c.MeasureWidth(value, fonts.Mono12);
        }

        if (frame.Usage is { HasAnyWindow: true } usage)
        {
            x += s.Px(Design.SlimGap);
            c.FillRect(Palette.White(0.14), x, cy - s.Px(Design.SlimDividerH) / 2, s.Px(Design.SlimDividerW), s.Px(Design.SlimDividerH));
            x += s.Px(Design.SlimDividerW);

            if (usage.FiveHour is { } five)
            {
                x += s.Px(Design.SlimGap);
                x = DrawMeterGroup(c, fonts, x, cy, "5h", frame.Meters.FiveHour ?? (float)five.Used, Palette.Blue,
                    Formats.FiveHourStage(five, frame.Now));
            }
            if (usage.SevenDay is { } seven)
            {
                x += s.Px(Design.SlimGap);
                DrawMeterGroup(c, fonts, x, cy, "7d", frame.Meters.SevenDay ?? (float)seven.Used, Palette.Green,
                    Formats.SevenDayStage(seven, frame.Now));
            }
        }
    }

    /// <returns>Prawa krawędź narysowanej grupy - do ustawienia kolejnego elementu.</returns>
    private static float DrawMeterGroup(Canvas c, FontSet fonts, float x, float cy, string label, float used, Color color,
        float? stage = null)
    {
        var s = fonts.Scale;
        c.Text(label, fonts.Mono12, Palette.White(0.38), x, cy);
        x += c.MeasureWidth(label, fonts.Mono12) + s.Px(Design.SlimMeterGap);

        var barY = cy - s.Px(Design.SlimMeterBarH) / 2;
        var barW = s.Px(Design.SlimMeterBarW);
        c.Bar(x, barY, barW, s.Px(Design.SlimMeterBarH), used, Palette.ForUsage(used, color), 0.14, false);
        if (stage is { } frac) c.BarTick(x, barY, barW, s.Px(Design.SlimMeterBarH), frac);
        x += s.Px(Design.SlimMeterBarW) + s.Px(Design.SlimMeterGap);

        var pct = Formats.UsageUsedShort(used);
        c.Text(pct, fonts.Mono12, Palette.ForUsage(used, Palette.White(0.72)), x, cy);
        return x + c.MeasureWidth(pct, fonts.Mono12);
    }


    private static void RenderVertical(Canvas c, FontSet fonts, FrameInput frame, int w, int h)
    {
        var s = fonts.Scale;
        var cx = w / 2f;
        var style = frame.Summary.Style;

        var y = s.Px(Design.SlimPadX);
        DrawRing(c, s, frame, style, cx, y + s.Px(Design.SlimRing) / 2);
        y += s.Px(Design.SlimRing);

        if (frame.Usage is { HasAnyWindow: true } usage)
        {
            y += s.Px(Design.SlimGap);
            c.FillRect(Palette.White(0.14), cx - s.Px(Design.SlimDividerH) / 2, y, s.Px(Design.SlimDividerH), s.Px(Design.SlimDividerW));
            y += s.Px(Design.SlimDividerW);

            if (usage.FiveHour is not null)
            {
                y += s.Px(Design.SlimGap);
                var used = frame.Meters.FiveHour ?? (float)usage.FiveHour.Used;
                c.VBar(cx - s.Px(Design.SlimMeterBarH) / 2, y, s.Px(Design.SlimMeterBarH), s.Px(Design.SlimVBarLen),
                    used, Palette.ForUsage(used, Palette.Blue), 0.14, false);
                y += s.Px(Design.SlimVBarLen);
            }
            if (usage.SevenDay is not null)
            {
                y += s.Px(Design.SlimGap);
                var used = frame.Meters.SevenDay ?? (float)usage.SevenDay.Used;
                c.VBar(cx - s.Px(Design.SlimMeterBarH) / 2, y, s.Px(Design.SlimMeterBarH), s.Px(Design.SlimVBarLen),
                    used, Palette.ForUsage(used, Palette.Green), 0.14, false);
                y += s.Px(Design.SlimVBarLen);
            }
        }

        if (frame.Sessions.Count > 0)
        {
            y += s.Px(Design.SlimGap);
            var count = frame.Sessions.Count.ToString();
            c.Text(count, fonts.Mono11, Palette.White(0.3), cx - c.MeasureWidth(count, fonts.Mono11) / 2, y + c.LineHeight(fonts.Mono11) / 2f);
        }
    }

    /// <summary>Pionowy pasek limitu rysowany jak <see cref="Canvas.Bar"/>, tylko wzdłuż osi Y.</summary>
    private static void DrawRing(Canvas c, DpiScale s, FrameInput frame, StateStyle style, float cx, float cy)
    {
        var sweep = StateAnimation.SweepFor(style.Glyph);
        var start = style.Glyph == Glyph.Spin ? StateAnimation.SpinStartDeg(frame.TimeMs) : 0f;
        var alpha = StateAnimation.RingAlpha(style, frame.TimeMs);
        c.Ring(cx, cy, s.Px(Design.SlimRing), s.Px(Design.SlimRingHole), style.Color, start, sweep, alpha);
    }

    public void Dispose() => _layer.Dispose();
}
