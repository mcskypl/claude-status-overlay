using ClaudeStatus.Core.Sessions;

namespace ClaudeStatus.Overlay.Assistant;

/// <summary>Co asystent robi w tej chwili - tyle, ile trzeba, żeby pokazać go w pastylce.</summary>
internal sealed record AssistantStatus(SessionState State, string Note, DateTime At);

/// <summary>
/// Sesja asystenta widziana od strony pastylki. Nie ma pliku stanu: hook jest
/// dla niej wyciszony (<see cref="Core.Hooks.HookProcessor.SilenceVariable"/>),
/// a wiersz na liście składa nakładka z tego, co sama zrobiła.
/// </summary>
internal static class AssistantSession
{
    /// <summary>Stały identyfikator - sesja żyje w pamięci, więc nie ma identyfikatora z dysku.</summary>
    public const string Id = "claude-status-assistant";

    public const string Name = "Asystent";

    public static SessionInfo Row(AssistantStatus status, string workingDirectory) =>
        new(Id, status.State, Name, workingDirectory, status.Note, status.At, FilePath: "");

    /// <summary>
    /// Kasuje wpisy zostawione przez wcześniejsze sesje asystenta. Zanim hook
    /// został wyciszony, zapisywał je jak każdą inną sesję - a że decyzja o zgodzie
    /// zapada w panelu, nie w terminalu, ostatni taki wpis zostawał w stanie
    /// "czeka na Ciebie" aż do wygaśnięcia po dobie i migał na pastylce.
    /// Katalog roboczy asystenta jest jego własny, więc rozpoznanie po nim jest
    /// jednoznaczne.
    /// </summary>
    public static void PurgeStale(SessionStatusStore store)
    {
        var home = Normalize(AssistantPaths.DefaultWorkingDirectory);

        foreach (var stored in store.ReadAll())
        {
            if (string.Equals(Normalize(stored.Document.Cwd), home, StringComparison.OrdinalIgnoreCase))
            {
                store.TryDelete(stored.FilePath);
            }
        }
    }

    private static string Normalize(string? path) =>
        path?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "";
}
