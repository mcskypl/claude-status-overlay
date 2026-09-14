using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeStatus.Core.Json;

namespace ClaudeStatus.Core.Usage;

/// <summary>Zawartość ~/.claude/.credentials.json - czytamy tylko to, co potrzebne.</summary>
public sealed class OAuthCredentialsDocument
{
    [JsonPropertyName("claudeAiOauth")]
    public OAuthSection? ClaudeAiOauth { get; set; }

    public sealed class OAuthSection
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        /// <summary>Unix ms.</summary>
        [JsonPropertyName("expiresAt")]
        public long? ExpiresAt { get; set; }
    }
}

/// <summary>Token dostępu wraz z terminem ważności; refresh token nas nie interesuje.</summary>
public sealed record OAuthToken(string AccessToken, DateTime? ExpiresAt)
{
    public bool IsExpired(DateTime now) => ExpiresAt is { } expires && now >= expires;
}

/// <summary>
/// Token OAuth Claude Code z pliku poświadczeń - wyłącznie do odczytu. Nakładka
/// nigdy go nie odświeża (rotacja refresh tokena zepsułaby sesję CLI) ani nie
/// zapisuje; gdy wygaśnie, czekamy, aż Claude Code sam podmieni plik.
/// </summary>
public sealed class OAuthCredentialStore
{
    private readonly string _path;
    private (long Ticks, long Length)? _stamp;
    private OAuthToken? _token;

    public OAuthCredentialStore(string path)
    {
        _path = path;
    }

    /// <summary>Ważny token albo null (brak pliku, konto na klucz API, token wygasł).</summary>
    public OAuthToken? Current(DateTime now)
    {
        RefreshIfChanged();
        return _token is { } token && !token.IsExpired(now) ? token : null;
    }

    private void RefreshIfChanged()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists)
            {
                _token = null;
                _stamp = null;
                return;
            }

            var stamp = (info.LastWriteTimeUtc.Ticks, info.Length);
            if (_stamp == stamp) return;
            _stamp = stamp;

            var doc = JsonSerializer.Deserialize(File.ReadAllText(_path), CoreJsonContext.Default.OAuthCredentialsDocument);
            var section = doc?.ClaudeAiOauth;
            if (string.IsNullOrWhiteSpace(section?.AccessToken))
            {
                _token = null;
                return;
            }

            DateTime? expires = section.ExpiresAt is { } ms
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime
                : null;
            _token = new OAuthToken(section.AccessToken, expires);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // plik w trakcie podmiany przez Claude Code - zostajemy przy poprzednim tokenie
        }
    }
}
