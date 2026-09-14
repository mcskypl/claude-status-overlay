namespace ClaudeStatus.Core.Sessions;

/// <summary>Sesja zmieniła stan; <see cref="Previous"/> jest null dla sesji widzianej pierwszy raz.</summary>
public readonly record struct StateTransition(SessionInfo Session, SessionState? Previous);

/// <summary>
/// Porównuje kolejne obrazy sesji i wyłuskuje zmiany stanu - na tym opierają
/// się dźwięki. Pamięta tylko ostatni obraz, więc sesje, które zniknęły,
/// po prostu przestają być śledzone.
/// </summary>
public sealed class StateChangeTracker
{
    private Dictionary<string, SessionState> _previous = new(StringComparer.Ordinal);

    public IReadOnlyList<StateTransition> Apply(SessionSnapshot snapshot)
    {
        var transitions = new List<StateTransition>();
        var next = new Dictionary<string, SessionState>(snapshot.Sessions.Count, StringComparer.Ordinal);

        foreach (var session in snapshot.Sessions)
        {
            next[session.SessionId] = session.State;

            if (_previous.TryGetValue(session.SessionId, out var old))
            {
                if (old != session.State) transitions.Add(new StateTransition(session, old));
            }
            else
            {
                transitions.Add(new StateTransition(session, null));
            }
        }

        _previous = next;
        return transitions;
    }
}
