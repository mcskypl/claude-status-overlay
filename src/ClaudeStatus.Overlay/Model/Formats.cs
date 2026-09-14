using System.Globalization;
using ClaudeStatus.Core.Usage;

namespace ClaudeStatus.Overlay.Model;

/// <summary>Teksty widoczne na widgecie - po polsku, z odmianą.</summary>
public static class Formats
{
    private static readonly string[] DayShort = ["ndz", "pon", "wt", "śr", "czw", "pt", "sob"];

    /// <summary>"12s", "6m", "3h", "2d".</summary>
    public static string Elapsed(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
        return $"{(int)span.TotalDays}d";
    }

    /// <summary>"13:23", "jutro 09:00", "śr 11:23".</summary>
    public static string ResetTime(DateTime t, DateTime now)
    {
        var today = now.Date;
        var hm = t.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (t.Date == today) return hm;
        if (t.Date == today.AddDays(1)) return "jutro " + hm;
        return DayShort[(int)t.DayOfWeek] + " " + hm;
    }

    /// <summary>"73% zużyte" - ta sama konwencja co /usage i panel w VS Code.</summary>
    public static string UsageUsed(UsageWindow window) => $"{(int)Math.Round(window.Used)}% zużyte";

    /// <summary>"73%" - ta sama liczba co <see cref="UsageUsed"/>, bez podpisu (pasek).</summary>
    public static string UsageUsedShort(double usedPercent) => $"{(int)Math.Round(usedPercent)}%";

    public static string UsageReset(UsageWindow window, DateTime now, bool stale)
    {
        if (window.ResetsAt is not { } resets) return "";
        var text = "reset " + ResetTime(resets, now);
        return stale ? text + " ?" : text;
    }

    /// <summary>"1 aktywna", "2 aktywne", "5 aktywnych".</summary>
    public static string ActiveCount(int n)
    {
        if (n == 1) return "1 aktywna";
        var r100 = n % 100;
        var r10 = n % 10;
        if (r10 is >= 2 and <= 4 && r100 is not (>= 12 and <= 14)) return $"{n} aktywne";
        return $"{n} aktywnych";
    }

    public static string SessionBadge(int count) => count > 1 ? "×" + count : "";

    /// <summary>Jak daleko jesteśmy w oknie 5 h (0 = dopiero start, 1 = zaraz reset).</summary>
    public static float? FiveHourStage(UsageWindow window, DateTime now) => Stage(window, now, 5.0);

    /// <summary>To samo dla okna 7 dni - żeby i tu było widać, ile czasu zostało do resetu.</summary>
    public static float? SevenDayStage(UsageWindow window, DateTime now) => Stage(window, now, 7 * 24.0);

    /// <summary>
    /// Jak daleko jesteśmy w oknie o zadanej długości, licząc od jego startu
    /// - null, gdy nie znamy terminu resetu.
    /// </summary>
    private static float? Stage(UsageWindow window, DateTime now, double windowHours)
    {
        if (window.ResetsAt is not { } resets) return null;
        var remainingHours = (resets - now).TotalHours;
        return (float)Math.Clamp(1 - remainingHours / windowHours, 0, 1);
    }
}
