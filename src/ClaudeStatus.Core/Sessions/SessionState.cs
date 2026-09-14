namespace ClaudeStatus.Core.Sessions;

/// <summary>Stan sesji Claude Code, tak jak widzi go nakładka.</summary>
public enum SessionState
{
    /// <summary>Sesja otwarta, nic się nie dzieje.</summary>
    Idle,
    /// <summary>Claude pracuje nad odpowiedzią.</summary>
    Working,
    /// <summary>Odpowiedź gotowa.</summary>
    Done,
    /// <summary>Claude czeka na Ciebie - zgoda na narzędzie albo pytanie.</summary>
    Attention,
    /// <summary>Tura przerwana błędem API / limitem.</summary>
    Error,
}

public static class SessionStateExtensions
{
    /// <summary>Klucz używany w plikach stanu i w argumentach hooka.</summary>
    public static string ToKey(this SessionState state) => state switch
    {
        SessionState.Idle => "idle",
        SessionState.Working => "working",
        SessionState.Done => "done",
        SessionState.Attention => "attention",
        SessionState.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static SessionState? Parse(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "idle" => SessionState.Idle,
        "working" => SessionState.Working,
        "done" => SessionState.Done,
        "attention" => SessionState.Attention,
        "error" => SessionState.Error,
        _ => null,
    };

    /// <summary>
    /// Kolejność ważności w pastylce: czeka na Ciebie -> pracuje -> gotowe -> bezczynny.
    /// Im mniejsza liczba, tym stan ważniejszy.
    /// </summary>
    public static int Priority(this SessionState state) => state switch
    {
        SessionState.Attention or SessionState.Error => 1,
        SessionState.Working => 2,
        SessionState.Done => 3,
        _ => 4,
    };

    public static bool NeedsAttention(this SessionState state) =>
        state is SessionState.Attention or SessionState.Error;

    /// <summary>Stany, które "Wyczyść zakończone sesje" może skasować.</summary>
    public static bool IsFinished(this SessionState state) =>
        state is SessionState.Done or SessionState.Idle;
}
