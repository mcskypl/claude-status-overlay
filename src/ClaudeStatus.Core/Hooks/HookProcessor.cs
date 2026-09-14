using System.Globalization;
using System.Text.RegularExpressions;
using ClaudeStatus.Core.Sessions;

namespace ClaudeStatus.Core.Hooks;

/// <summary>Co hook zrobił - do logu / wyjścia diagnostycznego.</summary>
public enum HookOutcome
{
    Written,
    Removed,
    IgnoredNotification,
}

/// <summary>
/// Zamienia zdarzenie hooka na plik stanu sesji. Nie zna stdin ani argumentów -
/// dostaje gotowy payload, więc da się go użyć także z innych źródeł (demo).
/// </summary>
public sealed partial class HookProcessor
{
    private const string FallbackSessionId = "default";
    private const string FallbackProject = "Claude";
    private const int MaxNoteLength = 120;

    private readonly SessionStatusStore _store;

    public HookProcessor(SessionStatusStore store)
    {
        _store = store;
    }

    public HookOutcome Apply(HookAction action, HookPayload? payload, string? noteOverride, DateTime now)
    {
        var sessionId = FirstNonEmpty(payload?.SessionId) ?? FallbackSessionId;

        if (action == HookAction.End)
        {
            _store.Delete(sessionId);
            return HookOutcome.Removed;
        }

        var notificationType = FirstNonEmpty(payload?.NotificationType);
        var message = FirstNonEmpty(payload?.Message, payload?.Title);

        if (action == HookAction.Attention && NotificationFilter.IsNoise(notificationType, message))
        {
            return HookOutcome.IgnoredNotification;
        }

        var note = FirstNonEmpty(noteOverride, message, notificationType) ?? "";
        if (note.Length > MaxNoteLength) note = note[..MaxNoteLength];

        _store.Write(new SessionStatusDocument
        {
            SessionId = sessionId,
            State = action.ToState()!.Value.ToKey(),
            Project = ProjectNameFrom(payload?.Cwd),
            Cwd = payload?.Cwd ?? "",
            Note = note,
            Transcript = payload?.TranscriptPath ?? "",
            Timestamp = now.ToString("o", CultureInfo.InvariantCulture),
        });
        return HookOutcome.Written;
    }

    /// <summary>Projekt = nazwa katalogu roboczego.</summary>
    internal static string ProjectNameFrom(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return FallbackProject;
        var trimmed = cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? FallbackProject : leaf;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return null;
    }
}

/// <summary>
/// Zdarzenie Notification odpala się też przy logowaniu czy aktualizacji - to
/// nie jest sytuacja "Claude czeka na Ciebie", więc takie pomijamy.
/// </summary>
public static partial class NotificationFilter
{
    public static bool IsNoise(string? notificationType, string? message)
    {
        if (!string.IsNullOrEmpty(notificationType)) return NoiseTypes().IsMatch(notificationType);
        return !string.IsNullOrEmpty(message) && NoiseMessages().IsMatch(message);
    }

    [GeneratedRegex("auth_success|auth_failure|auth_refresh|login|logout|update_available|installed", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseTypes();

    [GeneratedRegex("logged in|signed in|update available", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseMessages();
}
