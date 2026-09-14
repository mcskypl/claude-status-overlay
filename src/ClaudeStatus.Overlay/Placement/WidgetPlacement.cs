using ClaudeStatus.Overlay.Rendering;

namespace ClaudeStatus.Overlay.Placement;

/// <summary>
/// Przekłada kotwicę na pozycję widgetu i kształt rogów. Przyklejona pastylka
/// siedzi na samej krawędzi z płaskim bokiem od strony ekranu (jak notch)
/// i trzyma się jej także po rozwinięciu - na dole panel rośnie w górę,
/// po prawej w lewo.
/// </summary>
public static class WidgetPlacement
{
    /// <summary>W rogach pastylka nie siedzi w samym narożniku, tylko kawałek dalej.</summary>
    private const int CornerInset = 24;

    public static Edge EdgeOf(WidgetAnchor anchor, bool detached)
    {
        if (detached || anchor == WidgetAnchor.Free) return Edge.None;
        if (anchor.IsTop()) return Edge.Top;
        if (anchor.IsBottom()) return Edge.Bottom;
        return anchor switch
        {
            WidgetAnchor.MiddleLeft => Edge.Left,
            WidgetAnchor.MiddleRight => Edge.Right,
            _ => Edge.None,
        };
    }

    public static CornerRadii RadiiFor(Edge edge, float r) => edge switch
    {
        Edge.Top => new CornerRadii(0, 0, r, r),
        Edge.Bottom => new CornerRadii(r, r, 0, 0),
        Edge.Left => new CornerRadii(0, r, r, 0),
        Edge.Right => new CornerRadii(r, 0, 0, r),
        _ => CornerRadii.Uniform(r),
    };

    /// <summary>Lewy górny róg WIDGETU (nie okna) na ekranie.</summary>
    public static Point ContentOrigin(WidgetAnchor anchor, bool detached, Rectangle workArea, Size content,
        DpiScale scale, Point freePosition)
    {
        if (anchor == WidgetAnchor.Free) return freePosition;

        var gap = detached ? scale.PxInt(Design.GapDetached) : 0;
        var inset = scale.PxInt(CornerInset);

        int x;
        if (anchor.IsMiddle())
        {
            x = anchor == WidgetAnchor.MiddleRight ? workArea.Right - gap - content.Width : workArea.Left + gap;
        }
        else if (anchor.IsCenter())
        {
            x = workArea.Left + (workArea.Width - content.Width) / 2;
        }
        else if (anchor.IsRight())
        {
            x = workArea.Right - inset - gap - content.Width;
        }
        else
        {
            x = workArea.Left + inset + gap;
        }

        int y;
        if (anchor.IsMiddle()) y = workArea.Top + (workArea.Height - content.Height) / 2;
        else if (anchor.IsBottom()) y = workArea.Bottom - gap - content.Height;
        else y = workArea.Top + gap;

        return new Point(x, y);
    }
}
