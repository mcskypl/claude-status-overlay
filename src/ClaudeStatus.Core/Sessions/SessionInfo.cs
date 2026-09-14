namespace ClaudeStatus.Core.Sessions;

/// <summary>Sesja po rozstrzygnięciu, jaki stan naprawdę obowiązuje - to widzi UI.</summary>
public sealed record SessionInfo(
    string SessionId,
    SessionState State,
    string Project,
    string? Cwd,
    string? Note,
    DateTime Timestamp,
    string FilePath)
{
    /// <summary>Nazwa do pokazania: projekt, a gdy go brak - identyfikator sesji.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Project) ? SessionId : Project;

    /// <summary>Najpierw stany wymagające uwagi, w obrębie stanu - najświeższe.</summary>
    public static readonly Comparison<SessionInfo> DisplayOrder = (a, b) =>
    {
        var byPriority = a.State.Priority().CompareTo(b.State.Priority());
        return byPriority != 0 ? byPriority : b.Timestamp.CompareTo(a.Timestamp);
    };
}
