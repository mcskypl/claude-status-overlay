using System.Text.Json.Serialization;
using ClaudeStatus.Core.Hooks;
using ClaudeStatus.Core.Liveness;
using ClaudeStatus.Core.Sessions;
using ClaudeStatus.Core.Usage;

namespace ClaudeStatus.Core.Json;

/// <summary>
/// Serializacja generowana w czasie kompilacji - bez refleksji na starcie,
/// co ma znaczenie dla hooka odpalanego przy każdej turze.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SessionStatusDocument))]
[JsonSerializable(typeof(HookPayload))]
[JsonSerializable(typeof(SessionProcessDocument))]
[JsonSerializable(typeof(OAuthCredentialsDocument))]
internal sealed partial class CoreJsonContext : JsonSerializerContext
{
}
