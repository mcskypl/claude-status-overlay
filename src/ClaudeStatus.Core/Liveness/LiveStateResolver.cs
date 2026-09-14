using ClaudeStatus.Core.Sessions;

namespace ClaudeStatus.Core.Liveness;

/// <summary>
/// Claude Code nie ma zdarzenia "zgoda udzielona" ani "odmowa" (kolejność to
/// PreToolUse -> PermissionRequest -> pytanie -> narzędzie), więc po kliknięciu
/// Allow żaden hook się nie odpala i stan "czeka na Ciebie" wisiałby do końca
/// tury. Szukamy więc dowodów, że tura znowu leci:
/// <list type="number">
///   <item>transkrypt sesji urósł po alercie - Claude coś dopisał (odpowiedź,
///   wynik narzędzia, także informację o odmowie);</item>
///   <item>narzędzie odpalone po alercie nadal działa - przypadek "zgoda na
///   długi build": przez minuty do transkryptu nie trafia ani jedna linia.</item>
/// </list>
/// </summary>
public sealed class LiveStateResolver : ILiveStateResolver
{
    /// <summary>Zapas na kolejność zdarzeń (hook alertu vs zapis transkryptu).</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);

    private readonly TranscriptLocator _transcripts;
    private readonly ToolActivityDetector _tools;

    public LiveStateResolver(string projectsDir, string sessionsDir)
    {
        _transcripts = new TranscriptLocator(projectsDir);
        _tools = new ToolActivityDetector(new SessionProcessMap(sessionsDir));
    }

    public SessionState Resolve(SessionState recorded, string sessionId, string? transcriptHint, DateTime recordedAt)
    {
        if (!recorded.NeedsAttention()) return recorded;

        var transcript = _transcripts.Find(sessionId, transcriptHint);
        if (transcript is not null && ModifiedAfter(transcript, recordedAt + Grace)) return SessionState.Working;

        if (_tools.IsBusy(sessionId, recordedAt)) return SessionState.Working;

        return recorded;
    }

    private static bool ModifiedAfter(string path, DateTime moment)
    {
        try
        {
            return File.GetLastWriteTime(path) > moment;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
