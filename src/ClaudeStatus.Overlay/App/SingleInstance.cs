namespace ClaudeStatus.Overlay.App;

/// <summary>
/// Jedna nakładka naraz. Mutex trzyma instancję, a nazwane zdarzenie pozwala
/// instalatorowi (albo użytkownikowi: <c>ClaudeStatusOverlay.exe --exit</c>)
/// poprosić działającą nakładkę o grzeczne zamknięcie.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // ta sama nazwa co w wersji PowerShellowej - obie nie uruchomią się jednocześnie
    private const string MutexName = @"Global\ClaudeStatusOverlay_v1";
    private const string ExitEventName = @"Global\ClaudeStatusOverlay_v1_exit";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _exitEvent;
    private readonly RegisteredWaitHandle _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle exitEvent)
    {
        _mutex = mutex;
        _exitEvent = exitEvent;
        _registration = ThreadPool.RegisterWaitForSingleObject(
            exitEvent, (_, _) => ExitRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>Ktoś poprosił o zamknięcie. Wywoływane z wątku puli.</summary>
    public event Action? ExitRequested;

    /// <summary>Null, gdy nakładka już działa.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        if (!owned)
        {
            mutex.Dispose();
            return null;
        }

        var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        return new SingleInstance(mutex, exitEvent);
    }

    /// <summary>Poproś działającą instancję o zamknięcie; false, gdy żadnej nie ma.</summary>
    public static bool SignalExit()
    {
        if (!EventWaitHandle.TryOpenExisting(ExitEventName, out var handle)) return false;
        using (handle)
        {
            handle.Set();
        }
        return true;
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _exitEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // zwalniany z innego wątku niż przejęty - i tak zaraz ginie
        }
        _mutex.Dispose();
    }
}
