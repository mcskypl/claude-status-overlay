using ClaudeStatus.Overlay.Animation;
using ClaudeStatus.Overlay.Placement;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>Gotowa klatka: bitmapa z cieniem i rozmiar samego widgetu w niej.</summary>
public sealed record RenderedFrame(Bitmap Surface, Size ContentSize, int Margin);

/// <summary>
/// Składa klatkę: trzy poziomy (brzeg, pasek, panel) są rysowane w naturalnych
/// rozmiarach, tylko gdy choć trochę widoczne, a potem przenikają się - każdy
/// ze swoją alfą - w morfującej powłoce z cieniem.
///
/// To ten obiekt zna naturalny rozmiar każdego poziomu (mierzony tekst paska,
/// wysokość panelu zależna od liczby sesji) i sam nim steruje przez wewnętrzny
/// <see cref="ShapeMorph"/> - wywołujący (<c>OverlayForm</c>) mówi mu tylko,
/// do którego poziomu aktualnie zmierzać (<see cref="FrameInput.TargetStage"/>)
/// i jak widoczny ma być każdy z nich; o pikselach decyduje renderer.
///
/// Trzyma wszystkie zasoby zależne od DPI (fonty, cache) i odtwarza je przy
/// zmianie skali.
/// </summary>
public sealed class WidgetRenderer : IDisposable
{
    private const float VisibleAlpha = 0.001f;

    private readonly ResourceCache _resources = new();
    private readonly RestRenderer _rest = new();
    private readonly SlimRenderer _slim = new();
    private readonly PanelRenderer _panel = new();
    private readonly ShadowRenderer _shadow = new();
    private readonly LayerBitmap _surface = new();
    private readonly Graphics _measureGraphics = Graphics.FromHwnd(IntPtr.Zero);
    private readonly Canvas _measure;
    private readonly ShapeMorph _shape;

    private FontSet _fonts;
    private RectangleF _refreshHit;   // przycisk odświeżenia z ostatniej klatki, w układzie widgetu
    private RectangleF _updateHit;    // wiersz "nowa wersja" z ostatniej klatki

    public WidgetRenderer(DpiScale scale)
    {
        _fonts = new FontSet(scale);
        _measure = new Canvas(_measureGraphics, _resources, ownsGraphics: false);
        _shape = new ShapeMorph(Design.MorphMs, RestShape(scale, vertical: false));
    }

    public DpiScale Scale => _fonts.Scale;

    public int Margin => Scale.PxInt(Design.ShadowMargin);

    public void SetScale(DpiScale scale)
    {
        if (scale == _fonts.Scale) return;
        var factor = scale.Factor / _fonts.Scale.Factor;
        _fonts.Dispose();
        _fonts = new FontSet(scale);
        _shape.Rescale(factor);   // bez animowania - DPI zmienia się skokowo, nie w odpowiedzi na interakcję
    }

    public RenderedFrame Render(FrameInput frame)
    {
        var margin = Margin;
        var s = Scale;
        _refreshHit = RectangleF.Empty;
        _updateHit = RectangleF.Empty;

        // 1. warstwy w naturalnych rozmiarach - tylko te choć trochę widoczne
        //    (Slim mierzymy też wtedy, gdy to on jest celem morfingu, żeby kształt
        //    miał dokąd zmierzać, zanim jego własny fade zdąży ruszyć z zera)
        var restBitmap = frame.RestAlpha > VisibleAlpha ? _rest.Render(_fonts, _resources, frame) : null;

        SlimLayout? slimLayout = null;
        Bitmap? slimBitmap = null;
        if (frame.TargetStage == WidgetStage.Slim || frame.SlimAlpha > VisibleAlpha)
        {
            slimLayout = _slim.Measure(_measure, _fonts, frame);
            if (frame.SlimAlpha > VisibleAlpha) slimBitmap = _slim.Render(slimLayout, _fonts, _resources, frame);
        }

        var panelBitmap = frame.PanelAlpha > VisibleAlpha ? _panel.Render(_fonts, _resources, frame) : null;

        // 2. kształt powłoki - cel zależy od tego, dokąd zmierzamy, nie od tego, co akurat widać
        _shape.SetTarget(TargetShape(frame, s, slimLayout), frame.TimeMs);
        _shape.Update(frame.TimeMs);
        var shell = _shape.Current;
        var radii = WidgetPlacement.RadiiFor(frame.Edge, shell.R);

        var contentW = (int)Math.Ceiling(shell.W);
        var contentH = (int)Math.Ceiling(shell.H);
        var surfaceW = contentW + 2 * margin;
        var surfaceH = contentH + 2 * margin;

        // 3. kompozycja
        var surface = _surface.Ensure(surfaceW, surfaceH);
        using (var c = Canvas.ForLayer(surface, _resources))
        {
            // im mniej brzegu, tym bardziej "podniesiony" cień - box-shadow w prototypie
            // też nie jest animowany, więc próg zamiast płynnego mieszania
            var shadowStyle = frame.RestAlpha >= 0.5f ? ShadowStyle.Soft : ShadowStyle.Strong;
            _shadow.Draw(c, margin, margin, shell.W, shell.H, radii, surfaceW, surfaceH, s, shadowStyle);

            if (frame.TargetStage == WidgetStage.Rest)
            {
                RestRenderer.DrawGlow(c, s, frame, margin, margin, shell.W, shell.H);
            }

            using var clip = RoundedRect.Create(margin, margin, shell.W, shell.H, radii);
            c.Graphics.SetClip(clip);
            c.Graphics.FillPath(_resources.Brush(Palette.Black), clip);

            if (restBitmap is not null && frame.RestAlpha > VisibleAlpha)
            {
                DrawCentered(c, restBitmap, margin, shell, frame.RestAlpha);
            }
            if (slimBitmap is not null && frame.SlimAlpha > VisibleAlpha)
            {
                DrawCentered(c, slimBitmap, margin, shell, frame.SlimAlpha);
            }
            if (panelBitmap is not null && frame.PanelAlpha > VisibleAlpha)
            {
                // skaluje się z transform-origin 50% 0% - rośnie od góry, nie ze środka
                var dw = panelBitmap.Width * frame.PanelScale;
                var dh = panelBitmap.Height * frame.PanelScale;
                var dx = (shell.W - dw) / 2;
                c.Layer(panelBitmap, margin + dx, margin, dw, dh, frame.PanelAlpha);

                var k = frame.PanelScale;
                var button = PanelRenderer.RefreshButtonRect(s, frame, panelBitmap.Width, panelBitmap.Height);
                if (!button.IsEmpty)
                {
                    _refreshHit = new RectangleF(dx + button.X * k, button.Y * k, button.Width * k, button.Height * k);
                }

                var update = PanelRenderer.UpdateRowRect(s, frame, panelBitmap.Width, panelBitmap.Height);
                if (!update.IsEmpty)
                {
                    _updateHit = new RectangleF(dx + update.X * k, update.Y * k, update.Width * k, update.Height * k);
                }
            }

            c.Graphics.ResetClip();

            // wewnętrzny hairline 0.5 px rgba(255,255,255,.08)
            using var hairline = RoundedRect.Create(margin + 0.5f, margin + 0.5f, shell.W - 1, shell.H - 1, radii);
            c.Graphics.DrawPath(_resources.Pen(Palette.White(0.08), 1f), hairline);
        }

        return new RenderedFrame(surface, new Size(contentW, contentH), margin);
    }

    private static Shape TargetShape(FrameInput frame, DpiScale s, SlimLayout? slimLayout) => frame.TargetStage switch
    {
        WidgetStage.Rest => RestShape(s, frame.Vertical),
        WidgetStage.Slim => SlimShape(s, frame.Vertical, slimLayout!.MainLen),
        _ => new Shape(s.Px(Design.PanelW), PanelRenderer.MeasureHeight(s, frame), s.Px(Design.RPanel)),
    };

    private static Shape RestShape(DpiScale s, bool vertical) => vertical
        ? new Shape(s.Px(Design.RestH), s.Px(Design.RestW), s.Px(Design.RRest))
        : new Shape(s.Px(Design.RestW), s.Px(Design.RestH), s.Px(Design.RRest));

    private static Shape SlimShape(DpiScale s, bool vertical, float mainLen) => vertical
        ? new Shape(s.Px(Design.SlimH), mainLen, s.Px(Design.RSlim))
        : new Shape(mainLen, s.Px(Design.SlimH), s.Px(Design.RSlim));

    /// <summary>Warstwa bez skalowania, wyśrodkowana w bieżącym kształcie powłoki.</summary>
    private static void DrawCentered(Canvas c, Bitmap layer, int margin, Shape shell, float alpha)
    {
        var x = margin + (shell.W - layer.Width) / 2f;
        var y = margin + (shell.H - layer.Height) / 2f;
        c.Layer(layer, x, y, layer.Width, layer.Height, alpha);
    }

    /// <summary>Indeks wiersza panelu pod punktem (w układzie widgetu) albo -1.</summary>
    public int RowIndexAt(PointF pointInContent, int sessionCount)
        => PanelRenderer.RowIndexAt(Scale, pointInContent, sessionCount);

    /// <summary>
    /// Czy punkt (w układzie widgetu) trafia w przycisk odświeżenia limitów.
    /// Bierzemy prostokąt z ostatnio narysowanej klatki, więc obszar klikalny
    /// zawsze pokrywa się z tym, co widać - także w trakcie animacji panelu.
    /// </summary>
    public bool RefreshButtonHit(PointF pointInContent)
        => !_refreshHit.IsEmpty && _refreshHit.Contains(pointInContent);

    /// <summary>Czy punkt (w układzie widgetu) trafia w wiersz "nowa wersja" na dole panelu.</summary>
    public bool UpdateRowHit(PointF pointInContent)
        => !_updateHit.IsEmpty && _updateHit.Contains(pointInContent);

    public void Dispose()
    {
        _measure.Dispose();
        _measureGraphics.Dispose();
        _fonts.Dispose();
        _rest.Dispose();
        _slim.Dispose();
        _panel.Dispose();
        _shadow.Dispose();
        _surface.Dispose();
        _resources.Dispose();
    }
}
