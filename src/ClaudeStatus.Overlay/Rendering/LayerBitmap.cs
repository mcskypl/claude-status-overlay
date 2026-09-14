using System.Drawing.Imaging;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// Bitmapa wielokrotnego użytku - alokowana od nowa tylko, gdy zmieni się
/// rozmiar albo format. Warstwy (pastylka, panel, cień, powierzchnia) mają po jednej.
/// </summary>
public sealed class LayerBitmap : IDisposable
{
    private Bitmap? _bitmap;

    public Bitmap Ensure(int width, int height, PixelFormat format = PixelFormat.Format32bppArgb)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_bitmap is null || _bitmap.Width != width || _bitmap.Height != height || _bitmap.PixelFormat != format)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(width, height, format);
        }
        return _bitmap;
    }

    public Bitmap? Current => _bitmap;

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
