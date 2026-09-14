using ClaudeStatus.Core.Sessions;

namespace ClaudeStatus.Core.Hooks;

/// <summary>Co hook ma zrobić z wpisem sesji: ustawić stan albo go skasować.</summary>
public enum HookAction
{
    Idle,
    Working,
    Done,
    Attention,
    Error,
    /// <summary>Sesja zakończona - wpis znika.</summary>
    End,
}

public static class HookActions
{
    /// <summary>Zdarzenie Claude Code -&gt; akcja hooka. Kolejność = kolejność w settings.json.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, HookAction>> ByEvent =
    [
        new("SessionStart", HookAction.Idle),
        new("UserPromptSubmit", HookAction.Working),
        new("Notification", HookAction.Attention),
        new("Stop", HookAction.Done),
        new("StopFailure", HookAction.Error),
        new("SessionEnd", HookAction.End),
    ];

    public static string ToKey(this HookAction action) => action switch
    {
        HookAction.End => "end",
        _ => action.ToState()!.Value.ToKey(),
    };

    public static HookAction? Parse(string? key)
    {
        if (string.Equals(key?.Trim(), "end", StringComparison.OrdinalIgnoreCase)) return HookAction.End;
        return SessionStateExtensions.Parse(key) switch
        {
            SessionState.Idle => HookAction.Idle,
            SessionState.Working => HookAction.Working,
            SessionState.Done => HookAction.Done,
            SessionState.Attention => HookAction.Attention,
            SessionState.Error => HookAction.Error,
            _ => null,
        };
    }

    public static SessionState? ToState(this HookAction action) => action switch
    {
        HookAction.Idle => SessionState.Idle,
        HookAction.Working => SessionState.Working,
        HookAction.Done => SessionState.Done,
        HookAction.Attention => SessionState.Attention,
        HookAction.Error => SessionState.Error,
        _ => null,
    };
}
