using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeStatus.Core.Json;

namespace ClaudeStatus.Core.Hooks;

/// <summary>
/// JSON, który Claude Code podaje hookowi na stdin. Interesuje nas tylko kilka
/// pól; reszta jest ignorowana.
/// </summary>
public sealed class HookPayload
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }

    [JsonPropertyName("hook_event_name")]
    public string? EventName { get; set; }

    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Parsuje JSON zdarzenia; null dla pustego albo uszkodzonego wejścia.</summary>
    public static HookPayload? TryParse(ReadOnlySpan<char> json)
    {
        if (json.IsWhiteSpace()) return null;
        try
        {
            return JsonSerializer.Deserialize(json, CoreJsonContext.Default.HookPayload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
