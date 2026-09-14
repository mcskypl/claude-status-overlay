using System.Text.Json;

namespace ClaudeStatus.Core.Usage;

/// <summary>
/// Limity z pliku, który Claude Code sam utrzymuje: ~/.claude.json -&gt;
/// cachedUsageUtilization:
/// <code>
/// "cachedUsageUtilization": {
///   "fetchedAtMs": 1789117228324,
///   "utilization": {
///     "five_hour": { "utilization": 0,  "resets_at": "2026-09-11T12:30:00+00:00" },
///     "seven_day": { "utilization": 20, "resets_at": "2026-09-17T08:00:00+00:00" }
///   }
/// }
/// </code>
/// Uwaga: ten wpis odświeża tylko terminalowe TUI Claude Code - sesje w VS Code
/// go nie ruszają, więc bywa wielogodzinny. Dlatego to źródło awaryjne dla
/// <see cref="LiveUsageSource"/>, nie główne. Plik ma kilkadziesiąt kB
/// i zmienia się rzadko - czytamy go tylko po zmianie.
/// </summary>
public sealed class UsageCacheReader
{
    /// <summary>Jak często wolno w ogóle zerknąć na plik.</summary>
    private static readonly TimeSpan MinReadInterval = TimeSpan.FromSeconds(10);

    private readonly string _path;
    private DateTime _lastAttempt = DateTime.MinValue;
    private (long Ticks, long Length)? _stamp;
    private UsageData? _data;

    public UsageCacheReader(string path)
    {
        _path = path;
    }

    /// <summary>Ostatnie sensowne dane z pliku albo null, gdy ich nie ma.</summary>
    public UsageData? Read(DateTime now)
    {
        RefreshIfNeeded(now);
        return _data;
    }

    private void RefreshIfNeeded(DateTime now)
    {
        if (now - _lastAttempt < MinReadInterval) return;
        _lastAttempt = now;

        FileInfo info;
        try
        {
            info = new FileInfo(_path);
            if (!info.Exists) return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }

        var stamp = (info.LastWriteTimeUtc.Ticks, info.Length);
        if (_stamp == stamp && _data is not null) return;

        var parsed = TryParse();
        _stamp = stamp;
        // połowicznie zapisany plik = brak danych; zostajemy przy poprzednich
        if (parsed is not null) _data = parsed;
    }

    private UsageData? TryParse()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty("cachedUsageUtilization", out var cached)) return null;
            if (!cached.TryGetProperty("utilization", out var windows)) return null;

            // bez czasu pobrania nie wiemy, jak stare są dane - traktujemy jak martwe
            var fetchedAt = DateTime.MinValue;
            if (cached.TryGetProperty("fetchedAtMs", out var ms) && ms.TryGetInt64(out var unixMs))
            {
                fetchedAt = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime;
            }

            return UsageData.FromWindows(windows, fetchedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
