namespace ClaudeStatus.Core.Usage;

/// <summary>Skąd nakładka bierze limity. Odczyt ma być natychmiastowy - bez czekania na dysk czy sieć.</summary>
public interface IUsageSource
{
    /// <summary>
    /// Odstęp między automatycznymi pobraniami. Skrócenie działa od razu,
    /// a <see cref="RefreshNow"/> omija go zupełnie.
    /// </summary>
    TimeSpan Interval { get; set; }

    UsageSnapshot Read(DateTime now);

    /// <summary>
    /// Pobierz przy najbliższym odczycie, nie czekając na zwykły odstęp
    /// - ręczne odświeżenie z panelu nakładki.
    /// </summary>
    void RefreshNow();
}
