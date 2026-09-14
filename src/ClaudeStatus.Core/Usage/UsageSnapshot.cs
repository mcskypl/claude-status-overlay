namespace ClaudeStatus.Core.Usage;

/// <summary>Jedno okno limitu (5 h albo 7 dni).</summary>
/// <param name="Used">Procent zużyty, 0-100.</param>
/// <param name="ResetsAt">Kiedy licznik się zeruje; null, gdy nieznane albo już minął.</param>
public sealed record UsageWindow(double Used, DateTime? ResetsAt);

/// <summary>Limity odczytane z cache Claude Code.</summary>
/// <param name="Ok">Czy mamy sensowne dane.</param>
/// <param name="Stale">Dane starsze niż godzina - Claude Code chwilę ich nie odświeżał.</param>
/// <param name="FetchedAt">Kiedy Claude Code je pobrał.</param>
public sealed record UsageSnapshot(bool Ok, bool Stale, DateTime? FetchedAt, UsageWindow? FiveHour, UsageWindow? SevenDay)
{
    public static readonly UsageSnapshot Empty = new(false, false, null, null, null);

    public int WindowCount => (FiveHour is null ? 0 : 1) + (SevenDay is null ? 0 : 1);

    public bool HasAnyWindow => WindowCount > 0;
}
