using System.Text.Json;

namespace ClaudeStatus.Core.Account;

/// <summary>Tożsamość zalogowanego konta - tylko to, co ma sens pokazać na widgecie.</summary>
public sealed record AccountInfo(string? Email);

/// <summary>
/// Adres e-mail zalogowanego konta z ~/.claude.json -&gt; oauthAccount.emailAddress.
/// Ten sam plik, co limity (<see cref="Usage.UsageCacheReader"/>), ale czytany
/// osobno - to inna, rzadko zmieniająca się informacja (tożsamość konta, nie
/// liczby zużycia), więc nie ma sensu mieszać jej z logiką odświeżania limitów.
/// Plik zmienia się rzadko - czytamy go tylko po zmianie, najwyżej raz na 10 s.
/// </summary>
public sealed class AccountInfoReader
{
    private static readonly TimeSpan MinReadInterval = TimeSpan.FromSeconds(10);

    private readonly string _path;
    private DateTime _lastAttempt = DateTime.MinValue;
    private (long Ticks, long Length)? _stamp;
    private AccountInfo? _info;

    public AccountInfoReader(string path)
    {
        _path = path;
    }

    public AccountInfo? Read(DateTime now)
    {
        RefreshIfNeeded(now);
        return _info;
    }

    private void RefreshIfNeeded(DateTime now)
    {
        if (now - _lastAttempt < MinReadInterval) return;
        _lastAttempt = now;

        FileInfo file;
        try
        {
            file = new FileInfo(_path);
            if (!file.Exists) return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }

        var stamp = (file.LastWriteTimeUtc.Ticks, file.Length);
        if (_stamp == stamp && _info is not null) return;

        var parsed = TryParse();
        _stamp = stamp;
        // połowicznie zapisany plik = brak danych; zostajemy przy poprzednich
        if (parsed is not null) _info = parsed;
    }

    private AccountInfo? TryParse()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty("oauthAccount", out var account) || account.ValueKind != JsonValueKind.Object)
            {
                return new AccountInfo(null);
            }

            string? email = account.TryGetProperty("emailAddress", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : null;
            return new AccountInfo(email);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
