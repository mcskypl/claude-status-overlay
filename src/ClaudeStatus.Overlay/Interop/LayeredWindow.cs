using static ClaudeStatus.Overlay.Interop.NativeMethods;

namespace ClaudeStatus.Overlay.Interop;

/// <summary>
/// Okno bez ramki, którego cała zawartość pochodzi z bitmapy ARGB wpychanej
/// przez UpdateLayeredWindow. Zwykła forma nie umie cienia z rozmyciem ani
/// wygładzonych rogów (region tnie piksele twardo) - alfa per piksel załatwia
/// rogi, cień i przenikanie pastylki z panelem.
/// </summary>
public class LayeredWindow : Form
{
    public LayeredWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = false;
    }

    /// <summary>
    /// Prostokąt samego widgetu w bitmapie - wokół niego jest tylko cień, więc
    /// kliknięcia stamtąd oddajemy oknu pod spodem.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Rectangle ContentBounds { get; set; } = Rectangle.Empty;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    /// <summary>
    /// Prawdziwa pozycja okna na ekranie. UpdateLayeredWindow przesuwa okno bez
    /// WM_WINDOWPOSCHANGED, więc Left/Top z WinForms bywają nieaktualne -
    /// pytamy system.
    /// </summary>
    public Point ScreenOrigin
    {
        get
        {
            if (!IsHandleCreated || !GetWindowRect(Handle, out var rect)) return Location;
            return new Point(rect.Left, rect.Top);
        }
    }

    /// <summary>Przesunięcie bez zmiany rozmiaru i kolejności (przeciąganie).</summary>
    public void MoveTo(Point origin)
    {
        if (!IsHandleCreated) return;
        SetWindowPos(Handle, IntPtr.Zero, origin.X, origin.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// "Zawsze na wierzchu" ustawiane wprost. Form.TopMost przed utworzeniem
    /// uchwytu nie zawsze przekłada się na WS_EX_TOPMOST (okno bez paska zadań
    /// dostaje ukrytego właściciela), więc nie polegamy na nim.
    /// </summary>
    public void ApplyTopMost(bool topMost)
    {
        if (!IsHandleCreated) return;
        SetWindowPos(Handle, topMost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST && !ContentBounds.IsEmpty)
        {
            var lp = unchecked((int)(long)m.LParam);
            var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            if (!ContentBounds.Contains(PointToClient(screen)))
            {
                m.Result = HTTRANSPARENT;
                return;
            }
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// Wypycha bitmapę do okna; rozmiar okna staje się rozmiarem bitmapy.
    /// Podana pozycja jest ustawiana w tym samym wywołaniu - pozycja, rozmiar
    /// i obraz zmieniają się atomowo, więc panel rosnący "w górę" nie skacze.
    /// </summary>
    public unsafe void Push(Bitmap surface, Point? location, byte opacity = 255)
    {
        if (!IsHandleCreated) return;

        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var hBitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            hBitmap = surface.GetHbitmap(Color.FromArgb(0));
            previous = SelectObject(memory, hBitmap);

            var size = new SIZE { cx = surface.Width, cy = surface.Height };
            var source = new POINT();
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = opacity,
                AlphaFormat = AC_SRC_ALPHA,
            };

            var destination = new POINT { x = location?.X ?? 0, y = location?.Y ?? 0 };
            var pptDst = location is null ? IntPtr.Zero : (IntPtr)(&destination);
            UpdateLayeredWindow(Handle, screen, pptDst, ref size, memory, ref source, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                SelectObject(memory, previous);
                DeleteObject(hBitmap);
            }
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
