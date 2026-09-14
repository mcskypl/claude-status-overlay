using ClaudeStatus.Core.Account;
using ClaudeStatus.Core.Usage;

namespace ClaudeStatus.Core.Sessions;

/// <summary>
/// Odświeża obraz sesji w tle - co zadany interwał albo od razu, gdy w katalogu
/// stanu coś się zmieni. Dzięki temu pętla rysowania nigdy nie czeka na dysk
/// ani na odpytywanie procesów; bierze tylko gotowy <see cref="Current"/>.
/// </summary>
public sealed class SessionMonitor : IDisposable
{
    private readonly SessionReader _reader;
    private readonly IUsageSource _usage;
    private readonly AccountInfoReader _account;
    private readonly string _watchDirectory;
    private readonly TimeSpan _interval;
    private readonly AutoResetEvent _wake = new(false);

    private Thread? _thread;
    private FileSystemWatcher? _watcher;
    private volatile bool _stopping;
    private volatile bool _usageEnabled = true;
    private SessionSnapshot _current = SessionSnapshot.Empty;

    public SessionMonitor(SessionReader reader, IUsageSource usage, AccountInfoReader account, string watchDirectory, TimeSpan interval)
    {
        _reader = reader;
        _usage = usage;
        _account = account;
        _watchDirectory = watchDirectory;
        _interval = interval;
    }

    /// <summary>Najnowszy obraz; bezpieczny do czytania z dowolnego wątku.</summary>
    public SessionSnapshot Current => Volatile.Read(ref _current);

    /// <summary>
    /// Czy w ogóle pytać o limity. Wyłączone = źródło limitów nie jest dotykane,
    /// więc nakładka nie robi wtedy żadnego ruchu w sieci.
    /// </summary>
    public bool UsageEnabled
    {
        get => _usageEnabled;
        set
        {
            if (_usageEnabled == value) return;
            _usageEnabled = value;
            Refresh();
        }
    }

    /// <summary>Co ile źródło limitów samo sięga po nowe dane (ustawienie z menu nakładki).</summary>
    public TimeSpan UsageInterval
    {
        get => _usage.Interval;
        set => _usage.Interval = value;
    }

    /// <summary>Nowy obraz gotowy. Uwaga: wywoływane z wątku monitora.</summary>
    public event Action<SessionSnapshot>? Updated;

    /// <summary>Błąd jednej rundy odczytu - runda jest pomijana, monitor żyje dalej.</summary>
    public event Action<Exception>? Failed;

    public void Start()
    {
        if (_thread is not null) return;

        _thread = new Thread(Loop) { IsBackground = true, Name = "ClaudeStatus.SessionMonitor" };
        _thread.Start();
        TryWatch();
    }

    /// <summary>Poproś o odczyt teraz, nie czekając na interwał.</summary>
    public void Refresh() => _wake.Set();

    /// <summary>
    /// Ręczne odświeżenie limitów: pomija odstęp między pobraniami i budzi monitor,
    /// żeby nowe dane trafiły do obrazu od razu, a nie przy kolejnej rundzie.
    /// Przy wyłączonych limitach nic nie robi - nie ruszamy wtedy sieci.
    /// </summary>
    public void RefreshUsage()
    {
        if (!_usageEnabled) return;
        _usage.RefreshNow();
        Refresh();
    }

    private void Loop()
    {
        while (!_stopping)
        {
            try
            {
                var now = DateTime.Now;
                var usage = _usageEnabled ? _usage.Read(now) : UsageSnapshot.Empty;
                var account = _account.Read(now);
                var snapshot = new SessionSnapshot(_reader.Read(now), usage, account, now);
                Volatile.Write(ref _current, snapshot);
                Updated?.Invoke(snapshot);
            }
            catch (Exception ex)
            {
                Failed?.Invoke(ex);
            }

            _wake.WaitOne(_interval);
        }
    }

    // FileSystemWatcher to tylko "szturchnięcie" - gdy się nie uda, zostaje
    // zwykłe odpytywanie co interwał.
    private void TryWatch()
    {
        try
        {
            Directory.CreateDirectory(_watchDirectory);
            _watcher = new FileSystemWatcher(_watchDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Created += (_, _) => _wake.Set();
            _watcher.Changed += (_, _) => _wake.Set();
            _watcher.Deleted += (_, _) => _wake.Set();
            _watcher.Renamed += (_, _) => _wake.Set();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _watcher?.Dispose();
        _wake.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _wake.Dispose();
    }
}
