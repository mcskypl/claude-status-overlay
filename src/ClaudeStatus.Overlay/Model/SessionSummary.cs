using ClaudeStatus.Core.Sessions;
using ClaudeStatus.Overlay.Rendering;

namespace ClaudeStatus.Overlay.Model;

/// <summary>To, co pokazuje zwinięta pastylka: najważniejsza sesja, jej styl, czas i licznik.</summary>
public sealed record SessionSummary(SessionInfo? Top, StateStyle Style, string TimeText, int Count)
{
    public static readonly SessionSummary None = new(null, StateStyle.Idle, "", 0);

    public static SessionSummary From(SessionSnapshot snapshot, DateTime now)
    {
        var top = snapshot.Top;
        if (top is null) return None;

        var style = StateStyle.For(top.State);
        var time = top.State == SessionState.Idle ? "" : Formats.Elapsed(now - top.Timestamp);
        return new SessionSummary(top, style, time, snapshot.CountAtTopPriority());
    }
}
