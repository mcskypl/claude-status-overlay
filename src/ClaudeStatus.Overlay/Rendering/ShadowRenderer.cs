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
public sealed class ShadowRenderer : IDisposable
{
    private const int BlurPasses = 3;

    private readonly LayerBitmap _cache = new();
    private (int, int, int, int, (int, int, int, int), ShadowStyle)? _key;
    private float[] _alpha = [];
    private float[] _scratch = [];

    public void Draw(Canvas target, float x, float y, float w, float h, CornerRadii radii, int bitmapW, int bitmapH,
        DpiScale scale, ShadowStyle style)
    {
        var key = (bitmapW, bitmapH, (int)w, (int)h, radii.Key, style);
        if (_key != key || _cache.Current is null)
        {
            Rebuild(x, y, w, h, radii, bitmapW, bitmapH, scale, target.Resources, style);
            _key = key;
        }
        target.Graphics.DrawImageUnscaled(_cache.Current!, 0, 0);
    }

    private void Rebuild(float x, float y, float w, float h, CornerRadii radii, int bitmapW, int bitmapH,
        DpiScale scale, ResourceCache resources, ShadowStyle style)
    {
        var bitmap = _cache.Ensure(bitmapW, bitmapH);

        // 1. kształt cienia: prostokąt widgetu zmniejszony o spread, przesunięty w dół;
        //    ujemny spread zmniejsza też promień rogów (jak w CSS)
        var inset = scale.Px(-style.Spread);
        var sx = x + inset;
        var sy = y + inset + scale.Px(style.OffsetY);
        var sw = w - 2 * inset;
        var sh = h - 2 * inset;

        using (var canvas = Canvas.ForLayer(bitmap, resources))
        {
            if (sw <= 1 || sh <= 1) return;
            using var path = RoundedRect.Create(sx, sy, sw, sh, radii.Grow(-inset));
            canvas.Graphics.FillPath(resources.Brush(Color.Black), path);
        }

        // 2. rozmycie kanału alfa i przemnożenie przez krycie z projektu
        var pixels = bitmapW * bitmapH;
        if (_alpha.Length < pixels)
        {
            _alpha = new float[pixels];
            _scratch = new float[pixels];
        }

        var data = bitmap.LockBits(new Rectangle(0, 0, bitmapW, bitmapH), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            ReadAlpha(data, bitmapW, bitmapH, _alpha);
            GaussianBlur(_alpha, _scratch, bitmapW, bitmapH, scale.Px(style.Blur / 2.0));
            WriteBlack(data, bitmapW, bitmapH, _alpha, OpacityFor(_alpha, pixels, style));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
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
