using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClaudeStatus.Overlay.Animation;
using ClaudeStatus.Overlay.App;
using ClaudeStatus.Overlay.Interop;
using ClaudeStatus.Overlay.Placement;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClaudeStatus.Overlay.Assistant;

/// <summary>
/// Dymek „odpowiedź gotowa" - nakładka pod pastylką, pokazywana wtedy i tylko
/// wtedy, gdy tura skończyła się przy schowanej rozmowie. Odliczanie, pasek
/// postępu i pauza pod kursorem należą do strony; tutaj zostaje okno, jego
/// miejsce, kształt i odsłonięcie.
/// </summary>
/// <remarks>
/// Okno nigdy nie przejmuje aktywności - powiadomienie, które zabiera fokus
/// w trakcie pisania, szkodzi bardziej, niż pomaga. Stąd <c>WS_EX_NOACTIVATE</c>
/// i <see cref="ShowWithoutActivation"/>, a stąd z kolei sposób na <c>Esc</c>:
/// skrót rejestrujemy dopiero wtedy, gdy kursor stoi na dymku (czyli patrzysz
/// na niego i odliczanie i tak jest wstrzymane), i zdejmujemy od razu po zjeździe.
/// Globalne trzymanie <c>Esc</c> przez osiem sekund psułoby pracę w innych oknach.
/// </remarks>
internal sealed class AnswerToastForm : Form
{
    private const int HotkeyId = 0x4342;
    private const int WmHotkey = 0x0312;
    private const int VkEscape = 0x1B;

    /// <summary>
    /// Przyciemnienie nakładane na rozmycie, w formacie AABBGGRR - ta sama
    /// wartość, co pod pastylką i pod rozmową. Drugi stopień tłumienia niesie
    /// karta ze strony (<c>--tint</c> w toast.css).
    /// </summary>
    private const uint Tint = 0x50000000;

    /// <summary>Szerokość karty z projektu; wysokość składa strona i odsyła ją hostowi.</summary>
    private const int LogicalWidth = 560;
    private const int LogicalHeight = 200;

    /// <summary>Odstęp od pastylki - ten sam, co między pastylką a kartą w projekcie.</summary>
    private const int LogicalGap = 10;
    private const int LogicalMargin = 12;

    /// <summary>Odsłonięcie kształtu; w projekcie 280 ms, tyle samo co przenikanie treści.</summary>
    private const int RevealMs = 280;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private readonly OverlayConfig _config;
    private readonly OverlayLog _log;
    private readonly Func<Rectangle> _pillBounds;
    /// <summary>
    /// Przezroczysta treść - dopiero wtedy rozmycie spod okna ma jak być widoczne.
    /// Samego rozmycia strona nie zrobi: <c>backdrop-filter</c> widzi tylko to, co
    /// jest w niej samej, a nie to, co leży pod całą powierzchnią przeglądarki.
    /// </summary>
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };

    /// <summary>
    /// Odsłonięcie kształtu, w rytmie kompozytora. Zwykły <c>Timer</c> tykał tu co
    /// 15,6 ms albo co 31 - a postęp liczył się z <c>TickCount64</c>, który ma
    /// dokładnie to samo ziarno. Dwie kwantyzacje na raz z 280 ms animacji robiły
    /// kilkanaście skoków zamiast dojazdu.
    /// </summary>
    private readonly FramePacer _reveal;

    /// <summary>Czas animacji mierzony bez ziarna systemowego tyknięcia.</summary>
    private readonly Stopwatch _revealClock = new();

    /// <summary>
    /// Strona ma odesłać wysokość karty; bez niej nie ma jak postawić okna.
    /// Gdyby kiedyś nie odesłała, dymek nie pojawiłby się bez słowa - a tak
    /// zostaje ślad w logu zamiast ciszy.
    /// </summary>
    private readonly System.Windows.Forms.Timer _sizeWatchdog = new() { Interval = 800 };

    /// <summary>Prostokąt docelowy - ten, do którego dojeżdża rozwijanie.</summary>
    private Rectangle _target;
    private bool _webReady;
    private bool _allowVisible;
    private bool _hovered;
    private bool _closing;
    private bool _escRegistered;
    private string? _waiting;

    public AnswerToastForm(OverlayConfig config, OverlayLog log, Func<Rectangle> pillBounds)
    {
        _config = config;
        _log = log;
        _pillBounds = pillBounds;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = "Odpowiedź gotowa";

        // Formularz nie maluje tła: każdy zamalowany piksel zakryłby szkło,
        // które stoi w osobnym oknie pod spodem.
        SetStyle(ControlStyles.Opaque, true);

        Size = new Size(LogicalWidth, LogicalHeight);
        Controls.Add(_web);

        // Poza animacją wątek klatek śpi bez końca - budzi go dopiero Active.
        _reveal = new FramePacer(this, StepReveal, Timeout.Infinite);

        _sizeWatchdog.Tick += (_, _) =>
        {
            _sizeWatchdog.Stop();
            _log.Write("toast: strona nie odeslala wysokosci - dymek sie nie pokazal");
        };

        _ = Handle;
        _reveal.Start();
        _ = InitializeWebAsync();
    }

    /// <summary>Klik w dymek - pastylka ma otworzyć rozmowę.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action? OpenRequested { get; set; }

    /// <summary>Powiadomienie nie zabiera aktywności oknu, przy którym właśnie pracujesz.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            const int WsExNoActivate = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(_allowVisible && value);

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Rozmycie i rogi bierze DWM. Jedno i drugie dotyczy całego okna - dlatego
        // okno ma dokładnie rozmiar karty, a jego prostokąt animujemy przy wjeździe
        // i zjeździe. Promienia systemowego nie da się zmienić, więc karta w arkuszu
        // trzyma tę samą wartość, żeby jedno nie wystawało spod drugiego.
        //
        // Treść rysuje WebView2 na przezroczystym tle, więc ramki nie trzeba
        // rozciągać - okno samo w sobie nie ma czego zamalowywać.
        WindowMaterial.Apply(Handle, Tint, extendFrame: false);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            HideToast();
            return;
        }
        base.WndProc(ref m);
    }

    // ------------------------------------------------------------------ WebView2

    private async Task InitializeWebAsync()
    {
        if (!AssistantPaths.CanStart(out _)) return;

        try
        {
            Directory.CreateDirectory(AssistantPaths.WebViewData);
            var environment = await CoreWebView2Environment.CreateAsync(null, AssistantPaths.WebViewData);
            await _web.EnsureCoreWebView2Async(environment);

            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            core.SetVirtualHostNameToFolderMapping(
                "assistant.invalid", AssistantPaths.UiDirectory, CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += OnWebMessage;

            // Dopiero po załadowaniu strony jest do kogo mówić - wiadomość wysłana
            // wcześniej przepada po cichu, a z nią cały dymek.
            core.NavigationCompleted += (_, _) =>
            {
                _webReady = true;
                if (_waiting is { } queued)
                {
                    _waiting = null;
                    Show(queued);
                }
            };

            core.Navigate("https://assistant.invalid/toast.html");
        }
        catch (Exception ex)
        {
            _log.Error("toast", ex);
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }

        try { Dispatch(json); }
        catch (Exception ex) { _log.Error("toast", ex); }
    }

    private void Dispatch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

        switch (type)
        {
            // Strona jest gotowa; jeśli odpowiedź przyszła przed nią - podajemy ją teraz.
            case "ready":
                if (_waiting is { } pending)
                {
                    _waiting = null;
                    Show(pending);
                }
                return;

            // Wysokość zna dopiero strona, bo zależy od długości odpowiedzi.
            case "size":
                _sizeWatchdog.Stop();
                if (root.TryGetProperty("height", out var h) && h.TryGetInt32(out var height)) Place(height);
                return;

            case "open":
                HideToast();
                OpenRequested?.Invoke();
                return;

            case "expired":
                Dismiss();
                return;

            case "hover":
                _hovered = root.TryGetProperty("over", out var over) && over.ValueKind == JsonValueKind.True;
                SyncEscape();
                return;
        }
    }

    private void Post(string json)
    {
        if (!_webReady || _web.CoreWebView2 is null) return;
        try { _web.CoreWebView2.PostWebMessageAsString(json); }
        catch (InvalidOperationException) { /* strona jeszcze się ładuje */ }
    }

    // ------------------------------------------------------------------ pokazanie

    /// <summary>Odpowiedź gotowa - pokaż dymek (albo odśwież ten, który jeszcze wisi).</summary>
    public void Show(string answer)
    {
        if (!_config.AssistantToast || string.IsNullOrWhiteSpace(answer)) return;

        if (!_webReady || _web.CoreWebView2 is null)
        {
            _waiting = answer;   // strona wstaje przy pierwszym uruchomieniu; pokażemy po `ready`
            return;
        }

        // Szerokość musi być znana przed pomiarem - od niej zależy, ile wierszy
        // zajmie odpowiedź, a więc i wysokość, którą strona zaraz odeśle.
        Width = (int)Math.Round(LogicalWidth * (DeviceDpi / 96.0));

        Post(JsonSerializer.Serialize(new
        {
            type = "answer",
            text = answer,
            seconds = Math.Clamp(_config.AssistantToastSeconds, 3, 20),
        }));

        _sizeWatchdog.Stop();
        _sizeWatchdog.Start();
    }

    /// <summary>Strona podała wysokość karty - dopiero teraz wiadomo, gdzie i jak duże jest okno.</summary>
    private void Place(int cardHeight)
    {
        var scale = DeviceDpi / 96.0;
        var gap = (int)Math.Round(LogicalGap * scale);
        var margin = (int)Math.Round(LogicalMargin * scale);
        var width = (int)Math.Round(LogicalWidth * scale);
        var height = Math.Max(1, (int)Math.Round(cardHeight * scale));

        var pill = _pillBounds();
        var area = Screen.FromRectangle(pill).WorkingArea;
        var edge = WidgetPlacement.EdgeOf(_config.Anchor, _config.Detached);

        // Dymek wyrasta z tej strony pastylki, po której jest miejsce - a to zależy
        // od krawędzi, do której pastylka przylega.
        var (x, y) = edge switch
        {
            Edge.Bottom => (pill.Left + (pill.Width - width) / 2, pill.Top - gap - height),
            Edge.Left => (pill.Right + gap, pill.Top + (pill.Height - height) / 2),
            Edge.Right => (pill.Left - gap - width, pill.Top + (pill.Height - height) / 2),
            _ => (pill.Left + (pill.Width - width) / 2, pill.Bottom + gap),
        };

        _target = new Rectangle(
            Math.Clamp(x, area.Left + margin, Math.Max(area.Left + margin, area.Right - margin - width)),
            Math.Clamp(y, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - margin - height)),
            width, height);

        _allowVisible = true;
        _closing = false;
        _revealClock.Restart();
        StepReveal();

        if (!Visible) Show();
        TopMost = true;
        _reveal.Active = true;
    }

    /// <summary>
    /// Dymek rozwija się i zwija wysokością samego okna. Region okna do tego nie
    /// służy: obcina treść, ale rozmycie i cień zostają narysowane dla pełnego
    /// prostokąta - przy zwijaniu karta znikała, a szkło stało dalej, aż okno się
    /// schowało. Prostokąt okna bierze jedno i drugie ze sobą.
    /// Strona tego nie odczuwa: szerokość się nie zmienia, więc tekst nie łamie
    /// się na nowo, a karta ma wysokość z treści i jest po prostu przycinana.
    /// </summary>
    private void StepReveal()
    {
        // Wątek klatek może przynieść jeszcze jedną prośbę po tym, jak animacja
        // się skończyła - schowanego dymka nie ma już czego dotyczyć.
        if (!_allowVisible) return;

        var progress = (float)Math.Clamp(_revealClock.Elapsed.TotalMilliseconds / RevealMs, 0, 1);
        var eased = Easing.OutQuint(progress);

        if (progress >= 1)
        {
            _reveal.Active = false;
            if (_closing)
            {
                HideToast();
            }
            else
            {
                Bounds = _target;
            }
            return;
        }

        var shown = _closing ? 1 - eased : eased;
        var height = Math.Max(1, (int)Math.Round(_target.Height * shown));

        // Przy dolnej kotwicy dymek trzyma się swojego dołu, żeby rósł w stronę
        // przeciwną do pastylki, a nie odjeżdżał od niej.
        var top = WidgetPlacement.EdgeOf(_config.Anchor, _config.Detached) == Edge.Bottom
            ? _target.Bottom - height
            : _target.Top;

        Bounds = new Rectangle(_target.X, top, _target.Width, height);
    }

    /// <summary>
    /// Koniec odliczania: okno zjeżdża tam, skąd przyjechało, i zabiera rozmycie
    /// ze sobą - bo to jego prostokąt jest rozmywany.
    /// </summary>
    private void Dismiss()
    {
        if (!Visible || _closing) return;

        _closing = true;
        _revealClock.Restart();
        _reveal.Active = true;
        StepReveal();
    }

    /// <summary>Znika od razu - klik, <c>Esc</c> albo otwarcie rozmowy, która go zastępuje.</summary>
    public void HideToast()
    {
        _reveal.Active = false;
        _sizeWatchdog.Stop();
        _allowVisible = false;
        _closing = false;
        _hovered = false;
        SyncEscape();
        Hide();
    }

    /// <summary>Esc działa tylko wtedy, gdy kursor stoi na dymku - patrz uwaga przy klasie.</summary>
    private void SyncEscape()
    {
        var wanted = _hovered && Visible;
        if (wanted == _escRegistered || !IsHandleCreated) return;

        _escRegistered = wanted
            ? RegisterHotKey(Handle, HotkeyId, 0, VkEscape)
            : !UnregisterHotKey(Handle, HotkeyId) && _escRegistered;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason is CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToast();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_escRegistered && IsHandleCreated) UnregisterHotKey(Handle, HotkeyId);
            _reveal.Dispose();
            _sizeWatchdog.Dispose();
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}

