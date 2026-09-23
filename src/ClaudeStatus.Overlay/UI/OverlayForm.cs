using System.Diagnostics;
using System.Runtime.InteropServices;
using ClaudeStatus.Core.Sessions;
using ClaudeStatus.Core.Update;
using ClaudeStatus.Core.Usage;
using ClaudeStatus.Overlay.Animation;
using ClaudeStatus.Overlay.App;
using ClaudeStatus.Overlay.Interop;
using ClaudeStatus.Overlay.Model;
using ClaudeStatus.Overlay.Placement;
using ClaudeStatus.Overlay.Rendering;
using static ClaudeStatus.Overlay.Interop.NativeMethods;

namespace ClaudeStatus.Overlay.UI;

/// <summary>
/// Okno nakładki: spina monitor sesji, animacje, renderer i mysz. Sama nic nie
/// rysuje - co klatkę buduje <see cref="FrameInput"/>, oddaje go rendererowi
/// i wypycha wynik do okna warstwowego.
///
/// Widget ma trzy poziomy (<see cref="WidgetStage"/>): brzeg (domyślny),
/// pasek (podgląd po najechaniu albo na stałe, zależnie od menu) i panel (po kliknięciu -
/// zostaje otwarty, dopóki nie kliknie się ponownie). Każdy poziom przenika
/// niezależnie (własny fade), a kształt powłoki liczy i animuje sam
/// <see cref="WidgetRenderer"/> na podstawie tego, do którego poziomu aktualnie
/// zmierzamy.
///
/// Pętla klatek jest oszczędna: przy ciągłej animacji (morfing, obrót łuku,
/// miganie, dojazd pasków) idzie w rytmie kompozytora (<see cref="FramePacer"/>),
/// czyli dokładnie tyle klatek, ile ekran zdąży pokazać; poza tym tylko sonduje
/// kursor, a rysuje dopiero, gdy coś się faktycznie zmieniło (sekunda zegara -
/// tylko gdy widoczny jest jakikolwiek tekst z wiekiem, nowe dane, wiersz pod
/// kursorem).
/// </summary>
public sealed class OverlayForm : LayeredWindow, IOverlayCommands
{
    private const int IdleFrameMs = 50;
    private const int DragThresholdPx = 3;

    private readonly OverlayConfig _config;
    private readonly OverlayConfigStore _configStore;
    private readonly SessionStatusStore _store;
    private readonly SessionMonitor _monitor;
    private readonly UpdateChecker _updates;
    private readonly UpdateInstaller _installer = new();
    private readonly OverlayLog _log;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly FadeAnimation _restFade = new(Design.RestFadeMs, startVisible: true);
    private readonly FadeAnimation _slimFade = new(Design.SlimFadeMs);
    private readonly MeterAnimation _meters = new();
    private readonly StateChangeTracker _transitions = new();
    private readonly StateSoundPlayer _sounds = new();
    private readonly FramePacer _pacer;
    private readonly OverlayMenu _menu;
    private readonly WidgetRenderer _renderer;

    /// <summary>Szkło pod widgetem; <c>null</c>, gdy system go nie dał - wtedy powłoka zostaje nieprzezroczysta.</summary>
    private readonly GlassWindow? _glass;

    private WidgetStage _stage = WidgetStage.Rest;

    private double _stageChangedAtMs = double.NegativeInfinity;

    // Rozmowa: od kiedy wchodzi (jej wejście liczy się tym samym zegarem, co pastylka).
    private bool _companionVisible;
    private double _companionShownAtMs = double.NegativeInfinity;

    private SessionSnapshot _snapshot = SessionSnapshot.Empty;
    private Size _contentSize;

    private (double AtMs, DateTime? FetchedAt)? _usageRefresh;
    private UpdatePhase _updatePhase;
    private double _updateProgress = -1;
    private UpdateBanner? _updateBanner;

    /// <summary>Na czym stoi aktualizacja - stąd bierze się napis w panelu.</summary>
    private enum UpdatePhase
    {
        /// <summary>Nic się nie dzieje: albo nie ma nowej wersji, albo czeka na klik.</summary>
        Idle,
        Downloading,
        Launching,
        Failed,
    }
    private double _lastFrameMs;
    private long _lastSecond = -1;
    private bool _trackingLeave;
    private bool _dirty = true;
    private DragState? _drag;

    private sealed class DragState
    {
        public Point CursorOrigin;
        public Point WindowOrigin;
        public bool Moved;
    }

    public OverlayForm(OverlayConfig config, OverlayConfigStore configStore, SessionStatusStore store,
        SessionMonitor monitor, UpdateChecker updates, OverlayLog log)
    {
        _config = config;
        _configStore = configStore;
        _store = store;
        _monitor = monitor;
        _updates = updates;
        _log = log;

        Text = "Claude Status";
        TopMost = config.TopMost;

        _renderer = new WidgetRenderer(DpiScale.FromDpi(DeviceDpi));
        var margin = _renderer.Margin;
        var scale = _renderer.Scale;
        var restW = scale.PxInt(config.Anchor.IsMiddle() ? Design.RestH : Design.RestW);
        var restH = scale.PxInt(config.Anchor.IsMiddle() ? Design.RestW : Design.RestH);
        Location = new Point(config.X - margin, config.Y - margin);
        Size = new Size(restW + 2 * margin, restH + 2 * margin);

        _menu = new OverlayMenu(config, this);
        ContextMenuStrip = _menu;

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var glass = new GlassWindow();
            _glass = glass.Available ? glass : null;
            if (_glass is null) glass.Dispose();
        }

        _pacer = new FramePacer(this, SafeFrame, IdleFrameMs);
    }

    // ------------------------------------------------------------ cykl życia

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTopMost(_config.TopMost);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyTopMost(_config.TopMost);
        PullSnapshot(playSounds: false);
        _meters.Snap(VisibleUsage());
        _lastFrameMs = _clock.Elapsed.TotalMilliseconds;
        SafeFrame();
        _pacer.Start();
    }

    /// <summary>
    /// Klik gdziekolwiek indziej na ekranie odbiera oknu aktywację (Windows
    /// aktywuje to, co zostało kliknięte) - tego używamy do zamykania panelu,
    /// tak jak każdy inny popover: klik na zewnątrz go zamyka.
    /// </summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!_companionVisible || _menu.Visible) return;

        // Rozmowa i pastylka są jedną całością, więc klik z jednej w drugą nie może
        // jej zamykać. Decyzja zapada dopiero, gdy wiadomo, co przejęło pierwszy
        // plan - w chwili tego zdarzenia system jeszcze tego nie ustawił.
        if (UnitHasFocus is { } hasFocus)
        {
            BeginInvoke(() =>
            {
                if (hasFocus()) return;
                ClosePanel();
            });
            return;
        }

        ClosePanel();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _pacer.Stop();
        if (_config.Anchor == WidgetAnchor.Free) RememberFreePosition();
        _configStore.Save(_config);
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pacer.Dispose();
            _menu.Dispose();
            _renderer.Dispose();
            _installer.Dispose();
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ klatka

    private void SafeFrame()
    {
        try
        {
            Frame();
        }
        catch (Exception ex)
        {
            _log.Error("frame", ex);
        }
    }

    private void Frame()
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        var dt = now - _lastFrameMs;
        _lastFrameMs = now;
        var wall = DateTime.Now;

        SyncScale();
        PullSnapshot(playSounds: _config.Sound);
        UpdateStage(now);

        var usage = VisibleUsage();
        _meters.Update(dt, usage);

        var summary = SessionSummary.From(_snapshot, wall);
        var vertical = _config.Anchor.IsMiddle();

        var refreshing = UsageRefreshPending(now, usage);

        _updates.Poll(wall);
        var banner = BuildUpdateBanner();
        if (banner != _updateBanner)
        {
            _updateBanner = banner;
            _dirty = true;

            // Pasek o nowej wersji stał kiedyś na dole panelu. Panelu nie ma,
            // więc niesie go rozmowa - razem z postępem pobierania.
            UpdateBannerChanged?.Invoke(banner);
        }

        // stany migające zawsze wygrywają priorytetem sortowania sesji (zobacz
        // SessionInfo.DisplayOrder), więc jeśli KTOKOLWIEK potrzebuje migania,
        // to i tak jest na szczycie - wystarczy sprawdzić styl podsumowania
        var shapeAnimating = now - _stageChangedAtMs < Design.MorphMs;
        // Ruch, który widać: morfing kształtu, przenikanie poziomów, mierniki.
        // Rozmowa nie jest tu liczona - od kiedy nie stoi obok panelu, prowadzi
        // swój wjazd i zjazd sama, na własnym zegarze.
        var moving = shapeAnimating
            || _restFade.IsAnimating || _slimFade.IsAnimating
            || _meters.IsAnimating || refreshing;

        // Oddech znacznika stanu - "pracuje" pulsuje, "czeka"/"błąd" miga. Trwa bez
        // końca, dopóki sesja jest w tym stanie, i to on, a nie przejścia, decyduje
        // o rachunku za procesor. Dostaje więc własny, rzadszy rytm: na kilku
        // pikselach nikt nie odróżni 30 klatek od 83.
        //
        // Miganie było kiedyś falą prostokątną i dało się je prowadzić zdarzeniem -
        // dwa przeskoki na sekundę zamiast rytmu. Wyglądało jednak jak mrugająca
        // plama, więc jest teraz płynne i musi mieć klatki jak pulsowanie.
        var breathing = summary.Style.Glyph is Glyph.Spin or Glyph.Blink;

        var continuous = moving || breathing;

        // wiek sesji pokazuje tylko pasek - w samym brzegu nie ma po co odświeżać co sekundę
        var textVisible = _slimFade.Value > 0.001f;
        var second = wall.Ticks / TimeSpan.TicksPerSecond;
        var secondChanged = textVisible && second != _lastSecond;
        if (secondChanged) _lastSecond = second;

        if (continuous || secondChanged || _dirty)
        {
            _dirty = false;

            Render(new FrameInput(
                TimeMs: now,
                Now: wall,
                Snapshot: _snapshot,
                Summary: summary,
                Usage: usage,
                Meters: _meters.Values,
                UsageRefreshing: refreshing,
                TargetStage: _stage,
                Edge: WidgetPlacement.EdgeOf(_config.Anchor, _config.Detached),
                Vertical: vertical,
                RestAlpha: _restFade.Value,
                SlimAlpha: _slimFade.Value)
            {
                Glass = _glass is { Available: true },
            });
        }

        _pacer.MinIntervalMs = moving ? FramePacer.FullRateMs : FramePacer.CalmRateMs;
        _pacer.Active = continuous;
    }

    /// <summary>
    /// Najechanie kursorem pokazuje pasek (o ile włączone w menu). Przeciąganie
    /// i otwarte menu zamrażają poziom, żeby pastylka nie morfowała pod ręką.
    /// </summary>
    private void UpdateStage(double now)
    {
        if (_drag is null && !_menu.Visible)
        {
            SetStage(IdleStage(), now);
        }

        _restFade.SetVisible(_stage == WidgetStage.Rest, now);
        _slimFade.SetVisible(_stage == WidgetStage.Slim, now);
        _restFade.Update(now);
        _slimFade.Update(now);
    }

    /// <summary>
    /// Poziom poza panelem: pasek, gdy pastylka ma stać rozwinięta na stałe albo
    /// kursor na niej stoi (przy rozwijaniu po najechaniu) - w przeciwnym razie brzeg.
    /// </summary>
    private WidgetStage IdleStage()
        => _config.AlwaysExpanded || (_config.Hover && CursorInsideContent())
            ? WidgetStage.Slim
            : WidgetStage.Rest;

    private void SetStage(WidgetStage stage, double now)
    {
        if (_stage == stage) return;
        _stage = stage;
        _stageChangedAtMs = now;
        _dirty = true;
    }

    private void Render(FrameInput frame)
    {
        var rendered = _renderer.Render(frame);
        var margin = rendered.Margin;
        var glass = _glass;
        _contentSize = rendered.ContentSize;

        Point? windowLocation = null;
        if (_drag is null)
        {
            var origin = WidgetPlacement.ContentOrigin(_config.Anchor, _config.Detached, CurrentWorkArea(margin),
                _contentSize, _renderer.Scale, new Point(_config.X, _config.Y));
            windowLocation = new Point(origin.X - margin, origin.Y - margin);
        }

        ContentBounds = new Rectangle(margin, margin, _contentSize.Width, _contentSize.Height);
        Push(rendered.Surface, windowLocation);

        // Szkło staje dokładnie pod kształtem widgetu - rozmiar i promień bierze
        // z tej samej klatki, więc jedzie razem z morfingiem.
        if (glass is { Available: true })
        {
            var origin = windowLocation ?? Location;
            glass.Follow(
                new Rectangle(origin.X + margin, origin.Y + margin, _contentSize.Width, _contentSize.Height),
                frame.Edge, Handle);
        }
    }

    private void SyncScale()
    {
        var scale = DpiScale.FromDpi(DeviceDpi);
        if (scale == _renderer.Scale) return;
        _renderer.SetScale(scale);
        _dirty = true;
    }

    /// <summary>Najnowszy obraz z monitora; ten sam obiekt = nic się nie zmieniło.</summary>
    private void PullSnapshot(bool playSounds)
    {
        var latest = _monitor.Current;
        if (ReferenceEquals(latest, _snapshot)) return;

        _snapshot = latest;
        _dirty = true;

        var transitions = _transitions.Apply(latest);
        if (playSounds) _sounds.Play(transitions);
    }

    private UsageSnapshot? VisibleUsage() => _config.Usage && _snapshot.Usage.Ok ? _snapshot.Usage : null;

    /// <summary>
    /// Ręczne odświeżenie limitów z przycisku w panelu - prosi źródło o pobranie
    /// poza kolejnością i zapamiętuje, na co czekamy (żeby ikona miała kiedy przestać
    /// się kręcić, nawet gdy API nie odpowie).
    /// </summary>
    private void RefreshUsageNow(double now)
    {
        _usageRefresh = (now, VisibleUsage()?.FetchedAt);
        _monitor.RefreshUsage();
        _dirty = true;
    }

    // ---------------------------------------------------------------- aktualizacje

    /// <summary>Napis o aktualizacji dla panelu; null, gdy nie ma o czym mówić.</summary>
    private UpdateBanner? BuildUpdateBanner() => _updatePhase switch
    {
        UpdatePhase.Downloading => new UpdateBanner(
            _updateProgress >= 0 ? $"pobieranie aktualizacji {(int)Math.Round(_updateProgress * 100)}%" : "pobieranie aktualizacji...",
            Actionable: false,
            Progress: _updateProgress),
        UpdatePhase.Launching => new UpdateBanner("uruchamiam instalator...", Actionable: false),
        UpdatePhase.Failed => new UpdateBanner("aktualizacja się nie udała - kliknij, aby spróbować ponownie", Actionable: true),
        _ => _updates.Available is { } info
            ? new UpdateBanner($"nowa wersja {info.Name} - {(info.CanInstall ? "zaktualizuj" : "otwórz stronę wydania")}", Actionable: true)
            : null,
    };

    /// <summary>
    /// Klik w pasek aktualizacji: pobierz instalator i oddaj mu stery. Wydanie
    /// bez instalatora (sama paczka portable) tylko otwiera stronę wydania.
    /// </summary>
    private void StartUpdate()
    {
        if (_updatePhase is UpdatePhase.Downloading or UpdatePhase.Launching) return;
        if (_updates.Available is not { } info) return;

        if (!info.CanInstall)
        {
            UpdateInstaller.OpenPage(info.PageUrl);
            return;
        }

        _updatePhase = UpdatePhase.Downloading;
        _updateProgress = -1;
        _dirty = true;

        // Progress<T> wraca na wątek UI, więc postęp można wprost przepisać do pola klatki
        _ = DownloadAndInstallAsync(info, new Progress<double>(p =>
        {
            _updateProgress = p;
            _dirty = true;
        }));
    }

    private async Task DownloadAndInstallAsync(UpdateInfo info, IProgress<double> progress)
    {
        try
        {
            var setup = await _installer.DownloadAsync(info, progress, CancellationToken.None);

            _updatePhase = UpdatePhase.Launching;
            _dirty = true;

            // instalator zaraz podmieni nasze pliki - najpierw zapisz ustawienia, potem zejdź mu z drogi
            if (_config.Anchor == WidgetAnchor.Free) RememberFreePosition();
            _configStore.Save(_config);

            if (UpdateInstaller.Launch(setup))
            {
                Close();
                return;
            }

            _updatePhase = UpdatePhase.Failed;
            _dirty = true;
        }
        catch (Exception ex)
        {
            _log.Error("update", ex);
            _updatePhase = UpdatePhase.Failed;
            _dirty = true;
        }
    }

    /// <summary>Czy wciąż czekamy na zamówione limity: przyszły nowe dane albo minął czas.</summary>
    private bool UsageRefreshPending(double now, UsageSnapshot? usage)
    {
        if (_usageRefresh is not { } pending) return false;
        if (now - pending.AtMs < Design.RefreshSpinMs && usage?.FetchedAt == pending.FetchedAt) return true;

        _usageRefresh = null;
        _dirty = true;
        return false;
    }

    private Rectangle CurrentWorkArea(int margin)
    {
        var origin = ScreenOrigin;
        return Screen.FromPoint(new Point(origin.X + margin, origin.Y + margin)).WorkingArea;
    }

    // ------------------------------------------------------------------ kursor

    /// <summary>Pozycja kursora w układzie widgetu (0,0 = lewy górny róg powłoki).</summary>
    private PointF CursorInContent()
    {
        var p = Cursor.Position;
        var origin = ScreenOrigin;
        return new PointF(p.X - origin.X - ContentBounds.X, p.Y - origin.Y - ContentBounds.Y);
    }

    private bool CursorInsideContent()
    {
        var c = CursorInContent();
        return c.X >= 0 && c.Y >= 0 && c.X < _contentSize.Width && c.Y < _contentSize.Height;
    }

    // -------------------------------------------------------------------- mysz

    protected override void WndProc(ref Message m)
    {
        // Kursor zjechał z pastylki. Bez tego o zjeździe nie dowiedzielibyśmy się
        // wcale - WM_MOUSEMOVE po prostu przestaje przychodzić - i zwinięcie
        // czekałoby na najbliższe tyknięcie pętli spoczynkowej.
        if (m.Msg == WM_MOUSELEAVE)
        {
            _trackingLeave = false;
            _pacer.Nudge();
        }

        base.WndProc(ref m);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _drag = new DragState { CursorOrigin = Cursor.Position, WindowOrigin = ScreenOrigin };
    }

    /// <summary>
    /// Najechanie i zjechanie kursorem obsługujemy zdarzeniem, a nie samym
    /// odpytywaniem w klatce: pętla spoczynkowa tyka co 50 ms, więc pastylka
    /// ociągała się z reakcją o nawet pół dziesiątej sekundy. Odpytywanie
    /// (<see cref="IdleStage"/>) zostaje jako siatka bezpieczeństwa i to ono dalej
    /// rozstrzyga - gdyby <c>WM_MOUSELEAVE</c> przepadło, pastylka zwinie się
    /// najpóźniej przy następnym tyknięciu, zamiast zostać rozwinięta na zawsze.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_trackingLeave)
        {
            var track = new TRACKMOUSEEVENT
            {
                cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = TME_LEAVE,
                hwndTrack = Handle,
            };
            _trackingLeave = TrackMouseEvent(ref track);
        }

        _pacer.Nudge();

        if (_drag is null) return;

        var p = Cursor.Position;
        var dx = p.X - _drag.CursorOrigin.X;
        var dy = p.Y - _drag.CursorOrigin.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > DragThresholdPx) _drag.Moved = true;

        // przy blokadzie gest rozpoznajemy dalej (żeby nie wyszedł z niego klik), ale okno stoi
        if (_drag.Moved && !_config.Locked) MoveTo(new Point(_drag.WindowOrigin.X + dx, _drag.WindowOrigin.Y + dy));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _drag is null) return;

        var drag = _drag;
        _drag = null;

        if (drag.Moved)
        {
            // przy blokadzie przeciągnięcie nie robi zupełnie nic - ani nie przesuwa,
            // ani nie liczy się jako klik otwierający panel
            if (_config.Locked) return;

            // przeciągnięcie = pozycja dowolna; kotwica przestaje obowiązywać
            _config.Anchor = WidgetAnchor.Free;
            RememberFreePosition();
            _menu.SyncAnchor();
            _configStore.Save(_config);
            _dirty = true;
            return;
        }

        HandleClick();
    }

    /// <summary>
    /// Pokaż albo schowaj okno rozmowy. Panel zachowuje się jak dotąd - zjeżdża
    /// z pastylki - a rozmowa dostawia się obok niego. Puste = zachowanie sprzed
    /// asystenta, sam panel.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<bool>? AssistantVisibility { get; set; }

    /// <summary>
    /// Czy pierwszoplanowe okno należy jeszcze do całości panel + rozmowa.
    /// Bez tego klik w rozmowę zwijałby panel, przy którym ona stoi.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<bool>? UnitHasFocus { get; set; }

    /// <summary>
    /// Gdzie stanęłaby treść tego rozmiaru przy bieżącej kotwicy - tym samym
    /// rachunkiem, którym ustawia się sama pastylka. Z tego korzysta okno rozmowy:
    /// wyrasta dokładnie tam, gdzie kiedyś rozwijał się panel.
    /// </summary>
    public Rectangle PlaceContent(Size size)
    {
        var origin = WidgetPlacement.ContentOrigin(_config.Anchor, _config.Detached,
            CurrentWorkArea(_renderer.Margin), size, _renderer.Scale, new Point(_config.X, _config.Y));
        return new Rectangle(origin, size);
    }

    /// <summary>
    /// Prostokąt treści pastylki na ekranie, taki, jaki jest w tej chwili - z tego,
    /// co widać, wyrasta dymek z odpowiedzią, niezależnie od poziomu pastylki.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Rectangle ContentScreenBounds
    {
        get
        {
            var origin = ScreenOrigin;
            return new Rectangle(
                origin.X + ContentBounds.X, origin.Y + ContentBounds.Y,
                ContentBounds.Width, ContentBounds.Height);
        }
    }

    /// <summary>Zwija rozmowę - wołane też przez nią samą, gdy fokus wyszedł poza całość.</summary>
    public void ClosePanel()
    {
        if (!_companionVisible) return;
        ShowCompanion(false, _clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Pokazuje albo chowa rozmowę - i tylko tędy, żeby stan pastylki i widoczność
    /// okna nigdy się nie rozjechały. Samą animację wjazdu i zjazdu prowadzi
    /// rozmowa, na własnym zegarze.
    /// </summary>
    private void ShowCompanion(bool visible, double now)
    {
        if (visible && !_companionVisible)
        {
            _companionShownAtMs = now;
            _dirty = true;
        }

        _companionVisible = visible;
        AssistantVisibility?.Invoke(visible);
    }

    /// <summary>
    /// Klik w wierszu panelu aktywuje edytor, klik w kołową strzałkę odświeża limity,
    /// klik gdziekolwiek indziej otwiera asystenta (albo panel, gdy asystenta nie ma).
    /// </summary>
    /// <summary>
    /// Klik otwiera rozmowę, a drugi ją zamyka. Panelu z listą sesji już nie ma:
    /// limity przeniosły się do nagłówka rozmowy, a stan najważniejszej sesji
    /// pastylka pokazuje sama - pierścieniem i kolorem.
    /// </summary>
    private void HandleClick()
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        ShowCompanion(!_companionVisible, now);
    }

    /// <summary>
    /// Zapamiętuje, czy rozmowa ma stać obok panelu. To preferencja, a nie stan
    /// jednej sesji, więc ląduje w konfiguracji od razu po kliknięciu.
    /// </summary>
    public void SetAssistantExpanded(bool expanded)
    {
        if (_config.AssistantExpanded == expanded) return;
        _config.AssistantExpanded = expanded;
        _configStore.Save(_config);
        _dirty = true;
    }

    /// <summary>Model asystenta wybrany w nagłówku rozmowy - też preferencja, więc od razu na dysk.</summary>
    public void SetAssistantModel(string? model)
    {
        if (_config.AssistantModel == model) return;
        _config.AssistantModel = model;
        _configStore.Save(_config);
    }

    /// <summary>Otwiera rozmowę - i skrót klawiszowy, i klik w pastylkę prowadzą tutaj.</summary>
    public void OpenPanelWithAssistant()
    {
        SetAssistantExpanded(true);
        ShowCompanion(true, _clock.Elapsed.TotalMilliseconds);
    }

    private void RememberFreePosition()
    {
        var origin = ScreenOrigin;
        _config.X = origin.X + ContentBounds.X;
        _config.Y = origin.Y + ContentBounds.Y;
    }

    // ------------------------------------------------------------------- menu

    public void ConfigChanged()
    {
        TopMost = _config.TopMost;
        ApplyTopMost(_config.TopMost);
        _monitor.UsageEnabled = _config.Usage;
        _monitor.UsageInterval = _config.UsageInterval;
        _updates.Enabled = _config.Updates;
        // menu jest jeszcze na wierzchu, więc UpdateStage stoi - poziom trzeba przestawić tutaj,
        // żeby przełącznik zadziałał od razu, a nie dopiero po zamknięciu menu
        SetStage(IdleStage(), _clock.Elapsed.TotalMilliseconds);
        _meters.Snap(VisibleUsage());
        _configStore.Save(_config);
        _dirty = true;
    }

    public void AnchorChanged(WidgetAnchor anchor)
    {
        _config.Anchor = anchor;
        if (anchor == WidgetAnchor.Free) RememberFreePosition();
        _configStore.Save(_config);
        _dirty = true;
    }

    /// <summary>Nazwa czekającej wersji dla menu; null, gdy nic nie czeka.</summary>
    public string? PendingUpdateName => _updates.Available?.Name;

    public bool UpdateCheckBusy => _updates.Busy;

    /// <summary>Menu: sprawdź od ręki, nie czekając na dobowy termin.</summary>
    public void CheckForUpdates()
    {
        _updates.CheckNow();
        _dirty = true;
    }

    /// <summary>
    /// Powiadomienie o nowej wersji dla rozmowy - z tekstem i postępem pobierania.
    /// <c>null</c> znaczy: nie ma czego pokazywać.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<UpdateBanner?>? UpdateBannerChanged { get; set; }

    /// <summary>Bieżące powiadomienie - rozmowa pyta o nie, gdy się otwiera.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public UpdateBanner? CurrentUpdateBanner => _updateBanner;

    /// <summary>Menu i wiersz w stopce rozmowy prowadzą tutaj.</summary>
    public void InstallUpdate()
    {
        if (_updatePhase == UpdatePhase.Failed) _updatePhase = UpdatePhase.Idle;
        StartUpdate();
    }

    public void ClearFinishedSessions()
    {
        foreach (var session in _snapshot.Sessions)
        {
            // Sesja bez pliku (asystent) żyje w pamięci nakładki - nie ma czego kasować.
            if (session.State.IsFinished() && session.FilePath.Length > 0) _store.TryDelete(session.FilePath);
        }
        _monitor.Refresh();
    }

    public void Exit() => Close();
}

