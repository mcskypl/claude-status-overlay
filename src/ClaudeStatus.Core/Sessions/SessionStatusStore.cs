using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeStatus.Core.Json;

namespace ClaudeStatus.Core.Sessions;

/// <summary>Plik stanu odczytany z dysku wraz z czasem, którego dotyczy.</summary>
public sealed record StoredSessionStatus(string FilePath, SessionStatusDocument Document, DateTime Timestamp);

/// <summary>
/// Katalog z plikami stanu sesji. Jedyne miejsce, które zna układ tych plików -
/// hook przez niego zapisuje, nakładka przez niego czyta i sprząta.
/// </summary>
public sealed partial class SessionStatusStore
{
    private const int MaxFileStem = 80;

    // bez escapowania "+" i polskich liter - plik ma być czytelny także dla człowieka
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        TypeInfoResolver = CoreJsonContext.Default,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public SessionStatusStore(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public void EnsureDirectory() => System.IO.Directory.CreateDirectory(Directory);

    /// <summary>Ścieżka pliku dla sesji; identyfikator jest oczyszczany do bezpiecznej nazwy.</summary>
    public string PathFor(string sessionId) => Path.Combine(Directory, SafeStem(sessionId) + ".json");

    public static string SafeStem(string sessionId)
    {
        var stem = UnsafeChars().Replace(sessionId, "_");
        return stem.Length > MaxFileStem ? stem[..MaxFileStem] : stem;
    }

    public IReadOnlyList<StoredSessionStatus> ReadAll()
    {
        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(Directory, "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var result = new List<StoredSessionStatus>(files.Length);
        foreach (var file in files)
        {
            var doc = TryRead(file);
            if (doc is null) continue;

            var ts = doc.ParseTimestamp() ?? SafeLastWrite(file);
            result.Add(new StoredSessionStatus(file, doc, ts));
        }
        return result;
    }

    public SessionStatusDocument? TryRead(string path)
    {
        try
        {
            // ReadAllText rozpoznaje BOM, który zostawiał PowerShell 5.1
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return JsonSerializer.Deserialize(text, CoreJsonContext.Default.SessionStatusDocument);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // plik w trakcie zapisu albo uszkodzony - pominiemy go w tej rundzie
            return null;
        }
    }

    /// <summary>
    /// Zapis atomowy (tmp + podmiana), żeby nakładka nie złapała połowy pliku.
    /// Gdy podmiana się nie uda (plik akurat czytany), piszemy wprost.
    /// </summary>
    public void Write(SessionStatusDocument document)
    {
        EnsureDirectory();
        var target = PathFor(document.SessionId);
        var json = JsonSerializer.Serialize(document, typeof(SessionStatusDocument), WriteOptions);
        var tmp = target + ".tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, target, overwrite: true);
        }
        catch (IOException)
        {
            File.WriteAllText(target, json);
            TryDelete(tmp);
        }
    }

    public bool Delete(string sessionId) => TryDelete(PathFor(sessionId));

    public bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static DateTime SafeLastWrite(string path)
    {
        try { return File.GetLastWriteTime(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.Now; }
    }

    [GeneratedRegex(@"[^A-Za-z0-9_\-.]")]
    private static partial Regex UnsafeChars();
}
