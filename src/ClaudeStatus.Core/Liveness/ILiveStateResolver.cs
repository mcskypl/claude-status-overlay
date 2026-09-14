using ClaudeStatus.Core.Sessions;

namespace ClaudeStatus.Core.Liveness;

/// <summary>Odpowiada na pytanie: czy stan zapisany przez hook jest jeszcze aktualny?</summary>
public interface ILiveStateResolver
{
    /// <param name="recorded">Stan z pliku.</param>
    /// <param name="sessionId">Identyfikator sesji Claude Code.</param>
    /// <param name="transcriptHint">Ścieżka transkryptu, jeśli hook ją dostał.</param>
    /// <param name="recordedAt">Kiedy stan został zapisany.</param>
    SessionState Resolve(SessionState recorded, string sessionId, string? transcriptHint, DateTime recordedAt);
}

/// <summary>Bez żadnych sprawdzeń - stan z pliku jest prawdą.</summary>
public sealed class PassThroughLiveStateResolver : ILiveStateResolver
{
    public SessionState Resolve(SessionState recorded, string sessionId, string? transcriptHint, DateTime recordedAt)
        => recorded;
}
