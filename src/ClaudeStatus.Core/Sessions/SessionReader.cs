using ClaudeStatus.Core.Liveness;

namespace ClaudeStatus.Core.Sessions;

/// <summary>
/// Zamienia pliki stanu na listę sesji do wyświetlenia: odsiewa martwe wpisy,
/// rozstrzyga stan faktyczny i sortuje.
/// </summary>
public sealed class SessionReader
{
    /// <summary>Wpisy starsze niż doba traktujemy jako pozostałość po zamkniętej sesji.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly SessionStatusStore _store;
    private readonly ILiveStateResolver _liveness;

    public SessionReader(SessionStatusStore store, ILiveStateResolver liveness)
    {
        _store = store;
        _liveness = liveness;
    }

    public IReadOnlyList<SessionInfo> Read(DateTime now)
    {
        var rows = new List<SessionInfo>();
        foreach (var stored in _store.ReadAll())
        {
            if (now - stored.Timestamp > MaxAge)
            {
                _store.TryDelete(stored.FilePath);
                continue;
            }

            var doc = stored.Document;
            var recorded = SessionStateExtensions.Parse(doc.State) ?? SessionState.Idle;
            var state = _liveness.Resolve(recorded, doc.SessionId, doc.Transcript, stored.Timestamp);

            rows.Add(new SessionInfo(
                doc.SessionId,
                state,
                doc.Project ?? "",
                doc.Cwd,
                doc.Note,
                stored.Timestamp,
                stored.FilePath));
        }

        rows.Sort(SessionInfo.DisplayOrder);
        return rows;
    }
}
