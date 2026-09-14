namespace ClaudeStatus.Core;

/// <summary>
/// Wszystkie ścieżki, z których korzysta nakładka i hook - w jednym miejscu.
/// Claude Code pozwala przenieść katalog konfiguracji zmienną CLAUDE_CONFIG_DIR,
/// więc honorujemy ją tak samo jak on.
/// </summary>
public static class ClaudePaths
{
    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string? ConfigDirOverride =
        NullIfBlank(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));

    /// <summary>Katalog konfiguracji Claude Code (domyślnie ~/.claude).</summary>
    public static string ConfigDir { get; } = ConfigDirOverride ?? Path.Combine(UserProfile, ".claude");

    /// <summary>Pliki stanu sesji zapisywane przez hooki.</summary>
    public static string StatusDir { get; } = Path.Combine(ConfigDir, "status");

    /// <summary>Transkrypty sesji: projects/&lt;projekt&gt;/&lt;session&gt;.jsonl.</summary>
    public static string ProjectsDir { get; } = Path.Combine(ConfigDir, "projects");

    /// <summary>Mapowanie sesja -&gt; pid procesu Claude Code.</summary>
    public static string SessionsDir { get; } = Path.Combine(ConfigDir, "sessions");

    /// <summary>Token OAuth Claude Code (tylko odczyt - do zapytania o limity).</summary>
    public static string CredentialsFile { get; } = Path.Combine(ConfigDir, ".credentials.json");

    /// <summary>settings.json z hookami.</summary>
    public static string SettingsFile { get; } = Path.Combine(ConfigDir, "settings.json");

    /// <summary>
    /// Plik, w którym Claude Code trzyma m.in. cachedUsageUtilization i dane
    /// zalogowanego konta (oauthAccount). Bez CLAUDE_CONFIG_DIR leży w profilu
    /// użytkownika, a nie w ~/.claude.
    /// </summary>
    public static string UsageCacheFile { get; } =
        Path.Combine(ConfigDirOverride ?? UserProfile, ".claude.json");

    /// <summary>To samo, co <see cref="UsageCacheFile"/> - alias pod właściwą nazwę dla czytelności wywołania.</summary>
    public static string AccountFile => UsageCacheFile;

    public static string OverlayConfigFile { get; } = Path.Combine(ConfigDir, "status-overlay.config.json");
    public static string OverlayLogFile { get; } = Path.Combine(ConfigDir, "status-overlay.log");
    public static string InstallDir { get; } = Path.Combine(ConfigDir, "status-overlay");

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
