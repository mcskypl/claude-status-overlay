using System.Diagnostics;
using System.Text.Json;

namespace ClaudeStatus.Overlay.Assistant;

/// <summary>
/// Podpowiedzi na pusty ekran, wyprowadzone z wcześniejszych pytań do asystenta.
/// Liczy je mały model w <c>suggest.mjs</c>, co trwa kilkadziesiąt sekund - więc
/// ekran zawsze dostaje ostatni wynik z pliku od ręki, a nowy powstaje w tle,
/// i to tylko wtedy, gdy od poprzedniego przybyła jakaś rozmowa.
/// </summary>
internal sealed class AssistantSuggestions
{
    /// <summary>Najrzadziej tyle między dwoma przeliczeniami - każde to zapytanie do API.</summary>
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(20);

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private readonly string _workingDirectory;
    private readonly object _gate = new();
    private Cache _cache;
    private DateTime _lastAttempt = DateTime.MinValue;
    private bool _running;

    public AssistantSuggestions(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        _cache = Load();
    }

    /// <summary>Nowe podpowiedzi są gotowe. Wołane z wątku puli.</summary>
    public event Action<string[]>? Changed;

    public event Action<string>? Log;

    public string[] Items => _cache.Items;

    private sealed record Cache(string[] Items, DateTime SourceStampUtc);

    /// <summary>Przelicza w tle, jeśli od ostatniego razu przybyło historii i nie było to przed chwilą.</summary>
    public void RefreshIfStale()
    {
        var newest = NewestTranscriptUtc();
        if (newest is null) return;

        lock (_gate)
        {
            if (_running || newest <= _cache.SourceStampUtc) return;
            if (DateTime.UtcNow - _lastAttempt < MinInterval) return;
            _running = true;
            _lastAttempt = DateTime.UtcNow;
        }

        _ = Task.Run(() => Generate(newest.Value));
    }

    private async Task Generate(DateTime stamp)
    {
        try
        {
            var info = AssistantPaths.SuggestStartInfo(_workingDirectory);
            if (info is null) return;

            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(Timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                Log?.Invoke("podpowiedzi: przekroczony czas");
                return;
            }

            if (process.ExitCode != 0)
            {
                Log?.Invoke($"podpowiedzi: exit={process.ExitCode} {(await stderr).Trim()}");
                return;
            }

            var items = Parse(await stdout);
            if (items is null)
            {
                Log?.Invoke("podpowiedzi: nieczytelna odpowiedz skryptu");
                return;
            }

            // Pusta lista też jest wynikiem (za mało historii) - zapamiętujemy ją,
            // żeby nie pytać ponownie, dopóki nie przybędzie rozmów.
            _cache = new Cache(items, stamp);
            Save(_cache);
            Changed?.Invoke(items);
        }
        catch (Exception ex)
        {
            Log?.Invoke("podpowiedzi: " + ex.Message);
        }
        finally
        {
            lock (_gate) _running = false;
        }
    }

    private static string[]? Parse(string stdout)
    {
        var line = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (line is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("suggestions", out var list) || list.ValueKind != JsonValueKind.Array) return null;
            return [.. list.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!.Trim())
                .Where(x => x.Length > 0)];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private DateTime? NewestTranscriptUtc()
    {
        try
        {
            var dir = new DirectoryInfo(AssistantPaths.TranscriptDirectory(_workingDirectory));
            if (!dir.Exists) return null;
            var files = dir.GetFiles("*.jsonl");
            return files.Length == 0 ? null : files.Max(f => f.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Cache Load()
    {
        try
        {
            var path = AssistantPaths.SuggestionsCache;
            if (File.Exists(path))
            {
                var cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(path));
                if (cache?.Items is not null) return cache;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // zepsuty plik podręczny - policzymy od nowa
        }
        return new Cache([], DateTime.MinValue);
    }

    private static void Save(Cache cache)
    {
        try
        {
            var path = AssistantPaths.SuggestionsCache;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // bez pliku podpowiedzi policzą się znowu przy następnym starcie
        }
    }
}
