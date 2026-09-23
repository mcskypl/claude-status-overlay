using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeStatus.Overlay.Animation;

/// <summary>
/// Zegar klatek zestrojony z kompozytorem.
/// </summary>
/// <remarks>
/// Dlaczego nie <see cref="System.Windows.Forms.Timer"/>: WM_TIMER ma ziarno
/// systemowego tyknięcia (15,625 ms) i jest wiadomością najniższego priorytetu -
/// system generuje ją dopiero, gdy kolejka jest pusta, i nigdy nie nadrabia
/// zaległości. Klatka trwająca choć chwilę przesuwa tyknięcie o CAŁE ziarno, więc
/// odstępy wychodziły na przemian 15 i 31 ms (zmierzone). Animacja liczona z
/// zegara ściennego przy takich odstępach szarpie, choć każda klatka z osobna
/// jest policzona poprawnie.
///
/// Zamiast tego osobny wątek czeka na kompozytor (<c>DwmFlush</c> wraca tuż po
/// tym, jak DWM pokazał klatkę) i dopiero wtedy prosi wątek UI o kolejną -
/// zwykłą wiadomością <c>BeginInvoke</c>, która nie ma priorytetu WM_TIMER.
/// Rysujemy więc dokładnie w rytmie ekranu: ani jednej klatki, której nikt nie
/// zobaczy, ani jednej spóźnionej.
///
/// Odświeżanie ekranu bywa wysokie (144 Hz), a nakładka nie potrzebuje aż tylu
/// klatek - <see cref="MinIntervalMs"/> przepuszcza co drugie odbicie zamiast
/// rysować w każdym, a przy samym pulsowaniu znacznika schodzi jeszcze niżej.
/// </remarks>
public sealed class FramePacer : IDisposable
{
    /// <summary>Najkrótszy odstęp między klatkami - przy 144 Hz rysujemy co drugie odbicie, przy 60 Hz i niżej każde.</summary>
    public const int FullRateMs = 12;

    /// <summary>
    /// Rytm dla animacji, których oko i tak szybciej nie rozróżni: pulsowania
    /// i migania kilkupikselowego znacznika. Znacznik na brzegu pulsuje przez cały
    /// dzień, więc to jedyna animacja, która naprawdę kosztuje - a różnicy między
    /// 30 a 83 klatkami na sekundę na pięciu pikselach nie widać.
    /// </summary>
    public const int CalmRateMs = 33;

    /// <summary>Odstęp, gdy kompozytor nie czeka (brak DWM, zdalny pulpit bez kompozycji).</summary>
    private const int FallbackMs = 16;

    /// <summary>Po tylu natychmiastowych powrotach uznajemy, że <c>DwmFlush</c> nie nadaje rytmu.</summary>
    private const int GiveUpAfter = 30;

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private readonly Control _target;
    private readonly Action _frame;
    private readonly Action _run;
    private readonly int _idleMs;
    private readonly ManualResetEventSlim _wake = new(false);

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _active;
    private volatile int _minFrameMs = FullRateMs;
    private int _pending;
    private int _instant;
    private bool _composited = true;
    private long _lastFrame;

    /// <param name="target">Okno, na którego wątku wykonuje się klatka.</param>
    /// <param name="frame">Klatka - wołana zawsze na wątku UI.</param>
    /// <param name="idleMs">Odstęp poza animacją; wtedy klatka służy tylko sondowaniu kursora i danych.</param>
    public FramePacer(Control target, Action frame, int idleMs)
    {
        _target = target;
        _frame = frame;
        _idleMs = idleMs;
        _run = Run;
    }

    /// <summary>Czy trwa animacja - wtedy klatki idą w rytmie ekranu, inaczej co <c>idleMs</c>.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value) _wake.Set();
        }
    }

    /// <summary>
    /// Najkrótszy odstęp między klatkami w trakcie animacji:
    /// <see cref="FullRateMs"/> albo <see cref="CalmRateMs"/>.
    /// </summary>
    public int MinIntervalMs
    {
        get => _minFrameMs;
        set => _minFrameMs = Math.Max(1, value);
    }

    /// <summary>
    /// Prosi o klatkę natychmiast, nie czekając na resztę odstępu spoczynkowego.
    /// Do zdarzeń, na które trzeba odpowiedzieć od razu - jak wjechanie kursorem
    /// na pastylkę, gdzie czekanie do 50 ms widać jako ociąganie.
    /// </summary>
    public void Nudge() => _wake.Set();

    public void Start()
    {
        if (_thread is not null) return;
        _running = true;
        _lastFrame = Stopwatch.GetTimestamp();
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "klatki nakładki",
        };
        _thread.Start();
    }

    public void Stop() => _running = false;

    private void Loop()
    {
        while (_running)
        {
            if (_active) WaitForCompositor();
            else
            {
                _wake.Wait(_idleMs);
                _wake.Reset();
            }

            if (!_running) return;
            Request();
        }
    }

    /// <summary>
    /// Czeka na kolejne odbicie kompozytora; przy wysokim odświeżaniu przepuszcza
    /// tyle odbić, ile trzeba, żeby nie schodzić poniżej <see cref="MinFrameMs"/>.
    /// </summary>
    private void WaitForCompositor()
    {
        while (_running)
        {
            if (!_composited)
            {
                Thread.Sleep(FallbackMs);
                return;
            }

            // Odstęp wyczekujemy kolejnymi DwmFlush, a nie Thread.Sleep: sen ma
            // ziarno 15,6 ms, więc przy rytmie 33 ms rozjeżdżał klatki do 23/s
            // i robił je nierównymi (zmierzone). DwmFlush trafia w rytm ekranu
            // co do odbicia, a samo wywołanie okazało się tanie.
            var before = Stopwatch.GetTimestamp();
            var hr = DwmFlush();
            var waited = Elapsed(before, Stopwatch.GetTimestamp());

            if (hr != 0 || waited < 1.0)
            {
                // Kompozytor nie nadaje rytmu - po serii takich powrotów przestajemy pytać,
                // inaczej ta pętla kręciłaby się bez opamiętania.
                if (++_instant >= GiveUpAfter) _composited = false;
            }
            else
            {
                _instant = 0;
            }

            var now = Stopwatch.GetTimestamp();
            if (Elapsed(_lastFrame, now) >= _minFrameMs || !_composited)
            {
                _lastFrame = now;
                return;
            }
        }
    }

    private static double Elapsed(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Prosi wątek UI o klatkę. Nigdy nie stoi w kolejce więcej niż jedna - gdy
    /// rysowanie nie nadąża, kolejne prośby po prostu przepadają zamiast się piętrzyć.
    /// </summary>
    private void Request()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;

        try
        {
            _target.BeginInvoke(_run);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            Volatile.Write(ref _pending, 0);
            _running = false;
        }
    }

    private void Run()
    {
        Volatile.Write(ref _pending, 0);
        _frame();
    }

    public void Dispose()
    {
        _running = false;
        _wake.Set();
        _thread?.Join(200);
        _thread = null;
        _wake.Dispose();
    }
}
