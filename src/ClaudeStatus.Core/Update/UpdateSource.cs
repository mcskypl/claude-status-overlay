using System.Reflection;

namespace ClaudeStatus.Core.Update;

/// <summary>
/// Skąd biorą się aktualizacje: publiczne wydania (Releases) na GitHubie.
/// Publiczne repozytorium = żadnego tokena w aplikacji; pytamy tylko o
/// najnowsze wydanie i pobieramy z niego instalator.
///
/// Po sforkowaniu projektu wystarczy podmienić <see cref="Repo"/>. Dopóki stoi
/// tu wartość zastępcza, nakładka w ogóle nie rusza sieci w sprawie aktualizacji.
/// </summary>
public static class UpdateSource
{
    /// <summary>Repozytorium w formie "właściciel/nazwa".</summary>
    public const string Repo = "OWNER/REPO";

    public static bool Configured => !Repo.Contains("OWNER", StringComparison.Ordinal);

    public static Uri LatestReleaseApi { get; } = new($"https://api.github.com/repos/{Repo}/releases/latest");

    public static Uri ReleasesPage { get; } = new($"https://github.com/{Repo}/releases");

    /// <summary>
    /// Wersja, która właśnie działa - z atrybutu zbudowanego pliku (Directory.Build.props).
    /// Zawsze trzyczłonowa, żeby porównanie z tagiem wydania ("v2.1.0") było uczciwe.
    /// </summary>
    public static Version Current { get; } = Normalize(
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>"2.1.0" albo "v2.1.0" -&gt; 2.1.0; null, gdy to nie wygląda na wersję.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        // "2.1.0-rc1" -> bierzemy część przed myślnikiem
        var dash = text.IndexOf('-');
        if (dash > 0) text = text[..dash];

        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
