using System.Runtime.InteropServices;
using ClaudeStatus.Overlay.Interop;
using ClaudeStatus.Overlay.Placement;

namespace ClaudeStatus.Overlay.UI;

/// <summary>
/// Szkło pod widgetem: osobne okno niosące wyłącznie materiał - rozmycie tego,
/// co naprawdę jest pod spodem, razem z zaokrągleniem rogów. Treść rysuje
/// warstwa nad nim, półprzezroczyście, więc materiał widać przez nią.
/// </summary>
/// <remarks>
/// Dlaczego osobne okno: materiał jest właściwością <b>okna</b> - obejmuje cały
/// jego prostokąt. Widget rysuje się w oknie warstwowym z marginesem na cień,
/// więc materiał założony na nie rozmyłby też ten margines. Osobne okno ma
/// rozmiar dokładnie taki, jak kształt widgetu.
///
/// Dlaczego czarne tło: materiał potrzebuje okna z przezroczystą treścią. Ramka
/// rozciągnięta na całą powierzchnię klienta sprawia, że zamalowana w niej czerń
/// jest dla kompozytora przezroczysta - i przez to widać rozmycie.
///
/// Sam materiał zakłada <see cref="WindowMaterial"/>; tam też jest opisane,
/// dlaczego nie tą drogą, którą poleca dokumentacja.
///
/// Okno nie przyjmuje ani kliknięć, ani aktywności.
/// </remarks>
internal sealed class GlassWindow : Form
{
    /// <summary>Promień, który nakłada DWM - nie da się go zmienić, więc reszta kształtu się do niego dopasowuje.</summary>
    public const int Radius = WindowMaterial.Radius;

    /// <summary>
    /// Przyciemnienie nakładane na rozmycie, w formacie AABBGGRR.
    /// </summary>
    /// <remarks>
    /// Tłumienie jest dwustopniowe: najpierw ten tint na rozmyciu, potem powłoka
    /// widgetu (<see cref="Rendering.Design.GlassAlpha"/>). Przy 0xA0 i 0,68 do
    /// wnętrza docierało ~12 % tła - za mało, żeby to wyglądało jak szkło.
    ///
    /// Czytelność na tym nie cierpi tak, jak mogłoby się wydawać: przebieg
    /// <c>Translucent</c> skaluje alfę JEDNOSTAJNIE, więc różnica między literą
    /// a tłem pod nią zostaje równa <c>ink × GlassAlpha</c> niezależnie od tego,
    /// co jest pod spodem. Przy 0,45 to wciąż ~104/255 kontrastu.
    /// </remarks>
    private const uint Tint = 0x50000000;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int w, int h);

    /// <summary>GW_HWNDNEXT - okno leżące bezpośrednio pod zadanym.</summary>
    private const uint GwHwndNext = 2;

    private const int SwpNoActivate = 0x0010, SwpNoCopyBits = 0x0100;

    private Rectangle _bounds;
    private Size _region;

    public GlassWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = "Claude Status - szkło";

        _ = Handle;
    }

    /// <summary>Czy system dał materiał; gdy nie - widget rysuje się nieprzezroczyście, jak dotąd.</summary>
    public bool Available { get; private set; }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            const int WsExTransparent = 0x00000020;
            const int WsExNoActivate = 0x08000000;

            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExTransparent | WsExNoActivate;
            return cp;
        }
    }

    /// <summary>
    /// Nic nie malujemy. Akryl z polityki akcentu JEST tłem okna - każdy zamalowany
    /// piksel by go zakrył. Wcześniej stała tu czerń w rozciągniętej ramce (czerń
    /// w takiej ramce jest dla kompozytora przezroczysta), ale to właśnie ta ramka
    /// kazała DWM rysować pod oknem cień - szarą plamę sięgającą 60 px pod
    /// pięciopikselowy brzeg, szerszą i ciemniejszą niż sama pastylka.
    /// </summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Available = ApplyMaterial();
    }

    /// <summary>
    /// Nadaje oknu materiał. Wołane dwa razy: przy tworzeniu uchwytu i jeszcze raz
    /// tuż po pierwszym pokazaniu okna - atrybut założony na okno, którego DWM
    /// jeszcze nie widział, nie zawsze się przyjmuje.
    /// </summary>
    private bool ApplyMaterial() => WindowMaterial.Apply(Handle, Tint, extendFrame: false, noShadow: true);

    /// <summary>
    /// Stawia szkło pod zadanym kształtem. <paramref name="owner"/> wyznacza
    /// kolejność - szkło ma stać dokładnie pod nim.
    /// </summary>
    public void Follow(Rectangle content, Edge edge, IntPtr owner)
    {
        if (!Available || content.Width <= 0 || content.Height <= 0)
        {
            HideGlass();
            return;
        }

        var bounds = Extend(content, edge);
        if (!Visible)
        {
            Bounds = bounds;
            _bounds = bounds;
            RoundCorners(bounds.Size);
            Show();
            ApplyMaterial();
        }

        // Okno ruszamy tylko wtedy, gdy naprawdę jest co poprawić: albo zmienił się
        // prostokąt, albo szkło przestało leżeć bezpośrednio pod widgetem. Samo
        // pytanie o sąsiada w kolejności kosztuje mikrosekundy, a SetWindowPos co
        // klatkę niepotrzebnie mieli materiał.
        var misplaced = GetWindow(owner, GwHwndNext) != Handle;
        if (bounds == _bounds && !misplaced) return;

        _bounds = bounds;
        SetWindowPos(Handle, owner, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SwpNoActivate | SwpNoCopyBits);
        RoundCorners(bounds.Size);
    }

    /// <summary>
    /// Zaokrąglenie własnym regionem zamiast tego od DWM. Powód jest jeden:
    /// w Windows 11 zaokrąglenie DWM przychodzi razem z cieniem okna, a tego
    /// cienia nie da się od niego odpiąć - pod pięciopikselowym brzegiem robił
    /// szarą plamę sięgającą 60 px w dół. Region cienia nie rzuca.
    ///
    /// Krawędź regionu nie jest wygładzana, ale leży dokładnie pod wygładzoną
    /// krawędzią kształtu rysowanego przez widget, więc nie ma jej gdzie widać.
    /// </summary>
    private void RoundCorners(Size size)
    {
        if (size == _region) return;
        _region = size;

        // +1, bo prawa i dolna krawędź regionu są wyłączne.
        var rgn = CreateRoundRectRgn(0, 0, size.Width + 1, size.Height + 1, Radius * 2 + 1, Radius * 2 + 1);
        SetWindowRgn(Handle, rgn, true);   // okno przejmuje region na własność - nie zwalniamy
    }

    public void HideGlass()
    {
        if (Visible) Hide();
    }

    /// <summary>
    /// Bok przylegający do krawędzi ekranu wypychamy poza nią o promień. Zaokrąglone
    /// rogi lądują wtedy poza ekranem, a widoczna krawędź zostaje płaska - dokładnie
    /// tak, jak ścina kształt <see cref="WidgetPlacement.RadiiFor"/>. Promienia DWM
    /// nie da się ustawić, więc to jedyny sposób na płaski bok.
    /// </summary>
    private static Rectangle Extend(Rectangle content, Edge edge) => edge switch
    {
        Edge.Top => new Rectangle(content.X, content.Y - Radius, content.Width, content.Height + Radius),
        Edge.Bottom => new Rectangle(content.X, content.Y, content.Width, content.Height + Radius),
        Edge.Left => new Rectangle(content.X - Radius, content.Y, content.Width + Radius, content.Height),
        Edge.Right => new Rectangle(content.X, content.Y, content.Width + Radius, content.Height),
        _ => content,
    };
}

