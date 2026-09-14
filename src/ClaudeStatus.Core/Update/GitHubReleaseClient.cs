using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeStatus.Core.Update;

/// <summary>Nowsze wydanie niż to, które chodzi.</summary>
/// <param name="Version">Wersja z tagu wydania.</param>
/// <param name="Name">Nazwa wydania do pokazania człowiekowi (zwykle "2.1.0").</param>
/// <param name="PageUrl">Strona wydania na GitHubie - dla trybu "tylko powiadom".</param>
/// <param name="SetupUrl">Instalator z załączników wydania; null, gdy wydanie go nie ma.</param>
/// <param name="SetupSize">Rozmiar instalatora w bajtach (0 = nieznany).</param>
public sealed record UpdateInfo(Version Version, string Name, string PageUrl, string? SetupUrl, long SetupSize)
{
    public bool CanInstall => SetupUrl is { Length: > 0 };
}

/// <summary>Odpowiedź inna niż 2xx z api.github.com.</summary>
public sealed class GitHubApiException(HttpStatusCode status) : Exception($"HTTP {(int)status} {status}")
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>
/// Pyta publiczne API GitHuba o najnowsze wydanie. Bez tokena - publiczne
/// repozytorium pozwala na to każdemu (limit 60 zapytań na godzinę z jednego
/// adresu, a my pytamy raz na dobę).
/// </summary>
public sealed class GitHubReleaseClient : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;

    public GitHubReleaseClient()
    {
        _http = new HttpClient { Timeout = Timeout };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ClaudeStatusOverlay/{UpdateSource.Current}");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    /// <summary>
    /// Najnowsze wydanie albo null, gdy nie jest nowsze od bieżącego.
    /// Szkice i wydania wstępne pomija samo API (/releases/latest ich nie zwraca).
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(UpdateSource.LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        // repozytorium bez żadnego wydania odpowiada 404 - to nie jest błąd, tylko "jeszcze nic nie ma"
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new GitHubApiException(response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (UpdateSource.ParseTag(tag) is not { } version) return null;
        if (version <= UpdateSource.Current) return null;

        var page = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;
        var (setupUrl, setupSize) = FindSetupAsset(root);

        return new UpdateInfo(version, version.ToString(), page ?? UpdateSource.ReleasesPage.ToString(), setupUrl, setupSize);
    }

    /// <summary>Z załączników wydania bierzemy instalator (.exe); paczka portable nie nadaje się do samoaktualizacji.</summary>
    private static (string? Url, long Size) FindSetupAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return (null, 0);

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (url is null) continue;

            var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            return (url, size);
        }

        return (null, 0);
    }

    public void Dispose() => _http.Dispose();
}
