using System.Text.Json;

namespace ClaudeStatus.Core.Usage;

/// <summary>
/// Limity na żywo z API, z cache Claude Code jako źródłem awaryjnym. Odczyt
/// jest natychmiastowy - zwraca nowsze z (ostatnia odpowiedź API, cache) -
/// a odświeżanie leci w tle: co <see cref="Interval"/> (domyślnie 5 min,
/// w nakładce do wyboru w menu), po błędzie z odstępem, a bez ważnego
/// tokena wcale (czekamy, aż Claude Code podmieni plik poświadczeń).
/// Gdy API milczy dłużej niż godzinę, dane dostają znak "?" jak każde inne
/// przykurzone; po dobie znikają.
/// </summary>
public sealed class LiveUsageSource : IUsageSource, IDisposable
{
    /// <summary>Odstęp, gdy nikt nie powie inaczej (menu nakładki podaje własny).</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryWithoutToken = TimeSpan.FromSeconds(30);

    private readonly UsageApiClient _api;
    private readonly OAuthCredentialStore _credentials;
    private readonly UsageCacheReader _cache;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();

    private Task? _inflight;
    private DateTime _nextAttempt = DateTime.MinValue;
    private TimeSpan _interval = DefaultInterval;
    private UsageData? _live;

    public LiveUsageSource(UsageApiClient api, OAuthCredentialStore credentials, UsageCacheReader cache)
    {
        _api = api;
        _credentials = credentials;
        _cache = cache;
    }

    /// <summary>Nieudane pobranie (sieć, HTTP, JSON). Komunikat nie zawiera tokena.</summary>
    public event Action<Exception>? Failed;

    /// <summary>
    /// Odstęp między automatycznymi pobraniami. Skrócenie nie każe czekać do
    /// końca poprzedniego, dłuższego odstępu - najbliższa próba przesuwa się
    /// najdalej o nową wartość.
    /// </summary>
    public TimeSpan Interval
    {
        get
        {
            lock (_gate) return _interval;
        }
        set
        {
            lock (_gate)
            {
                _interval = value;
                var cap = DateTime.Now + value;
                if (_nextAttempt > cap) _nextAttempt = cap;
            }
        }
    }

    public UsageSnapshot Read(DateTime now)
    {
        ScheduleRefresh(now);
        var newest = UsageData.Newest(Volatile.Read(ref _live), _cache.Read(now));
        return newest?.ToSnapshot(now) ?? UsageSnapshot.Empty;
    }

    /// <summary>
    /// Kasuje odstęp do następnej próby - kolejny <see cref="Read"/> od razu
    /// odpali pobranie. Jeśli akurat jedno leci, zostaje przy nim.
    /// </summary>
    public void RefreshNow()
    {
        lock (_gate)
        {
            _nextAttempt = DateTime.MinValue;
        }
    }

    private void ScheduleRefresh(DateTime now)
    {
        lock (_gate)
        {
            if (_cts.IsCancellationRequested) return;
            if (_inflight is { IsCompleted: false }) return;
            if (now < _nextAttempt) return;

            _nextAttempt = now + _interval;
            _inflight = Task.Run(() => RefreshAsync(_cts.Token), _cts.Token);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var token = _credentials.Current(DateTime.Now);
        if (token is null)
        {
            Postpone(RetryWithoutToken);
            return;
        }

        try
        {
            var data = await _api.FetchAsync(token.AccessToken, ct).ConfigureAwait(false);
            if (data is not null) Volatile.Write(ref _live, data);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // zamykamy się
        }
        catch (Exception ex) when (ex is HttpRequestException or UsageApiException or JsonException or OperationCanceledException)
        {
            // po błędzie odczekaj dłużej niż zwykle, ale nigdy krócej niż wybrany
            // odstęp - przy ustawieniu "rzadko" nie chcemy wracać do API co 2 minuty
            lock (_gate)
            {
                _nextAttempt = DateTime.Now + (RetryAfterFailure > _interval ? RetryAfterFailure : _interval);
            }
            Failed?.Invoke(ex);
        }
    }

    private void Postpone(TimeSpan delay)
    {
        lock (_gate)
        {
            _nextAttempt = DateTime.Now + delay;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
