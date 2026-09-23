using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClaudeStatus.Core.Sessions;
using ClaudeStatus.Overlay.Animation;
using ClaudeStatus.Overlay.App;
using ClaudeStatus.Overlay.Interop;
using ClaudeStatus.Overlay.Placement;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClaudeStatus.Overlay.Assistant;

/// <summary>
/// Okno asystenta - czwarty poziom pastylki. Bezramkowe, zawsze na wierzchu,
/// wyrasta obok pastylki i chowa się zamiast zamykać, żeby rozmowa przeżyła.
/// Treść rysuje WebView2 z plików <c>Assistant/ui</c>; tutaj zostaje okno,
/// skrót globalny, pozycjonowanie i przekazywanie wiadomości.
/// </summary>
internal sealed class AssistantForm : Form
{
    private const int HotkeyId = 0x4341;
    private const int WmHotkey = 0x0312;
    private const int ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004;
    private const int VkSpace = 0x20;
    /// <summary>
    /// Przyciemnienie nakładane na rozmycie, w formacie AABBGGRR - tyle samo, co
    /// pod dymkiem, bo to ta sama karta rozmowy, tylko większa.
    /// </summary>
    /// <summary>
    /// Przyciemnienie rozmycia, w formacie AABBGGRR - ta sama wartość, co pod
    /// pastylką, bo okno z niej wyrasta i nie może być od niej ciemniejsze.
    /// Drugi stopień tłumienia niesie strona (<c>--tint</c> w assistant.css),
    /// tak jak przy pastylce niesie go powłoka: oba stoją na 0,40.
    /// </summary>
    private const uint Tint = 0x50000000;

    /// <summary>
    /// Promień rogów, który nakłada DWM. Nie da się go zmienić, a obowiązuje
    /// wszystkie cztery rogi razem z rozmyciem - stąd cała reszta kształtu
    /// dopasowuje się do niego, a nie odwrotnie.
    /// </summary>
    private const int DwmRadius = 8;


    /// <summary>
    /// Całe okno asystenta. Było 640 px, gdy obok stał jeszcze panel pastylki
    /// z sesjami i limitami - teraz mierniki siedzą w nagłówku tego okna, więc
    /// samo musi je pomieścić: dwa kafelki po prawej to ~380 px, a po lewej
    /// zostaje tytuł rozmowy z katalogiem roboczym.
    /// </summary>
    private const int LogicalWidth = 880;

    /// <summary>Stała wysokość - nie ma już panelu, którego wysokość trzeba było powtarzać.</summary>
    private const int LogicalHeight = 560;
    private const int LogicalMargin = 12;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    private readonly OverlayConfig _config;
    private readonly SessionMonitor _monitor;
    private readonly OverlayLog _log;
    /// <summary>Docelowy prostokąt panelu pastylki - ten, do którego zmierza morfing, nie bieżący.</summary>
    /// <summary>Gdzie stanęłaby treść tego rozmiaru przy bieżącej kotwicy pastylki.</summary>
    private readonly Func<Size, Rectangle> _place;

    /// <summary>Prostokąt pastylki na ekranie - stąd wyrasta okno i tam wraca.</summary>
    private readonly Func<Rectangle> _pillBounds;
    private readonly AssistantEngine _engine;

    /// <summary>Przezroczysta treść - przez nią widać szkło, które stoi w osobnym oknie pod spodem.</summary>
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };

    private readonly List<string> _pending = [];

    /// <summary>
    /// Postęp wejścia (0 → 1), wygładzony tą samą krzywą, co morfing pastylki.
    /// Liczy go pastylka w swojej klatce i podaje przez <see cref="Advance"/>.
    /// </summary>
    private float _morph;

    /// <summary>Zegar i rytm wjazdu/zjazdu okna - własne, bo nie ma już panelu, z którym trzeba było iść w takt.</summary>
    private readonly FramePacer _morphPacer;
    private readonly Stopwatch _morphClock = new();
    private bool _closing;

    private Edge? _postedEdge;
    private int _postedExtent = -1;
    private int _postedExtentW = -1;
    private bool _webReady;
    private AssistantStatus? _status;
    private Model.UpdateBanner? _update;

    /// <summary>Klik w wiersz o nowej wersji w stopce - pastylka wie, co z tym zrobić.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action? InstallUpdateRequested { get; set; }

    /// <summary>
    /// Powiadomienie o nowej wersji. Stało kiedyś na dole panelu pastylki;
    /// panelu nie ma, więc niesie je stopka rozmowy.
    /// </summary>
    public void ShowUpdate(Model.UpdateBanner? banner)
    {
        _update = banner;
        if (Visible) Post(UpdateMessage(banner));
    }

    private static string UpdateMessage(Model.UpdateBanner? banner) => JsonSerializer.Serialize(new
    {
        type = "update",
        update = banner is null ? null : new { text = banner.Text, actionable = banner.Actionable, progress = banner.Progress },
    });

    public AssistantForm(OverlayConfig config, SessionMonitor monitor, OverlayLog log,
        Func<Size, Rectangle> place, Func<Rectangle> pillBounds)
    {
        _config = config;
        _monitor = monitor;
        _log = log;
        _place = place;
        _pillBounds = pillBounds;
        _engine = new AssistantEngine(config.AssistantModel);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = "Asystent";

        // Nic nie maluje tła - każdy zamalowany piksel zakryłby szkło pod spodem.
        SetStyle(ControlStyles.Opaque, true);

        Size = new Size(LogicalWidth, LogicalHeight);
        Controls.Add(_web);

        // Poza wjazdem i zjazdem wątek klatek śpi bezterminowo.
        _morphPacer = new FramePacer(this, StepMorph, Timeout.Infinite);

        _engine.ToUi += json => Post(json);
        _engine.Log += message => _log.Write("assistant: " + message);
        _engine.StatusChanged += OnEngineStatus;
        _engine.AnswerReady += OnAnswerReady;
        _engine.ModelChanged += model => ModelChanged?.Invoke(model);

        _monitor.Updated += OnSnapshot;

        // Uchwyt musi powstać teraz: bez niego nie zarejestrujemy skrótu, a WebView2
        // nie ma się do czego doczepić. Okno pozostaje niewidoczne.
        _ = Handle;
        _morphPacer.Start();
        _ = InitializeWebAsync();
    }

    /// <summary>Okno nie kradnie Alt+Tab ani paska zadań - to nakładka, nie aplikacja.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow;
            return cp;
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        // Form.Show() jest jedynym miejscem, które ma prawo pokazać to okno;
        // konstruktor tworzy uchwyt, ale okno ma zostać schowane.
        base.SetVisibleCore(_allowVisible && value);
    }

    private bool _allowVisible;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (!RegisterHotKey(Handle, HotkeyId, ModControl | ModShift, VkSpace))
        {
            _log.Write("assistant: nie udalo sie zarejestrowac Ctrl+Shift+Spacja - skrot zajety przez inna aplikacje");
        }

        // Rozmycie i rogi bierze DWM. Oba dotyczą całego okna, więc to prostokąt
        // okna jest kształtem rozmowy - a jego rogi przy krawędzi ekranu chowamy
        // poza nią, żeby góra została płaska, jak przy panelu.
        //
        // Treść rysuje WebView2 na przezroczystym tle, więc ramki nie trzeba
        // rozciągać - okno samo w sobie nie ma czego zamalowywać.
        WindowMaterial.Apply(Handle, Tint, extendFrame: false);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            Toggle();
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Pastylka ma otworzyć panel razem z rozmową (skrót klawiszowy).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action? PanelOpenRequested { get; set; }

    /// <summary>Pastylka ma zwinąć panel - rozmowa i panel gasną razem.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action? PanelCloseRequested { get; set; }

    /// <summary>
    /// Gotowa odpowiedź dla dymka. Leci tylko przy schowanym oknie - dokładnie tak
    /// samo, jak stan „gotowe" na pastylce: to powiadomienie dla kogoś, kto nie patrzy.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<string>? AnswerReady { get; set; }

    /// <summary>Użytkownik zwinął rozmowę przyciskiem w jej nagłówku - wybór ma zostać zapamiętany.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<bool>? ExpandedChanged { get; set; }

    /// <summary>Wybór modelu w nagłówku rozmowy - do zapamiętania; <c>null</c> = z ustawień Claude Code.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<string?>? ModelChanged { get; set; }

    /// <summary>
    /// Utrata aktywności chowa całość - panel i rozmowę - tak samo, jak klik poza
    /// panelem zamykał go wcześniej. Sprawdzenie jest odroczone, bo w chwili
    /// zdarzenia system nie ustawił jeszcze nowego okna pierwszoplanowego.
    /// </summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!Visible) return;

        try
        {
            BeginInvoke(() =>
            {
                if (!Visible || ForegroundIsOurs()) return;
                HideAssistant();
                PanelCloseRequested?.Invoke();
            });
        }
        catch (InvalidOperationException)
        {
            // okno właśnie znika - nie ma czego chować
        }
    }

    /// <summary>
    /// Czy pierwszoplanowe okno nadal należy do tej aplikacji - czyli czy fokus
    /// został w rozmowie albo w pastylce, które od teraz są jedną całością.
    /// Liczy się nie tylko sam formularz: WebView2 trzyma treść w oknach
    /// potomnych należących do procesu przeglądarki, więc klik w rozmowę oddałby
    /// fokus „obcemu" procesowi i okno schowałoby się samo pod palcem.
    /// Rozstrzyga korzeń drzewa okien.
    /// </summary>
    public bool ForegroundIsOurs()
    {
        const int GaRoot = 2;

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        if (foreground == Handle) return true;
        if (GetAncestor(foreground, GaRoot) == Handle) return true;

        // Dialogi tej aplikacji (wybór katalogu, komunikat) też przejmują fokus.
        GetWindowThreadProcessId(foreground, out var processId);
        return processId == Environment.ProcessId;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Zamknięcie z systemu chowa okno; rozmowa ma przeżyć. Wyjątkiem jest
        // zamykanie całej aplikacji.
        if (e.CloseReason is CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideAssistant();
            return;
        }
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------ WebView2

    private async Task InitializeWebAsync()
    {
        if (!AssistantPaths.CanStart(out var problem))
        {
            _log.Write("assistant: " + problem);
            return;
        }

        try
        {
            Directory.CreateDirectory(AssistantPaths.WebViewData);
            var environment = await CoreWebView2Environment.CreateAsync(null, AssistantPaths.WebViewData);
            await _web.EnsureCoreWebView2Async(environment);

            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;

            // Pliki widoku jako wirtualny host: `https://assistant.invalid/` zamiast
            // `file://`. Dzięki temu obowiązuje zwykła polityka pochodzenia i CSP
            // z index.html, a nie luźne reguły schematu plikowego.
            core.SetVirtualHostNameToFolderMapping(
                "assistant.invalid", AssistantPaths.UiDirectory, CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += OnWebMessage;
            core.NavigationCompleted += (_, _) => Flush();

            core.Navigate("https://assistant.invalid/index.html");
            _webReady = true;
        }
        catch (Exception ex)
        {
            _log.Error("assistant", ex);
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }

        try { Dispatch(json); }
        catch (Exception ex) { _log.Error("assistant", ex); }
    }

    /// <summary>Wiadomości, które należą do okna; resztę bierze silnik.</summary>
    private void Dispatch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

        switch (type)
        {
            case "ready":
                Post(_engine.InitMessage(_monitor.Current, AnchorName()));
                Post(AssistantEngine.ShowMessage(true));
                return;

            // Zwinięcie zostawia panel z sesjami - znika sama rozmowa, a wybór
            // zostaje zapamiętany. Esc chowa całość, nie zmieniając preferencji.
            case "collapse":
                ExpandedChanged?.Invoke(false);
                HideAssistant();
                return;

            case "hide":
                HideAssistant();
                PanelCloseRequested?.Invoke();
                return;

            case "copy":
                if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    Clipboard.SetText(text.GetString() ?? "");
                }
                return;

            case "refreshUsage":
                _monitor.RefreshUsage();
                return;

            case "installUpdate":
                InstallUpdateRequested?.Invoke();
                return;
        }

        _engine.HandleUiMessage(json);
    }

    /// <summary>
    /// Klik w wiersz sesji przenosi do jej edytora - to samo, co robił panel
    /// pastylki, zanim zastąpiła go lewa kolumna asystenta.
    /// </summary>
    private void Post(string json)
    {
        if (!IsHandleCreated) return;

        if (InvokeRequired)
        {
            try { BeginInvoke(() => Post(json)); }
            catch (InvalidOperationException) { /* okno właśnie znika */ }
            return;
        }

        if (!_webReady || _web.CoreWebView2 is null)
        {
            _pending.Add(json);
            return;
        }

        try { _web.CoreWebView2.PostWebMessageAsString(json); }
        catch (InvalidOperationException) { _pending.Add(json); }
    }

    private void Flush()
    {
        if (_web.CoreWebView2 is null) return;
        foreach (var json in _pending) _web.CoreWebView2.PostWebMessageAsString(json);
        _pending.Clear();
    }

    // ------------------------------------------------------------------ dane

    private void OnSnapshot(SessionSnapshot snapshot)
    {
        // Monitor tyka co ~0,5 s także wtedy, gdy okno jest schowane - wtedy nie ma
        // komu tego pokazać, a każda wiadomość to skok przez granicę procesu.
        if (!Visible) return;

        // Listy sesji już nie ma - okno niesie limity i konto, a stan sesji
        // pokazuje sama pastylka.
        Post(_engine.LimitsMessage(snapshot));
        Post(_engine.AccountMessage(snapshot));
    }

    // --------------------------------------------------------- stan na pastylce

    private void OnAnswerReady(string answer)
    {
        if (!IsHandleCreated) return;

        if (InvokeRequired)
        {
            try { BeginInvoke(() => OnAnswerReady(answer)); }
            catch (InvalidOperationException) { /* okno właśnie znika */ }
            return;
        }

        if (Visible) return;
        AnswerReady?.Invoke(answer);
    }

    private void OnEngineStatus(AssistantStatus? status)
    {
        if (!IsHandleCreated) return;

        if (InvokeRequired)
        {
            try { BeginInvoke(() => OnEngineStatus(status)); }
            catch (InvalidOperationException) { /* okno właśnie znika */ }
            return;
        }

        _status = status;
        PublishStatus();
    }

    /// <summary>
    /// Asystent trafia na listę sesji jak każda inna sesja Claude Code - tyle że
    /// wprost, bez pliku stanu. Dzięki temu miga na niebiesko dokładnie wtedy,
    /// gdy naprawdę czeka na zgodę, także przy schowanym oknie, i przestaje w tej
    /// samej chwili, w której klikniesz decyzję.
    /// </summary>
    private void PublishStatus()
    {
        // "Gotowe" to powiadomienie dla kogoś, kto nie patrzy. Gdy okno stoi
        // otwarte, sygnał jest skonsumowany w chwili powstania - inaczej pastylka
        // świeciłaby na zielono jeszcze długo po przeczytaniu odpowiedzi.
        if (_status is { State: SessionState.Done } done && Visible)
        {
            _status = done with { State = SessionState.Idle };
        }

        _monitor.Companion = _status is null ? null : AssistantSession.Row(_status, _engine.WorkingDirectory);
    }

    // ------------------------------------------------------------------ okno

    /// <summary>Skrót globalny: otwiera albo zwija całość - panel razem z rozmową.</summary>
    public void Toggle()
    {
        if (Visible)
        {
            HideAssistant();
            PanelCloseRequested?.Invoke();
        }
        else
        {
            // Pokazanie idzie przez pastylkę, a nie wprost: to ona zapamiętuje
            // chwilę startu, od której oba kształty liczą swoje wejście.
            PanelOpenRequested?.Invoke();
        }
    }

    public void ShowAssistant()
    {
        if (!AssistantPaths.CanStart(out var problem))
        {
            MessageBox.Show(problem, "Asystent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _allowVisible = true;
        _closing = false;
        _morph = 0;
        Reposition();
        Show();
        TopMost = true;
        Activate();

        _morphClock.Restart();
        _morphPacer.Active = true;

        Post(AssistantEngine.ShowMessage(true));
        Post(UpdateMessage(_update));
        PublishStatus();
        OnSnapshot(_monitor.Current);
    }

    /// <summary>
    /// Zwinięcie tą samą drogą, którą okno weszło - w odwrotną stronę. Dotąd
    /// znikało natychmiast, bo <c>Hide()</c> szło od razu; wejście miało animację,
    /// wyjście nie.
    /// </summary>
    public void HideAssistant()
    {
        if (!Visible || _closing)
        {
            _allowVisible = false;
            return;
        }

        // Strona wygasza swoją treść od razu, a okno zjeżdża pod nią.
        Post(AssistantEngine.ShowMessage(false));

        _closing = true;
        _morphClock.Restart();
        _morphPacer.Active = true;
    }

    /// <summary>
    /// Jedna klatka wjazdu albo zjazdu. Rozmowa prowadzi to sama, na własnym
    /// zegarze: dopóki obok stał panel pastylki, oba prostokąty musiały ruszać
    /// się w tej samej klatce - panelu nie ma, więc nie ma już czego pilnować.
    /// </summary>
    private void StepMorph()
    {
        if (!Visible) return;

        var progress = (float)Math.Clamp(_morphClock.Elapsed.TotalMilliseconds / Rendering.Design.MorphMs, 0, 1);
        var eased = Easing.OutQuint(progress);
        _morph = _closing ? 1 - eased : eased;
        Reposition();

        if (progress < 1) return;

        _morphPacer.Active = false;
        if (!_closing) return;

        _closing = false;
        _allowVisible = false;
        _morph = 0;
        Hide();
    }

    /// <summary>
    /// Okno staje tuż obok panelu pastylki, z tą samą przerwą 8 px, co między
    /// oknami w projekcie. Strona zależy od miejsca: przy prawej krawędzi ekranu
    /// rozmowa idzie na lewo od panelu, żeby nie wyjść poza obszar roboczy.
    /// </summary>
    /// <remarks>
    /// Prostokąt bierzemy z <b>docelowego</b> panelu, nie z tego, który akurat
    /// morfuje - i ustawiamy go raz. Każda zmiana rozmiaru okna z WebView2 to
    /// przebudowa układu strony i powierzchni kompozytora w procesie
    /// przeglądarki; robiona co klatkę przez 380 ms była tym, co widać jako
    /// zacinanie. Samo wejście rysuje strona (<c>.app</c> ze skalą i przezroczystością
    /// z projektu), a okno tylko odsłania rosnący kształt regionem - to kosztuje
    /// tyle, co przerysowanie gotowej powierzchni.
    /// </remarks>
    private void Reposition()
    {
        var scale = DeviceDpi / 96.0;
        var margin = (int)Math.Round(LogicalMargin * scale);

        var width = Math.Max(1, (int)Math.Round(LogicalWidth * scale));
        var height = Math.Max(1, (int)Math.Round(LogicalHeight * scale));

        // Okno staje tam, gdzie stawał panel - ta sama kotwica, ten sam rachunek,
        // tylko rozmiar własny. Nie ma już dwóch prostokątów do zestawienia obok
        // siebie, więc nie ma też strony, na którą trzeba by uciekać.
        var placed = _place(new Size(width, height));
        var area = Screen.FromRectangle(placed).WorkingArea;
        var edge = WidgetPlacement.EdgeOf(_config.Anchor, _config.Detached);

        height = Math.Clamp(height, 1, area.Height);

        // Bok przylegający do krawędzi ekranu wypychamy poza nią o promień DWM.
        // Zaokrąglone rogi lądują wtedy poza ekranem, a widoczna krawędź zostaje
        // płaska. Strona dostaje tę wartość jako wyściółkę, żeby treść nie
        // wyjechała razem z nimi.
        var inset = (int)Math.Round(DwmRadius * scale);
        var top = Math.Clamp(placed.Top, area.Top, Math.Max(area.Top, area.Bottom - height));

        var full = new Rectangle(
            Math.Clamp(placed.X, area.Left + margin, Math.Max(area.Left + margin, area.Right - margin - width)),
            edge == Edge.Top ? top - inset : top,
            width, edge is Edge.Top or Edge.Bottom ? height + inset : height);

        // Wejście animuje prostokąt okna, bo to on jest kształtem: rozmycie idzie
        // za nim. Startem jest pasek pastylki, nie pełna szerokość - okno ma
        // wyrastać z tego, co widać, a nie rozwijać się z gotowej płachty.
        //
        // Strona tego nie odczuwa: jej układ jest przypięty do rozmiaru docelowego
        // (--extent i --extent-w), więc węższe okno tylko go przycina, zamiast
        // łamać tekst na nowo w każdej klatce.
        var bounds = Grow(_pillBounds(), full, _morph);
        if (bounds != Bounds) Bounds = bounds;

        var extent = (int)Math.Round(full.Height / scale);
        var extentW = (int)Math.Round(full.Width / scale);
        if (edge != _postedEdge || extent != _postedExtent || extentW != _postedExtentW)
        {
            _postedEdge = edge;
            _postedExtent = extent;
            _postedExtentW = extentW;
            Post(ShapeMessage(edge, (int)Math.Round(inset / scale), extent, extentW));
        }
    }

    /// <summary>
    /// Prostokąt w drodze od kształtu pastylki do docelowego. Interpolujemy cały
    /// prostokąt, a nie samą wysokość - dzięki temu każda kotwica zachowuje się
    /// tak samo: okno rozsuwa się z paska w stronę, w którą ma miejsce.
    /// </summary>
    private static Rectangle Grow(Rectangle from, Rectangle to, float t)
    {
        if (t >= 1) return to;
        if (from.Width <= 0 || from.Height <= 0) from = new Rectangle(to.X + to.Width / 2, to.Y, 1, 1);

        static int Mix(int a, int b, float k) => (int)Math.Round(a + (b - a) * k);

        return new Rectangle(
            Mix(from.X, to.X, t),
            Mix(from.Y, to.Y, t),
            Math.Max(1, Mix(from.Width, to.Width, t)),
            Math.Max(1, Mix(from.Height, to.Height, t)));
    }

    /// <summary>
    /// Kształt okna podany stronie: promień rogów (ten, który nakłada DWM),
    /// wyściółka od strony krawędzi ekranu oraz docelowa wysokość i szerokość.
    /// Oba rozmiary są stałe przez cały wjazd - dzięki temu zmiana rozmiaru okna
    /// tylko przycina treść, zamiast przebudowywać jej układ w każdej klatce.
    /// </summary>
    private static string ShapeMessage(Edge edge, int inset, int extent, int extentW)
    {
        var pad = edge switch
        {
            Edge.Top => $"{inset}px 0 0 0",
            Edge.Bottom => $"0 0 {inset}px 0",
            Edge.Left => $"0 0 0 {inset}px",
            Edge.Right => $"0 {inset}px 0 0",
            _ => "0",
        };

        return $$"""{"type":"shape","radius":"{{DwmRadius}}px","pad":"{{pad}}","extent":{{extent}},"extentW":{{extentW}}}""";
    }

    private string AnchorName() => WidgetPlacement.EdgeOf(_config.Anchor, _config.Detached) switch
    {
        Edge.Bottom => "bottom",
        Edge.Left or Edge.Right => "side",
        _ => "top",
    };

    /// <summary>Kotwica albo skala zmieniona w menu pastylki.</summary>
    public void ConfigChanged()
    {
        if (Visible) Reposition();
        Post(JsonSerializer.Serialize(new { type = "anchor", anchor = AnchorName() }));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitor.Updated -= OnSnapshot;
            _engine.StatusChanged -= OnEngineStatus;
            _engine.AnswerReady -= OnAnswerReady;
            _monitor.Companion = null;
            _morphPacer.Stop();
            _morphPacer.Dispose();
            _engine.Dispose();
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}

