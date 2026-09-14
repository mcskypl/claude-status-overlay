using ClaudeStatus.Core.Account;
using ClaudeStatus.Core.Usage;

namespace ClaudeStatus.Core.Sessions;

/// <summary>
/// Niezmienny obraz świata z jednego odczytu: posortowane sesje, limity i konto.
/// Monitor produkuje kolejne obrazy w tle, UI bierze zawsze najnowszy.
/// </summary>
public sealed class SessionSnapshot
{
    public static readonly SessionSnapshot Empty = new([], UsageSnapshot.Empty, null, DateTime.MinValue);

    public SessionSnapshot(IReadOnlyList<SessionInfo> sessions, UsageSnapshot usage, AccountInfo? account, DateTime takenAt)
    {
        Sessions = sessions;
        Usage = usage;
        Account = account;
        TakenAt = takenAt;
    }

    /// <summary>Sesje w kolejności wyświetlania (<see cref="SessionInfo.DisplayOrder"/>).</summary>
    public IReadOnlyList<SessionInfo> Sessions { get; }

    public UsageSnapshot Usage { get; }

    /// <summary>Tożsamość zalogowanego konta (e-mail); null, gdy nieznana.</summary>
    public AccountInfo? Account { get; }

    public DateTime TakenAt { get; }

    /// <summary>Najważniejsza sesja - ta, którą pokazuje zwinięta pastylka.</summary>
    public SessionInfo? Top => Sessions.Count > 0 ? Sessions[0] : null;

    /// <summary>Ile sesji dzieli priorytet z najważniejszą (licznik "×N" na pastylce).</summary>
    public int CountAtTopPriority()
    {
        if (Top is null) return 0;
        var priority = Top.State.Priority();
        var n = 0;
        foreach (var s in Sessions)
        {
            if (s.State.Priority() == priority) n++;
        }
        return n;
    }
}
