using System.Drawing.Imaging;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// To, co w warstwie zmienia się z samego upływu czasu, w obrębie jednej sekundy:
/// kąt obrotu łuku, krycie pulsowania i fazy migania (bit na wiersz). Warstwa,
/// w której nic się nie animuje, ma tu same zera - więc jej klucz stoi w miejscu
/// i wystarczy narysować ją raz.
/// </summary>
public readonly record struct AnimationPhase(float Spin, float Alpha, ulong Blink)
{
    public static readonly AnimationPhase Still = default;
}

/// <summary>
/// Warstwa rysowana tylko wtedy, gdy zmieniło się cokolwiek, od czego zależą jej
/// piksele.
/// </summary>
/// <remarks>
/// Morfing zmienia kształt powłoki w każdej klatce, ale treść warstw zwykle stoi
/// w miejscu: nazwa sesji nie zmienia się przez to, że panel właśnie rośnie.
/// Dotąd każda klatka morfingu mierzyła tekst i rysowała go od nowa - przy panelu
/// to kilka milisekund na klatkę wyrzucanych na przemalowanie tego samego.
///
/// Klucz to <see cref="FrameInput.ContentKey"/>, czyli prawie cała klatka: jest
/// dobrany tak, żeby pomyłka szła w stronę nadmiarowego rysowania, nie w stronę
/// nieodświeżonego napisu.
/// </remarks>
public sealed class CachedLayer : IDisposable
{
    private readonly LayerBitmap _layer = new();
    private FrameInput? _key;
    private AnimationPhase _phase;
    private bool _drawn;

    /// <summary>Ostatnio narysowana warstwa.</summary>
    public Bitmap Current => _layer.Current
        ?? throw new InvalidOperationException("warstwa nie była jeszcze rysowana");

    /// <summary>
    /// Płótno do narysowania warstwy - albo <c>null</c>, gdy identyczna warstwa
    /// jest już narysowana i wystarczy wziąć <see cref="Current"/>.
    /// </summary>
    public Canvas? BeginDraw(int width, int height, PixelFormat format, Color background,
        ResourceCache resources, FrameInput key, AnimationPhase phase)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_drawn && _layer.Current is { } cached
            && cached.Width == width && cached.Height == height && cached.PixelFormat == format
            && _phase == phase && _key == key)
        {
            return null;
        }

        var bitmap = _layer.Ensure(width, height, format);
        _key = key;
        _phase = phase;
        _drawn = true;
        return Canvas.ForLayer(bitmap, resources, background);
    }

    /// <summary>
    /// Unieważnia cache. Potrzebne przy zmianie DPI: fonty i zasoby są wtedy inne,
    /// a sam rozmiar warstwy nie zawsze się zmienia.
    /// </summary>
    public void Invalidate() => _drawn = false;

    public void Dispose()
    {
        _layer.Dispose();
        _drawn = false;
    }
}
