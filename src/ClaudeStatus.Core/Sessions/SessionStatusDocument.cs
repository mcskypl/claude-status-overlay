using System.Globalization;
using System.Text.Json.Serialization;

namespace ClaudeStatus.Core.Sessions;

/// <summary>
/// Zawartość pliku status/&lt;session&gt;.json - zapisywana przez hook, czytana
/// przez nakładkę. Format jest zgodny z wersją PowerShellową, więc stare pliki
/// i skrypt demo działają bez zmian.
/// </summary>
public sealed class SessionStatusDocument
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("project")]
    public string? Project { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>Ścieżka transkryptu sesji - podpowiedź dla detekcji "tura leci dalej".</summary>
    [JsonPropertyName("transcript")]
    public string? Transcript { get; set; }

    /// <summary>Czas zapisu w formacie ISO 8601 ("o").</summary>
    [JsonPropertyName("ts")]
    public string? Timestamp { get; set; }

    public DateTime? ParseTimestamp()
    {
        if (string.IsNullOrWhiteSpace(Timestamp)) return null;
        return DateTimeOffset.TryParse(Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto)
            ? dto.LocalDateTime
            : null;
    }
}
