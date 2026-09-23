using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>Parametry cienia, jak we właściwości CSS box-shadow: przesunięcie, spread, rozmycie, krycie.</summary>
public readonly record struct ShadowStyle(int OffsetY, int Spread, int Blur, double Alpha, double MinPeakAlpha)
{
    /// <summary>0 18px 40px -14px rgba(0,0,0,.85) - pasek i panel.</summary>
    public static readonly ShadowStyle Strong = new(18, -14, 40, 0.85, 0.40);

    /// <summary>0 4px 14px -6px rgba(0,0,0,.7) - brzeg: dużo subtelniejszy, ledwie odrywa linię od tła.</summary>
    public static readonly ShadowStyle Soft = new(4, -6, 14, 0.70, 0.18);
}

/// <summary>
/// Cień robimy tak, jak przeglądarka: kształt widgetu zmniejszony o spread
/// i przesunięty w dół rozmywamy Gaussem o σ = Blur/2 (Gauss to trzy przebiegi
/// box blur na samym kanale alfa - kolor jest jeden, czarny). Cień zależy tylko
/// od kształtu i stylu, więc poza zmianą jednego z nich bierzemy go z cache.
/// </summary>
/// <remarks>
/// Morfing zmienia kształt w każdej klatce, więc cache trafia tylko poza animacją -
/// a właśnie w animacji liczy się czas klatki. Dlatego cień powstaje w skali
/// pomniejszonej (<see cref="StepFor"/>) i dopiero przy rysowaniu wraca do pełnego
/// rozmiaru: rozmycie o σ kilkunastu pikseli nie ma żadnych szczegółów, które
/// mogłaby zgubić czterokrotnie mniejsza siatka, a pracy jest szesnaście razy mniej.
/// Zmierzone na panelu: 8-28 ms na klatkę przed zmianą, poniżej 2 ms po niej.
/// </remarks>
public sealed class ShadowRenderer : IDisposable
{
    private const int BlurPasses = 3;

    /// <summary>Docelowa σ w pomniejszonej siatce - poniżej tego rozmycie zaczyna być kanciaste.</summary>
    private const double SigmaPerStep = 5;

    private readonly LayerBitmap _cache = new();
    private (int, int, int, int, (int, int, int, int), ShadowStyle)? _key;
    private float[] _alpha = [];
    private float[] _scratch = [];
    private bool _empty;

    public void Draw(Canvas target, float x, float y, float w, float h, CornerRadii radii, int bitmapW, int bitmapH,
        DpiScale scale, ShadowStyle style)
    {
        var key = (bitmapW, bitmapH, (int)w, (int)h, radii.Key, style);
        if (_key != key || _cache.Current is null)
        {
            Rebuild(x, y, w, h, radii, bitmapW, bitmapH, scale, target.Resources, style);
            _key = key;
        }

        if (_empty || _cache.Current is not { } shadow) return;

        if (shadow.Width == bitmapW && shadow.Height == bitmapH)
        {
            target.Graphics.DrawImageUnscaled(shadow, 0, 0);
            return;
        }

        // Dwuliniowo, nie dwusześciennie: rozciągamy gładką plamę, więc droższy
        // filtr nie ma czego poprawić, a kosztuje tyle samo co całe rozmycie.
        var previous = target.Graphics.InterpolationMode;
        target.Graphics.InterpolationMode = InterpolationMode.Bilinear;
        target.Graphics.DrawImage(shadow, new Rectangle(0, 0, bitmapW, bitmapH));
        target.Graphics.InterpolationMode = previous;
    }

    /// <summary>
    /// O ile pomniejszamy siatkę cienia. Im szersze rozmycie, tym więcej można
    /// zejść - ale nigdy poniżej <see cref="SigmaPerStep"/> pikseli σ, bo wtedy
    /// rozciągnięcie zaczyna pokazywać schodki.
    /// </summary>
    private static int StepFor(double sigma) => Math.Clamp((int)(sigma / SigmaPerStep), 1, 4);

    private void Rebuild(float x, float y, float w, float h, CornerRadii radii, int bitmapW, int bitmapH,
        DpiScale scale, ResourceCache resources, ShadowStyle style)
    {
        _empty = true;

        var sigma = scale.Px(style.Blur / 2.0);
        var step = StepFor(sigma);
        var gridW = Math.Max(1, (bitmapW + step - 1) / step);
        var gridH = Math.Max(1, (bitmapH + step - 1) / step);
        var bitmap = _cache.Ensure(gridW, gridH);

        // 1. kształt cienia: prostokąt widgetu zmniejszony o spread, przesunięty w dół;
        //    ujemny spread zmniejsza też promień rogów (jak w CSS)
        var inset = scale.Px(-style.Spread);
        var sx = (x + inset) / step;
        var sy = (y + inset + scale.Px(style.OffsetY)) / step;
        var sw = (w - 2 * inset) / step;
        var sh = (h - 2 * inset) / step;

        using (var canvas = Canvas.ForLayer(bitmap, resources))
        {
            if (w - 2 * inset <= 1 || h - 2 * inset <= 1) return;
            using var path = RoundedRect.Create(sx, sy, sw, sh, radii.Grow(-inset).Scale(1f / step));
            canvas.Graphics.FillPath(resources.Brush(Color.Black), path);
        }

        // 2. rozmycie kanału alfa i przemnożenie przez krycie z projektu
        var pixels = gridW * gridH;
        if (_alpha.Length < pixels)
        {
            _alpha = new float[pixels];
            _scratch = new float[pixels];
        }

        var data = bitmap.LockBits(new Rectangle(0, 0, gridW, gridH), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            ReadAlpha(data, gridW, gridH, _alpha);
            GaussianBlur(_alpha, _scratch, gridW, gridH, sigma / step);
            WriteBlack(data, gridW, gridH, _alpha, OpacityFor(_alpha, pixels, style));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        _empty = false;
    }

    /// <summary>
    /// Krycie ze stylu, podbite tak, żeby szczyt cienia sięgnął co najmniej
    /// <see cref="ShadowStyle.MinPeakAlpha"/> - małemu kształtowi (brzeg, dawniej
    /// pastylka) rozmycie zabiera prawie całą masę, więc bez tego byłby ledwie
    /// widoczny mimo krycia z projektu.
    /// </summary>
    private static double OpacityFor(float[] alpha, int count, ShadowStyle style)
    {
        var max = 0f;
        for (var i = 0; i < count; i++)
        {
            if (alpha[i] > max) max = alpha[i];
        }

        var peak = max / 255.0 * style.Alpha;
        if (peak <= 0 || peak >= style.MinPeakAlpha) return style.Alpha;
        return style.Alpha * (style.MinPeakAlpha / peak);
    }

    private static unsafe void ReadAlpha(BitmapData data, int w, int h, float[] alpha)
    {
        for (var y = 0; y < h; y++)
        {
            var row = (byte*)data.Scan0 + y * data.Stride;
            var offset = y * w;
            for (var x = 0; x < w; x++) alpha[offset + x] = row[x * 4 + 3];
        }
    }

    private static unsafe void WriteBlack(BitmapData data, int w, int h, float[] alpha, double opacity)
    {
        for (var y = 0; y < h; y++)
        {
            var row = (uint*)((byte*)data.Scan0 + y * data.Stride);
            var offset = y * w;
            for (var x = 0; x < w; x++)
            {
                var a = (uint)Math.Clamp((int)Math.Round(alpha[offset + x] * opacity), 0, 255);
                row[x] = a << 24;   // ARGB: czarny z alfą
            }
        }
    }

    /// <summary>
    /// Gauss ≈ trzy box blury o promieniach dobranych do σ (Kovesi, "Fast almost-
    /// Gaussian filtering"). Poza bitmapą jest przezroczystość, więc okno
    /// przy brzegu po prostu widzi zera.
    /// </summary>
    private static void GaussianBlur(float[] src, float[] tmp, int w, int h, double sigma)
    {
        foreach (var radius in BoxRadii(sigma, BlurPasses))
        {
            if (radius <= 0) continue;
            BoxBlurHorizontal(src, tmp, w, h, radius);
            BoxBlurVertical(tmp, src, w, h, radius);
        }
    }

    private static int[] BoxRadii(double sigma, int passes)
    {
        var ideal = Math.Sqrt(12 * sigma * sigma / passes + 1);
        var lower = (int)Math.Floor(ideal);
        if (lower % 2 == 0) lower--;
        var upper = lower + 2;
        var m = (int)Math.Round((12 * sigma * sigma - passes * lower * lower - 4 * passes * lower - 3 * passes)
                                / (-4.0 * lower - 4));

        var radii = new int[passes];
        for (var i = 0; i < passes; i++) radii[i] = ((i < m ? lower : upper) - 1) / 2;
        return radii;
    }

    private static void BoxBlurHorizontal(float[] src, float[] dst, int w, int h, int r)
    {
        var norm = 1f / (2 * r + 1);
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            var sum = 0f;
            for (var x = 0; x <= Math.Min(r, w - 1); x++) sum += src[row + x];

            for (var x = 0; x < w; x++)
            {
                dst[row + x] = sum * norm;
                var leaving = x - r;
                var entering = x + r + 1;
                if (leaving >= 0) sum -= src[row + leaving];
                if (entering < w) sum += src[row + entering];
            }
        }
    }

    private static void BoxBlurVertical(float[] src, float[] dst, int w, int h, int r)
    {
        var norm = 1f / (2 * r + 1);
        for (var x = 0; x < w; x++)
        {
            var sum = 0f;
            for (var y = 0; y <= Math.Min(r, h - 1); y++) sum += src[y * w + x];

            for (var y = 0; y < h; y++)
            {
                dst[y * w + x] = sum * norm;
                var leaving = y - r;
                var entering = y + r + 1;
                if (leaving >= 0) sum -= src[leaving * w + x];
                if (entering < h) sum += src[entering * w + x];
            }
        }
    }

    public void Dispose() => _cache.Dispose();
}
