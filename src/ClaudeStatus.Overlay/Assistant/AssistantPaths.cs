using System.Diagnostics;
using System.Text;
using ClaudeStatus.Core.Hooks;

namespace ClaudeStatus.Overlay.Assistant;

/// <summary>
/// Gdzie leżą trzy rzeczy, których asystent potrzebuje z zewnątrz: pliki widoku,
/// mostek Node i sam Node. Rozwiązywane w czasie działania, bo inaczej wyglądają
/// po zbudowaniu (wszystko obok exe), a inaczej przy pracy ze źródeł.
/// </summary>
internal static class AssistantPaths
{
    /// <summary>Katalog z <c>index.html</c>; kopiowany obok exe przez wpis w .csproj.</summary>
    public static string UiDirectory => Path.Combine(AppContext.BaseDirectory, "Assistant", "ui");

    /// <summary>Katalog danych WebView2 - własny, żeby nie mieszać się z innymi aplikacjami.</summary>
    public static string WebViewData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Claude Status Overlay", "WebView2");

    /// <summary>
    /// <c>assistant-bridge/index.mjs</c>: najpierw obok exe, potem w górę drzewa -
    /// przy uruchomieniu z bin/Debug repozytorium leży kilka poziomów wyżej.
    /// </summary>
    public static string? FindBridge()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "assistant-bridge", "index.mjs");
        if (File.Exists(beside)) return beside;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "assistant-bridge", "index.mjs");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Plik podręczny z podpowiedziami na pusty ekran - liczenie ich trwa kilkadziesiąt sekund.</summary>
    public static string SuggestionsCache => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Claude Status Overlay", "assistant-suggestions.json");

    /// <summary>
    /// Transkrypty sesji asystenta. Claude Code nazywa katalog projektu od cwd,
    /// zamieniając każdy znak spoza [A-Za-z0-9] na '-'.
    /// </summary>
    public static string TranscriptDirectory(string workingDirectory) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects",
        new string([.. workingDirectory.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]));

    /// <summary>Jednorazowy proces liczący podpowiedzi - <c>suggest.mjs</c> obok mostka.</summary>
    public static ProcessStartInfo? SuggestStartInfo(string workingDirectory)
    {
        var node = FindNode();
        var bridge = FindBridge();
        if (node is null || bridge is null) return null;

        var script = Path.Combine(Path.GetDirectoryName(bridge)!, "suggest.mjs");
        if (!File.Exists(script)) return null;

        var info = new ProcessStartInfo
        {
            FileName = node,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };
        info.Environment[HookProcessor.SilenceVariable] = HookProcessor.SilenceValue;
        info.ArgumentList.Add(script);
        info.ArgumentList.Add("--cwd");
        info.ArgumentList.Add(workingDirectory);
        return info;
    }

    /// <summary>Pełna ścieżka do node.exe albo null, gdy nie ma go ani w PATH, ani w Program Files.</summary>
    public static string? FindNode()
    {
        var fromPath = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var dir in fromPath)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var exe = Path.Combine(dir.Trim(), "node.exe");
                if (File.Exists(exe)) return exe;
            }
            catch (ArgumentException)
            {
                // wpis w PATH z niedozwolonym znakiem - pomijamy, to nie nasz problem
            }
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            var exe = Path.Combine(root, "nodejs", "node.exe");
            if (File.Exists(exe)) return exe;
        }

        return null;
    }

    /// <summary>Domyślny katalog roboczy asystenta - zaufane miejsce na pytania „o rzeczy".</summary>
    public static string DefaultWorkingDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "assistant");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Czy da się w ogóle wystartować; komunikat nadaje się wprost do pokazania.</summary>
    public static bool CanStart(out string problem)
    {
        if (FindNode() is null)
        {
            problem = "Nie znaleziono Node.js. Asystent potrzebuje go do uruchomienia mostka.";
            return false;
        }
        if (FindBridge() is null)
        {
            problem = "Nie znaleziono assistant-bridge/index.mjs obok aplikacji.";
            return false;
        }
        if (!File.Exists(Path.Combine(UiDirectory, "index.html")))
        {
            problem = "Nie znaleziono plików widoku (Assistant/ui/index.html).";
            return false;
        }
        problem = "";
        return true;
    }

    /// <summary>
    /// Mostek mówi i słucha po UTF-8 - jak każdy Node. Bez jawnego ustawienia
    /// .NET bierze kodowanie konsoli, a przy oknie bez konsoli schodzi do strony
    /// kodowej systemu (na polskim Windowsie 1250): „zgłoszenie" wracałoby wtedy
    /// jako „zgĹ‚oszenie", a polskie znaki w pytaniu ginęłyby po drodze w drugą stronę.
    /// </summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Uruchomienie mostka - w jednym miejscu, żeby test i produkcja robiły to tak samo.</summary>
    public static ProcessStartInfo BridgeStartInfo(string workingDirectory, string? resume, string? model = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = FindNode()!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };

        // Sesja asystenta odpala te same hooki, co każda inna - łącznie z naszym.
        // Wyciszamy go w całym poddrzewie procesów: stan tej jednej sesji nakładka
        // zna z pierwszej ręki i pokazuje go wprost, bez objazdu przez plik.
        info.Environment[HookProcessor.SilenceVariable] = HookProcessor.SilenceValue;

        info.ArgumentList.Add(FindBridge()!);
        info.ArgumentList.Add("--cwd");
        info.ArgumentList.Add(workingDirectory);
        if (!string.IsNullOrWhiteSpace(resume))
        {
            info.ArgumentList.Add("--resume");
            info.ArgumentList.Add(resume);
        }
        if (!string.IsNullOrWhiteSpace(model))
        {
            info.ArgumentList.Add("--model");
            info.ArgumentList.Add(model);
        }
        return info;
    }
}
