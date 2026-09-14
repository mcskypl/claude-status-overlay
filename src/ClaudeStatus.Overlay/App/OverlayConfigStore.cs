using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeStatus.Overlay.Placement;

namespace ClaudeStatus.Overlay.App;

/// <summary>Odczyt i zapis konfiguracji; błędny plik = ustawienia domyślne.</summary>
public sealed class OverlayConfigStore
{
    private readonly string _path;

    public OverlayConfigStore(string path)
    {
        _path = path;
    }

    public OverlayConfig Load()
    {
        try
        {
            if (!File.Exists(_path)) return new OverlayConfig();
            var text = File.ReadAllText(_path);
            return JsonSerializer.Deserialize(text, OverlayJsonContext.Default.OverlayConfig) ?? new OverlayConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new OverlayConfig();
        }
    }

    public void Save(OverlayConfig config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, OverlayJsonContext.Default.OverlayConfig));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // konfiguracja nie jest warta zatrzymania nakładki
        }
    }
}

/// <summary>
/// Kotwica jako tekst ("TopCenter"); nieznana wartość daje <see cref="WidgetAnchor.Free"/>
/// zamiast wywalać całą konfigurację.
/// </summary>
public sealed class LenientAnchorConverter : JsonConverter<WidgetAnchor>
{
    public override WidgetAnchor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) return WidgetAnchor.Free;
        return Enum.TryParse<WidgetAnchor>(reader.GetString(), ignoreCase: true, out var anchor) ? anchor : WidgetAnchor.Free;
    }

    public override void Write(Utf8JsonWriter writer, WidgetAnchor value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OverlayConfig))]
internal sealed partial class OverlayJsonContext : JsonSerializerContext
{
}
