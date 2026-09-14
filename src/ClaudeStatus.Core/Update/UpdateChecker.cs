using System.Text.Json;

namespace ClaudeStatus.Core.Update;

/// <summary>
/// Pilnuje, czy na GitHubie nie ma nowszego wydania. Pytanie leci w tle raz na
/// dobę (i raz krótko po starcie), a odczyt wyniku jest natychmiastowy - pętla
/// rysowania nigdy nie czeka na sieć.
///
/// Wzorzec jest ten sam co w <c>LiveUsageSource</c>: wywołujący woła
/// <see cref="Poll"/> tak często, jak mu wygodnie, a to ta klasa decyduje,
/// czy już pora.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(2);

    /// <summary>Pierwsze sprawdzenie chwilę po starcie - żeby nie konkurować z rozruchem nakładki.</summary>
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(25);

    private readonly GitHubReleaseClient _client;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();

    private Task? _inflight;
    private DateTime _nextCheck;
    private UpdateInfo? _available;

    public UpdateChecker(GitHubReleaseClient client)
    {
        _client = client;
        _nextCheck = DateTime.Now + FirstDelay;
    }

    /// <summary>Nieudane sprawdzenie (sieć, HTTP, JSON) - nakładka tylko to loguje.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Znaleziono nowsze wydanie. Uwaga: wywoływane z wątku puli.</summary>
    public event Action<UpdateInfo>? Found;

    /// <summary>Czy w ogóle sprawdzać. Wyłączone = zero ruchu w sieci.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Nowsze wydanie albo null. Bezpieczne do czytania z dowolnego wątku.</summary>
    public UpdateInfo? Available => Volatile.Read(ref _available);

    /// <summary>Czy trwa sprawdzanie (do pokazania „sprawdzam...").</summary>
    public bool Busy
    {
        get
        {
            lock (_gate) return _inflight is { IsCompleted: false };
        }
    }

    /// <summary>Sprawdź, jeśli już pora. Tanie - można wołać co klatkę.</summary>
    public void Poll(DateTime now)
    {
        if (!Enabled || !UpdateSource.Configured) return;
        if (now < _nextCheck) return;
        Start(CheckEvery);
    }

    /// <summary>Sprawdź teraz, niezależnie od harmonogramu (menu „Sprawdź aktualizacje").</summary>
    public void CheckNow() => Start(CheckEvery);

    private void Start(TimeSpan nextIn)
    {
        lock (_gate)
        {
            if (_cts.IsCancellationRequested) return;
            if (_inflight is { IsCompleted: false }) return;

            _nextCheck = DateTime.Now + nextIn;
            _inflight = Task.Run(() => CheckAsync(_cts.Token), _cts.Token);
        }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        if (!UpdateSource.Configured) return;

        try
        {
            var info = await _client.CheckAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _available, info);
            if (info is not null) Found?.Invoke(info);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // zamykamy się
        }
        catch (Exception ex) when (ex is HttpRequestException or GitHubApiException or JsonException or OperationCanceledException)
        {
            lock (_gate)
            {
                _nextCheck = DateTime.Now + RetryAfterFailure;
            }
            Failed?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
