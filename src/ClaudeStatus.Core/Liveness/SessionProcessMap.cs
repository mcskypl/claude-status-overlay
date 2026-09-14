using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeStatus.Core.Json;

namespace ClaudeStatus.Core.Liveness;

/// <summary>Zawartość sessions/&lt;pid&gt;.json zapisywanego przez Claude Code.</summary>
public sealed class SessionProcessDocument
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("pid")]
    public int Pid { get; set; }
}

/// <summary>
/// Mapa sesja -&gt; pid procesu Claude Code. Wpisy zostają po zamkniętych
/// sesjach, więc sprawdzamy, że pod numerem nadal siedzi claude/node, a nie
/// przypadkowy nowy proces. Odświeżana najwyżej co 10 s.
/// </summary>
internal sealed class SessionProcessMap
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(10);
    private static readonly string[] KnownHosts = ["claude", "node"];

    private readonly string _sessionsDir;
    private Dictionary<string, int> _map = new(StringComparer.Ordinal);
    private DateTime _stamp = DateTime.MinValue;

    public SessionProcessMap(string sessionsDir)
    {
        _sessionsDir = sessionsDir;
    }

    public int PidOf(string sessionId)
    {
        RefreshIfStale();
        return _map.TryGetValue(sessionId, out var pid) ? pid : 0;
    }

    private void RefreshIfStale()
    {
        if (DateTime.Now - _stamp < MaxAge) return;
        _stamp = DateTime.Now;

        string[] files;
        try
        {
            files = Directory.Exists(_sessionsDir) ? Directory.GetFiles(_sessionsDir, "*.json") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var doc = TryRead(file);
            if (doc?.SessionId is null || doc.Pid <= 0) continue;
            if (!IsClaudeProcess(doc.Pid)) continue;
            map[doc.SessionId] = doc.Pid;
        }
        _map = map;
    }

    private static SessionProcessDocument? TryRead(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), CoreJsonContext.Default.SessionProcessDocument);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool IsClaudeProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return KnownHosts.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
