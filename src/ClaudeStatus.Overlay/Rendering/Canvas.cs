using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>Od którego końca rośnie wypełnienie paska - większość rośnie od startu, brzeg dzieli się na pół.</summary>
public enum BarAnchor
{
    Start,
    End,
}

/// <summary>
/// Cienka warstwa nad GDI+: tekst wyśrodkowany w pionie, tracking, ellipsis,
/// zaokrąglone prostokąty, pierścień, pasek. Wszystkie współrzędne są już
/// w pikselach ekranu.
///
/// Kształty rysuje GDI+, ale tekst - GDI (<see cref="TextRenderer"/>), bo GDI+
/// na bitmapie umie tylko szare wygładzanie bez porządnego hintingu i przy
/// 10-13 px daje cienkie, poszarpane litery. GDI rysuje tak jak każde okno
/// Windows (ClearType / hinting), ale wymaga nieprzezroczystego tła i nie zna
/// alfy w kolorze - dlatego warstwy z tekstem są czarne i nieprzezroczyste,
/// a kolory z alfą mieszamy z czernią przed rysowaniem.
/// </summary>
public sealed class Canvas : IDisposable
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping;

    private const TextFormatFlags ClippedTextFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
        TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;

    private static readonly Size Unbounded = new(int.MaxValue, int.MaxValue);
    private static readonly ImageAttributes LayerAttributes = new();
    private static readonly ColorMatrix LayerMatrix = new();

    private readonly bool _ownsGraphics;

    public Canvas(Graphics graphics, ResourceCache resources, bool ownsGraphics)
    {
        Graphics = graphics;
        Resources = resources;
        _ownsGraphics = ownsGraphics;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    /// <summary>Płótno nad świeżo wyczyszczoną (przezroczystą) bitmapą - do kompozycji i cienia.</summary>
    public static Canvas ForLayer(Bitmap bitmap, ResourceCache resources) => ForLayer(bitmap, resources, Color.Transparent);

    /// <summary>Płótno nad bitmapą zalaną kolorem; czarne, nieprzezroczyste tło jest warunkiem ładnego tekstu.</summary>
    public static Canvas ForLayer(Bitmap bitmap, ResourceCache resources, Color background)
    {
        var g = Graphics.FromImage(bitmap);
        g.Clear(background);
        return new Canvas(g, resources, ownsGraphics: true);
    }

    public Graphics Graphics { get; }

    public ResourceCache Resources { get; }

    // ------------------------------------------------------------------ tekst

    public float MeasureWidth(string? text, Font font)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        return TextRenderer.MeasureText(Graphics, text, font, Unbounded, TextFlags).Width;
    }

    /// <summary>Wysokość wiersza tekstu w tej czcionce (metryka GDI).</summary>
    public int LineHeight(Font font) => TextRenderer.MeasureText(Graphics, "Ag", font, Unbounded, TextFlags).Height;

    /// <summary>Tekst od lewej, wyśrodkowany w pionie na cy.</summary>
    public void Text(string? text, Font font, Color color, float x, float cy)
    {
        if (string.IsNullOrEmpty(text)) return;
        var top = (int)Math.Round(cy - LineHeight(font) / 2f);
        TextRenderer.DrawText(Graphics, text, font, new Point((int)Math.Round(x), top), OverBlack(color), TextFlags);
    }

    /// <summary>Tekst dosunięty do prawej krawędzi right.</summary>
    public void TextRight(string? text, Font font, Color color, float right, float cy)
    {
        if (string.IsNullOrEmpty(text)) return;
        Text(text, font, color, right - MeasureWidth(text, font), cy);
    }

    /// <summary>Tekst z ellipsis w ograniczonej szerokości.</summary>
    public void TextClipped(string? text, Font font, Color color, float x, float cy, float width)
    {
        if (string.IsNullOrEmpty(text) || width < 8) return;
        var h = LineHeight(font);
        var rect = new Rectangle((int)Math.Round(x), (int)Math.Round(cy - h / 2f), (int)width, h);
        TextRenderer.DrawText(Graphics, text, font, rect, OverBlack(color), ClippedTextFlags);
    }

    /// <summary>Szerokość tekstu z trackingiem (letter-spacing) - GDI nie ma tego natywnie.</summary>
    public float MeasureTracked(string? text, Font font, float tracking)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        var w = 0f;
        foreach (var ch in text) w += MeasureWidth(ch.ToString(), font) + tracking;
        return w - tracking;
    }

    public void TextTracked(string? text, Font font, Color color, float x, float cy, float tracking)
    {
        if (string.IsNullOrEmpty(text)) return;
        var top = (int)Math.Round(cy - LineHeight(font) / 2f);
        var gdiColor = OverBlack(color);
        foreach (var ch in text)
        {
            var s = ch.ToString();
            TextRenderer.DrawText(Graphics, s, font, new Point((int)Math.Round(x), top), gdiColor, TextFlags);
            x += MeasureWidth(s, font) + tracking;
        }
    }

    /// <summary>GDI ignoruje alfę - rgba(255,255,255,.34) na czarnym tle to po prostu szary.</summary>
    private static Color OverBlack(Color c)
    {
        if (c.A == 255) return c;
        return Color.FromArgb(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255);
    }

    // ---------------------------------------------------------------- kształty

    public void FillRounded(Color color, float x, float y, float w, float h, float r)
        => FillRounded(color, x, y, w, h, CornerRadii.Uniform(r));

    public void FillRounded(Color color, float x, float y, float w, float h, CornerRadii radii)
    {
        if (w <= 0 || h <= 0) return;
        using var path = RoundedRect.Create(x, y, w, h, radii);
        Graphics.FillPath(Resources.Brush(color), path);
    }

    public void FillCircle(Color color, float cx, float cy, float radius)
        => Graphics.FillEllipse(Resources.Brush(color), cx - radius, cy - radius, 2 * radius, 2 * radius);

    public void FillRect(Color color, float x, float y, float w, float h)
        => Graphics.FillRectangle(Resources.Brush(color), x, y, w, h);

    /// <summary>
    /// Pierścień: tor + łuk w kolorze stanu + czarny otwór. Kąty w stopniach,
    /// start liczony od godziny 12 zgodnie z ruchem wskazówek.
    /// </summary>
    public void Ring(float cx, float cy, float diameter, float hole, Color color, float startDeg, float sweepDeg, float alpha)
    {
        var rx = cx - diameter / 2;
        var ry = cy - diameter / 2;
        Graphics.FillEllipse(Resources.Brush(Palette.White(0.12 * alpha)), rx, ry, diameter, diameter);

        if (sweepDeg > 0)
        {
            var brush = Resources.Brush(color.With(alpha));
            if (sweepDeg >= 360) Graphics.FillEllipse(brush, rx, ry, diameter, diameter);
            else Graphics.FillPie(brush, rx, ry, diameter, diameter, startDeg - 90, sweepDeg);
        }

        Graphics.FillEllipse(Resources.Brush(Palette.Black), cx - hole / 2, cy - hole / 2, hole, hole);
    }

    /// <summary>Pasek limitu; used 0-100 to ile ZUŻYTO - pasek rośnie razem ze zużyciem, jak w /usage.</summary>
    public void Bar(float x, float y, float w, float h, double used, Color color, double trackAlpha, bool dim,
        BarAnchor anchor = BarAnchor.Start)
    {
        if (w < 4) return;
        FillRounded(color.With(trackAlpha), x, y, w, h, h / 2);

        var fill = (float)(w * Math.Clamp(used, 0, 100) / 100.0);
        if (fill < h)
        {
            if (used <= 0) return;
            fill = h;
        }
        var fx = anchor == BarAnchor.Start ? x : x + (w - fill);
        FillRounded(dim ? color.With(0.5) : color, fx, y, fill, h, h / 2);
    }

    /// <summary>
    /// Cienka pionowa kreska na pasku limitu - znacznik etapu okna czasowego
    /// (np. ile z 5 h sesji już minęło), niezależny od wypełnienia zużyciem.
    /// Wystaje odrobinę ponad/pod pasek, żeby było ją widać na obu tłach.
    /// </summary>
    public void BarTick(float x, float y, float w, float h, float frac)
    {
        if (w < 4) return;
        const float tickW = 2f, overhang = 2f;
        var tx = Math.Clamp(x + w * Math.Clamp(frac, 0f, 1f), x + tickW / 2, x + w - tickW / 2);
        FillRounded(Palette.White(0.85), tx - tickW / 2, y - overhang, tickW, h + 2 * overhang, tickW / 2);
    }

    /// <summary>
    /// Pionowy odpowiednik <see cref="Bar"/> - pasek brzegu i pionowy pasek w pasku
    /// podglądu. Domyślnie rośnie od dołu w górę (tak jak wszystkie pionowe paski
    /// w projekcie: <c>justify-content: flex-end</c>).
    /// </summary>
    public void VBar(float x, float y, float w, float h, double used, Color color, double trackAlpha, bool dim,
        BarAnchor anchor = BarAnchor.End)
    {
        if (h < 4) return;
        FillRounded(color.With(trackAlpha), x, y, w, h, w / 2);

        var fill = (float)(h * Math.Clamp(used, 0, 100) / 100.0);
        if (fill < w)
        {
            if (used <= 0) return;
            fill = w;
        }
        var fy = anchor == BarAnchor.End ? y + (h - fill) : y;
        FillRounded(dim ? color.With(0.5) : color, x, fy, w, fill, w / 2);
    }

    /// <summary>
    /// Kołowa strzałka (ikona odświeżania): łuk z grotem na końcu, wpisany
    /// w koło o promieniu <paramref name="r"/>. <paramref name="rotationDeg"/>
    /// obraca całość - tym kręcimy ikoną, dopóki trwa pobieranie limitów.
    /// </summary>
    public void RefreshGlyph(Color color, float cx, float cy, float r, float stroke, float rotationDeg)
    {
        const float StartDeg = -50f, SweepDeg = 280f;

        Graphics.DrawArc(Resources.Pen(color, stroke), cx - r, cy - r, 2 * r, 2 * r, StartDeg + rotationDeg, SweepDeg);

        // grot na końcu łuku, styczny do okręgu - trójkąt o podstawie na promieniu
        var end = (StartDeg + SweepDeg + rotationDeg) * (float)(Math.PI / 180);
        var (sin, cos) = ((float)Math.Sin(end), (float)Math.Cos(end));
        var px = cx + r * cos;
        var py = cy + r * sin;
        var size = stroke * 1.5f;

        PointF[] head =
        [
            new(px - sin * size * 1.8f, py + cos * size * 1.8f),
            new(px + cos * size, py + sin * size),
            new(px - cos * size, py - sin * size),
        ];
        Graphics.FillPolygon(Resources.Brush(color), head);
    }

    /// <summary>Warstwa (bitmapa) przeskalowana do w×h z globalną alfą.</summary>
    public void Layer(Bitmap layer, float x, float y, float w, float h, float alpha)
    {
        LayerMatrix.Matrix33 = alpha;
        LayerAttributes.SetColorMatrix(LayerMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

        PointF[] corners = [new(x, y), new(x + w, y), new(x, y + h)];
        var source = new RectangleF(0, 0, layer.Width, layer.Height);
        Graphics.DrawImage(layer, corners, source, GraphicsUnit.Pixel, LayerAttributes);
    }

    public void Dispose()
    {
        if (_ownsGraphics) Graphics.Dispose();
    }
}
