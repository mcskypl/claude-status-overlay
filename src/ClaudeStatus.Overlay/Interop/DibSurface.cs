using System.Drawing.Imaging;
using static ClaudeStatus.Overlay.Interop.NativeMethods;

namespace ClaudeStatus.Overlay.Interop;

/// <summary>
/// Powierzchnia klatki: jedna sekcja DIB, w którą rysuje GDI+ i którą
/// <c>UpdateLayeredWindow</c> czyta wprost, bez kopii po drodze.
/// </summary>
/// <remarks>
/// Wcześniej każda klatka robiła <c>Bitmap.GetHbitmap</c> - czyli tworzyła nową
/// sekcję DIB i kopiowała do niej całą powierzchnię - plus <c>CreateCompatibleDC</c>
/// i komplet zwolnień. Przy 72 kl./s i powierzchni brzegu 244×101 to był główny
/// koszt klatki, większy niż samo rysowanie.
///
/// Pamięć rośnie tylko w górę i nigdy się nie kurczy, a bitmapa GDI+ jest
/// zakładana na nią z bieżącym rozmiarem i skokiem wiersza pojemności. Dzięki
/// temu morfing, który zmienia rozmiar w każdej klatce, nie alokuje niczego -
/// dotąd realokował bitmapę powłoki ~32 razy na jedno rozwinięcie.
///
/// Format pikseli to <see cref="PixelFormat.Format32bppArgb"/>, taki sam jak
/// dotąd, żeby rysowanie zachowywało się identycznie: alfa jest domnażana do
/// koloru dopiero na końcu, w <c>WidgetRenderer.Translucent</c>, bo GDI - którym
/// idzie cały tekst - alfy nie zna.
/// </remarks>
public sealed class DibSurface : IDisposable
{
    private IntPtr _dc;
    private IntPtr _bitmap;
    private IntPtr _previous;
    private IntPtr _bits;
    private Bitmap? _surface;

    private int _capacityW;
    private int _capacityH;

    /// <summary>Kontekst z założoną sekcją - to jego dostaje <c>UpdateLayeredWindow</c>.</summary>
    public IntPtr Dc => _dc;

    /// <summary>Bitmapa bieżącej klatki, założona na pamięć sekcji.</summary>
    public Bitmap Surface => _surface
        ?? throw new InvalidOperationException("powierzchnia nie była jeszcze przygotowana");

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>
    /// Przygotowuje powierzchnię o zadanym rozmiarze. Sekcja jest tworzona od nowa
    /// tylko wtedy, gdy nie mieści się w dotychczasowej pojemności.
    /// </summary>
    public Bitmap Ensure(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (width > _capacityW || height > _capacityH)
        {
            // Zapas w górę, żeby morfing nie trafiał w realokację przy każdym pikselu.
            Allocate(Grow(Math.Max(width, _capacityW)), Grow(Math.Max(height, _capacityH)));
        }

        if (_surface is null || Width != width || Height != height)
        {
            _surface?.Dispose();
            // Skok wiersza bierzemy z pojemności, nie z bieżącej szerokości - bitmapa
            // jest oknem na lewy górny róg większej sekcji.
            _surface = new Bitmap(width, height, _capacityW * 4, PixelFormat.Format32bppArgb, _bits);
            Width = width;
            Height = height;
        }

        return _surface;
    }

    /// <summary>Zaokrąglenie w górę - drobne zmiany rozmiaru nie ruszają pamięci.</summary>
    private static int Grow(int value) => (value + 63) / 64 * 64;

    private void Allocate(int width, int height)
    {
        Release();

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,   // ujemna wysokość = wiersze od góry, tak jak w GDI+
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };

        var screen = GetDC(IntPtr.Zero);
        try
        {
            _dc = CreateCompatibleDC(screen);
            _bitmap = CreateDIBSection(_dc, ref header, DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero) throw new InvalidOperationException("nie udało się utworzyć sekcji DIB");
            _previous = SelectObject(_dc, _bitmap);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        _capacityW = width;
        _capacityH = height;

        // Bitmapa wskazywała na zwolnioną pamięć - następne Ensure założy nową.
        _surface?.Dispose();
        _surface = null;
        Width = 0;
        Height = 0;
    }

    private void Release()
    {
        if (_dc != IntPtr.Zero && _previous != IntPtr.Zero) SelectObject(_dc, _previous);
        if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
        if (_dc != IntPtr.Zero) DeleteDC(_dc);

        _previous = IntPtr.Zero;
        _bitmap = IntPtr.Zero;
        _dc = IntPtr.Zero;
        _bits = IntPtr.Zero;
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;
        Release();
        _capacityW = 0;
        _capacityH = 0;
        Width = 0;
        Height = 0;
    }
}
