namespace ClaudeStatus.Core.Liveness;

/// <summary>
/// Znajduje plik transkryptu sesji: projects/&lt;projekt&gt;/&lt;session&gt;.jsonl.
/// Hook zwykle podaje ścieżkę wprost; gdy nie, przeszukujemy katalog projektów,
/// ale nieudane szukanie powtarzamy najwyżej raz na minutę - to rekurencja.
/// </summary>
internal sealed class TranscriptLocator
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly string _projectsDir;
    private readonly Dictionary<string, (string? Path, DateTime CheckedAt)> _cache = new(StringComparer.Ordinal);

    public TranscriptLocator(string projectsDir)
    {
        _projectsDir = projectsDir;
    }

    public string? Find(string sessionId, string? hinted)
    {
        if (!string.IsNullOrWhiteSpace(hinted) && File.Exists(hinted)) return hinted;
        if (string.IsNullOrWhiteSpace(sessionId)) return null;

        if (_cache.TryGetValue(sessionId, out var cached))
        {
            if (cached.Path is not null && File.Exists(cached.Path)) return cached.Path;
            if (DateTime.Now - cached.CheckedAt < RetryAfter) return cached.Path;
        }

        var found = Search(sessionId);
        _cache[sessionId] = (found, DateTime.Now);
        return found;
    }

    private string? Search(string sessionId)
    {
        try
        {
            if (!Directory.Exists(_projectsDir)) return null;

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 3,
            };
            return Directory.EnumerateFiles(_projectsDir, sessionId + ".jsonl", options).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
