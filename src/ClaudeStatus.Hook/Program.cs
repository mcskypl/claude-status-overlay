using ClaudeStatus.Core;
using ClaudeStatus.Core.Hooks;
using ClaudeStatus.Core.Sessions;

namespace ClaudeStatus.Hook;

/// <summary>
/// Wywoływany przez hooki Claude Code. Czyta JSON zdarzenia ze stdin i zapisuje
/// plik stanu sesji, który nakładka zamienia na pierścień.
/// <code>
/// ClaudeStatusHook.exe --state working [--note "..."]
/// ClaudeStatusHook.exe --install      (dopisuje hooki do settings.json)
/// ClaudeStatusHook.exe --uninstall
/// </code>
/// Zapis stanu nigdy nie kończy się błędem widocznym dla Claude'a - hook ma
/// być niewidoczny, a każda usterka trafia najwyżej na stderr.
/// </summary>
internal static class Program
{
    private const char ByteOrderMark = (char)0xFEFF;

    private static int Main(string[] args)
    {
        var options = CommandLine.Parse(args);

        return options.Mode switch
        {
            CommandLine.Command.Install => Install(),
            CommandLine.Command.Uninstall => Uninstall(),
            CommandLine.Command.State => WriteState(options),
            _ => Usage(),
        };
    }

    private static int WriteState(CommandLine options)
    {
        // Sesja prowadzona przez samą nakładkę - jej stan idzie wprost do pastylki,
        // bez pośrednictwa pliku. Zapis tutaj tylko by go przekłamywał.
        if (HookProcessor.Silenced) return 0;

        try
        {
            var action = HookActions.Parse(options.State);
            if (action is null)
            {
                Console.Error.WriteLine($"Nieznany stan: {options.State}");
                return 0;
            }

            var payload = ReadPayload();
            var processor = new HookProcessor(new SessionStatusStore(ClaudePaths.StatusDir));
            processor.Apply(action.Value, payload, options.Note, DateTime.Now);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ClaudeStatusHook: {ex.Message}");
        }
        return 0;
    }

    private static HookPayload? ReadPayload()
    {
        // bez przekierowanego stdin ReadToEnd czekałby na klawiaturę
        if (!Console.IsInputRedirected) return null;

        string raw;
        try
        {
            raw = Console.In.ReadToEnd();
        }
        catch (IOException)
        {
            return null;
        }

        // PowerShell potrafi dokleić BOM na początku potoku
        var trimmed = raw.AsSpan().TrimStart(ByteOrderMark).Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{') return null;

        return HookPayload.TryParse(trimmed);
    }

    private static int Install()
    {
        try
        {
            var exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Nie udało się ustalić ścieżki ClaudeStatusHook.exe.");

            var registrar = new HookSettingsRegistrar(ClaudePaths.SettingsFile);
            registrar.Install(exe);

            Console.WriteLine($"Hooki dopisane do: {ClaudePaths.SettingsFile}");
            if (registrar.LastBackupPath is { } backup) Console.WriteLine($"  kopia zapasowa: {backup}");
            Console.WriteLine($"  zdarzenia: {string.Join(", ", HookSettingsRegistrar.EventNames)}");
            Console.WriteLine($"  hook: {exe}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Instalacja hooków nie powiodła się: {ex.Message}");
            return 1;
        }
    }

    private static int Uninstall()
    {
        try
        {
            var registrar = new HookSettingsRegistrar(ClaudePaths.SettingsFile);
            registrar.Uninstall();
            Console.WriteLine($"Hooki usunięte z: {ClaudePaths.SettingsFile}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Usuwanie hooków nie powiodło się: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Użycie: ClaudeStatusHook.exe --state <idle|working|done|attention|error|end> [--note tekst]");
        Console.Error.WriteLine("        ClaudeStatusHook.exe --install | --uninstall");
        return 2;
    }
}
