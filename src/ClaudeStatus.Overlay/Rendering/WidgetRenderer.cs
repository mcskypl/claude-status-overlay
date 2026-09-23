using System.Drawing.Imaging;
using ClaudeStatus.Overlay.Animation;
using ClaudeStatus.Overlay.Interop;
using ClaudeStatus.Overlay.Placement;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>Gotowa klatka: powierzchnia z cieniem, rozmiar samego widgetu w niej i promień jego rogów.</summary>
public sealed record RenderedFrame(DibSurface Surface, Size ContentSize, int Margin, float Radius);

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
    private readonly ShadowRenderer _shadow = new();
    private readonly DibSurface _surface = new();
    private readonly Graphics _measureGraphics = Graphics.FromHwnd(IntPtr.Zero);
    private readonly Canvas _measure;
    private readonly ShapeMorph _shape;

    private FontSet _fonts;

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

        // Warstwy z cache są narysowane starymi fontami - a sama treść się nie
        // zmieniła, więc nic by ich nie unieważniło.
        _rest.Invalidate();
        _slim.Invalidate();
    }

    public RenderedFrame Render(FrameInput frame)
    {
        var margin = Margin;
        var s = Scale;

        // Klucz cache warstw - liczony raz na klatkę, bo jest dla wszystkich ten sam.
        var key = frame.ContentKey();

        // 1. warstwy w naturalnych rozmiarach - tylko te choć trochę widoczne
        //    (Slim mierzymy też wtedy, gdy to on jest celem morfingu, żeby kształt
        //    miał dokąd zmierzać, zanim jego własny fade zdąży ruszyć z zera)
        var restBitmap = frame.RestAlpha > VisibleAlpha ? _rest.Render(_fonts, _resources, frame, key) : null;

        SlimLayout? slimLayout = null;
        Bitmap? slimBitmap = null;
        if (frame.TargetStage == WidgetStage.Slim || frame.SlimAlpha > VisibleAlpha)
        {
            slimLayout = _slim.Measure(_measure, _fonts, frame, key);
            if (frame.SlimAlpha > VisibleAlpha) slimBitmap = _slim.Render(slimLayout, _fonts, _resources, frame, key);
        }

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
                var origin = DrawCentered(c, restBitmap, margin, shell, frame.RestAlpha);
                RestRenderer.DrawMarker(c, s, frame, origin.X, origin.Y,
                    restBitmap.Width, restBitmap.Height, frame.Vertical, frame.RestAlpha);
            }
            if (slimBitmap is not null && frame.SlimAlpha > VisibleAlpha)
            {
                var origin = DrawCentered(c, slimBitmap, margin, shell, frame.SlimAlpha);
                SlimRenderer.DrawRing(c, s, frame, slimLayout!, origin.X, origin.Y, frame.SlimAlpha);
            }
            c.Graphics.ResetClip();

            // Wewnętrzny hairline. Na czerni wystarczało .08; na szkle krawędź ma
            // po drugiej stronie żywy obraz, więc musi być mocniejsza, żeby kształt
            // w ogóle się domykał.
            using var hairline = RoundedRect.Create(margin + 0.5f, margin + 0.5f, shell.W - 1, shell.H - 1, radii);
            c.Graphics.DrawPath(_resources.Pen(Palette.White(frame.Glass ? 0.14 : 0.08), 1f), hairline);
        }

        if (frame.Glass) Translucent(surface);

        return new RenderedFrame(_surface, new Size(contentW, contentH), margin, shell.R);
    }

    /// <summary>
    /// Ostatni przebieg: powłoka staje się półprzezroczysta, żeby było widać szkło
    /// pod spodem. Robimy to dopiero teraz, bo GDI - którym rysowany jest cały
    /// tekst - nie zna alfy i zeruje ją przy pisaniu. Rysując od razu na
    /// przezroczystym tle, straciliibyśmy hinting i ClearType; tak zachowujemy
    /// jedno i drugie, a półprzezroczystość dokładamy na gotowe piksele.
    /// Przy okazji domnażamy kolor przez alfę, bo <c>UpdateLayeredWindow</c>
    /// oczekuje wartości wstępnie przemnożonych.
    /// </summary>
    /// <remarks>
    /// Obejmuje CAŁĄ powierzchnię, razem z cieniem - nie samą treść. Gdy dotyczył
    /// tylko treści, obniżenie krycia powłoki zostawiało cień na pełnej sile
    /// i wychodziło z tego coś absurdalnego: cień ciemniejszy niż rzecz, która go
    /// rzuca. Zmierzone nad bielą: pastylka przepuszczała 28 % tła, a cień pod nią
    /// przyciemniał o 39 %. Rzecz półprzezroczysta rzuca lżejszy cień - i tyle.
    /// </remarks>
    private static unsafe void Translucent(Bitmap surface)
    {
        var rect = new Rectangle(0, 0, surface.Width, surface.Height);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var data = surface.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < rect.Height; y++)
            {
                var row = (byte*)data.Scan0 + y * data.Stride;
                for (var x = 0; x < rect.Width; x++)
                {
                    var p = row + x * 4;
                    var alpha = (int)(p[3] * Design.GlassAlpha);
                    if (alpha <= 0)
                    {
                        p[0] = p[1] = p[2] = p[3] = 0;
                        continue;
                    }

                    p[0] = (byte)(p[0] * alpha / 255);
                    p[1] = (byte)(p[1] * alpha / 255);
                    p[2] = (byte)(p[2] * alpha / 255);
                    p[3] = (byte)alpha;
                }
            }
        }
        finally
        {
            surface.UnlockBits(data);
        }
    }

    private static Shape TargetShape(FrameInput frame, DpiScale s, SlimLayout? slimLayout) => frame.TargetStage switch
    {
        WidgetStage.Slim => SlimShape(s, frame.Vertical, slimLayout!.MainLen),
        _ => RestShape(s, frame.Vertical),
    };

    private static Shape RestShape(DpiScale s, bool vertical) => vertical
        ? new Shape(s.Px(Design.RestH), s.Px(Design.RestW), s.Px(Design.RRest))
        : new Shape(s.Px(Design.RestW), s.Px(Design.RestH), s.Px(Design.RRest));

    private static Shape SlimShape(DpiScale s, bool vertical, float mainLen) => vertical
        ? new Shape(s.Px(Design.SlimH), mainLen, s.Px(Design.RSlim))
        : new Shape(mainLen, s.Px(Design.SlimH), s.Px(Design.RSlim));

    /// <summary>Warstwa bez skalowania, wyśrodkowana w bieżącym kształcie powłoki.</summary>
    /// <returns>Lewy górny róg warstwy na powłoce - stąd wiadomo, gdzie dorysować to, co warstwa oddała powłoce.</returns>
    private static PointF DrawCentered(Canvas c, Bitmap layer, int margin, Shape shell, float alpha)
    {
        var x = margin + (shell.W - layer.Width) / 2f;
        var y = margin + (shell.H - layer.Height) / 2f;
        c.Layer(layer, x, y, layer.Width, layer.Height, alpha);
        return new PointF(x, y);
    }

    public void Dispose()
    {
        _measure.Dispose();
        _measureGraphics.Dispose();
        _fonts.Dispose();
        _rest.Dispose();
        _slim.Dispose();
        _shadow.Dispose();
        _surface.Dispose();
        _resources.Dispose();
    }
}
