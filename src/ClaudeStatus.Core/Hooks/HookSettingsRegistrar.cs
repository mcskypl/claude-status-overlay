using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeStatus.Core.Hooks;

/// <summary>
/// Dopisuje / usuwa nasze hooki w settings.json Claude Code, nie ruszając
/// pozostałych ustawień ani cudzych hooków. Nasze wpisy rozpoznajemy po nazwie
/// pliku hooka w komendzie, więc operacja jest idempotentna.
/// </summary>
public sealed class HookSettingsRegistrar
{
    /// <summary>Nazwa exe hooka - musi zgadzać się z AssemblyName projektu ClaudeStatus.Hook.</summary>
    public const string HookExecutableName = "ClaudeStatusHook.exe";

    private const int HookTimeoutSeconds = 15;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _settingsPath;

    public HookSettingsRegistrar(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    /// <summary>Zarejestrowane zdarzenia - do wypisania użytkownikowi.</summary>
    public static IEnumerable<string> EventNames => HookActions.ByEvent.Select(e => e.Key);

    public void Install(string hookExecutablePath)
    {
        var root = Load();
        RemoveOurs(root);

        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }

        foreach (var (eventName, action) in HookActions.ByEvent)
        {
            if (hooks[eventName] is not JsonArray entries)
            {
                entries = new JsonArray();
                hooks[eventName] = entries;
            }
            entries.Add(BuildEntry(hookExecutablePath, action));
        }

        Save(root);
    }

    public void Uninstall()
    {
        if (!File.Exists(_settingsPath)) return;
        var root = Load();
        RemoveOurs(root);
        Save(root);
    }

    /// <summary>
    /// Pojedynczy string komendy (nie command + args): idzie przez powłokę, która
    /// przekazuje stdin z JSON-em zdarzenia. Async, żeby nie spowalniać Claude'a.
    /// </summary>
    private static JsonObject BuildEntry(string hookExecutablePath, HookAction action) => new()
    {
        ["hooks"] = new JsonArray(new JsonObject
        {
            ["type"] = "command",
            ["command"] = $"\"{hookExecutablePath}\" --state {action.ToKey()}",
            ["async"] = true,
            ["timeout"] = HookTimeoutSeconds,
        }),
    };

    private static void RemoveOurs(JsonObject root)
    {
        if (root["hooks"] is not JsonObject hooks) return;

        foreach (var eventName in hooks.Select(p => p.Key).ToList())
        {
            if (hooks[eventName] is not JsonArray entries)
            {
                continue;
            }

            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (IsOurs(entries[i])) entries.RemoveAt(i);
            }

            if (entries.Count == 0) hooks.Remove(eventName);
        }

        if (hooks.Count == 0) root.Remove("hooks");
    }

    private static bool IsOurs(JsonNode? entry)
    {
        if (entry is not JsonObject obj || obj["hooks"] is not JsonArray handlers) return false;

        foreach (var handler in handlers)
        {
            if (handler is not JsonObject h) continue;

            var text = h["command"]?.GetValue<string>() ?? "";
            if (h["args"] is JsonArray args)
            {
                text += " " + string.Join(' ', args.Select(a => a?.ToString() ?? ""));
            }

            if (text.Contains(HookExecutableName, StringComparison.OrdinalIgnoreCase)) return true;
            // wpisy starej wersji PowerShellowej też sprzątamy
            if (text.Contains("claude-status-hook.ps1", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private JsonObject Load()
    {
        if (!File.Exists(_settingsPath)) return new JsonObject();

        var text = File.ReadAllText(_settingsPath);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();

        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidDataException($"{_settingsPath}: oczekiwano obiektu JSON na najwyższym poziomie.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Plik {_settingsPath} nie jest poprawnym JSON-em. Popraw go albo zmień nazwę i spróbuj ponownie.", ex);
        }
    }

    private void Save(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        if (File.Exists(_settingsPath))
        {
            var backup = $"{_settingsPath}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_settingsPath, backup, overwrite: true);
            LastBackupPath = backup;
        }

        var tmp = _settingsPath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOptions) + Environment.NewLine);
        File.Move(tmp, _settingsPath, overwrite: true);
    }

    /// <summary>Ścieżka kopii zapasowej zrobionej przy ostatnim zapisie (do wypisania).</summary>
    public string? LastBackupPath { get; private set; }
}
