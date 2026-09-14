using System.Globalization;
using System.Text.Json;

namespace ClaudeStatus.Core.Usage;

/// <summary>
/// Limity tak, jak przyszły ze źródła (API albo cache Claude Code), z czasem
/// pobrania. Interpretacja - czy dane są przykurzone, czy okno już się
/// wyzerowało - dzieje się dopiero w <see cref="ToSnapshot"/>, więc oba
/// źródła dzielą jedną logikę i jeden parser.
/// </summary>
public sealed record UsageData(DateTime FetchedAt, UsageWindow? FiveHour, UsageWindow? SevenDay)
{
    /// <summary>Powyżej tego dane są "przykurzone" (znak ? przy terminie resetu).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    /// <summary>Powyżej tego nie pokazujemy nic.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public UsageSnapshot ToSnapshot(DateTime now)
    {
        var age = now - FetchedAt;
        if (age > MaxAge) return UsageSnapshot.Empty;

        var five = Resolve(FiveHour, now);
        var seven = Resolve(SevenDay, now);
        if (five is null && seven is null) return UsageSnapshot.Empty;

        return new UsageSnapshot(true, age > StaleAfter, FetchedAt, five, seven);
    }

    /// <summary>Nowsze z dwóch (null, gdy oba puste).</summary>
    public static UsageData? Newest(UsageData? a, UsageData? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return b.FetchedAt > a.FetchedAt ? b : a;
    }

    /// <summary>
    /// Obiekt z oknami limitów - w API to korzeń odpowiedzi, w cache pole
    /// "utilization". Null, gdy nie ma ani jednego okna (np. plik w połowie zapisu).
    /// </summary>
    public static UsageData? FromWindows(JsonElement windows, DateTime fetchedAt)
    {
        if (windows.ValueKind != JsonValueKind.Object) return null;

        var five = ParseWindow(windows, "five_hour");
        var seven = ParseWindow(windows, "seven_day");
        if (five is null && seven is null) return null;

        return new UsageData(fetchedAt, five, seven);
    }

    /// <summary>Okno, którego termin zerowania już minął, liczy od nowa od zera.</summary>
    private static UsageWindow? Resolve(UsageWindow? window, DateTime now)
    {
        if (window is null) return null;
        return window.ResetsAt is { } resets && now > resets ? new UsageWindow(0, null) : window;
    }

    private static UsageWindow? ParseWindow(JsonElement windows, string key)
    {
        if (!windows.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object) return null;
        if (!window.TryGetProperty("utilization", out var used) || used.ValueKind != JsonValueKind.Number) return null;

        var pct = Math.Clamp(used.GetDouble(), 0.0, 100.0);

        DateTime? resets = null;
        if (window.TryGetProperty("resets_at", out var at) && at.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            resets = dto.LocalDateTime;
        }

        return new UsageWindow(pct, resets);
    }
}
