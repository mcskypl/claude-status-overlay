namespace ClaudeStatus.Overlay.Placement;

/// <summary>Gdzie pastylka siedzi na ekranie. Nazwy są zapisywane w konfiguracji.</summary>
public enum WidgetAnchor
{
    /// <summary>Pozycja dowolna - z przeciągania.</summary>
    Free,
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

/// <summary>Krawędź ekranu, której widget "dotyka" - od tego zależą płaskie rogi.</summary>
public enum Edge
{
    None,
    Top,
    Bottom,
    Left,
    Right,
}

public static class AnchorExtensions
{
    /// <summary>Kotwice do menu, w kolejności wyświetlania (bez <see cref="WidgetAnchor.Free"/>).</summary>
    public static readonly IReadOnlyList<WidgetAnchor> EdgeAnchors =
    [
        WidgetAnchor.TopLeft, WidgetAnchor.TopCenter, WidgetAnchor.TopRight,
        WidgetAnchor.MiddleLeft, WidgetAnchor.MiddleRight,
        WidgetAnchor.BottomLeft, WidgetAnchor.BottomCenter, WidgetAnchor.BottomRight,
    ];

    public static string Label(this WidgetAnchor anchor) => anchor switch
    {
        WidgetAnchor.TopLeft => "Góra, po lewej",
        WidgetAnchor.TopCenter => "Góra, na środku",
        WidgetAnchor.TopRight => "Góra, po prawej",
        WidgetAnchor.MiddleLeft => "Lewa krawędź",
        WidgetAnchor.MiddleRight => "Prawa krawędź",
        WidgetAnchor.BottomLeft => "Dół, po lewej",
        WidgetAnchor.BottomCenter => "Dół, na środku",
        WidgetAnchor.BottomRight => "Dół, po prawej",
        _ => "Dowolna (przeciągnij pastylkę)",
    };

    /// <summary>Po tej kotwicy w menu jest separator (koniec rzędu góra / środek).</summary>
    public static bool EndsMenuRow(this WidgetAnchor anchor) => anchor is WidgetAnchor.TopRight or WidgetAnchor.MiddleRight;

    public static bool IsTop(this WidgetAnchor a) => a is WidgetAnchor.TopLeft or WidgetAnchor.TopCenter or WidgetAnchor.TopRight;
    public static bool IsBottom(this WidgetAnchor a) => a is WidgetAnchor.BottomLeft or WidgetAnchor.BottomCenter or WidgetAnchor.BottomRight;
    public static bool IsMiddle(this WidgetAnchor a) => a is WidgetAnchor.MiddleLeft or WidgetAnchor.MiddleRight;
    public static bool IsCenter(this WidgetAnchor a) => a is WidgetAnchor.TopCenter or WidgetAnchor.BottomCenter;
    public static bool IsRight(this WidgetAnchor a) => a is WidgetAnchor.TopRight or WidgetAnchor.MiddleRight or WidgetAnchor.BottomRight;
}
