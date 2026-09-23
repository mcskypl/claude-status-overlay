using System.Drawing.Drawing2D;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>Promienie rogów: TL, TR, BR, BL (jak w CSS).</summary>
public readonly record struct CornerRadii(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    public static CornerRadii Uniform(float r) => new(r, r, r, r);

    /// <summary>Rogi, które mają promień, powiększone o d - zero zostaje zerem (płaski bok).</summary>
    public CornerRadii Grow(float d) => new(
        TopLeft > 0 ? TopLeft + d : 0,
        TopRight > 0 ? TopRight + d : 0,
        BottomRight > 0 ? BottomRight + d : 0,
        BottomLeft > 0 ? BottomLeft + d : 0);

    /// <summary>Promienie przeskalowane - zero zostaje zerem (płaski bok).</summary>
    public CornerRadii Scale(float k) => new(TopLeft * k, TopRight * k, BottomRight * k, BottomLeft * k);

    /// <summary>Klucz do cache - po zaokrągleniu do całych pikseli.</summary>
    public (int, int, int, int) Key => ((int)TopLeft, (int)TopRight, (int)BottomRight, (int)BottomLeft);
}

public static class RoundedRect
{
    /// <summary>Zaokrąglony prostokąt z osobnym promieniem na każdy róg.</summary>
    public static GraphicsPath Create(float x, float y, float w, float h, CornerRadii r)
    {
        var path = new GraphicsPath();
        var max = Math.Min(w, h) / 2;
        var tl = Math.Clamp(r.TopLeft, 0, max);
        var tr = Math.Clamp(r.TopRight, 0, max);
        var br = Math.Clamp(r.BottomRight, 0, max);
        var bl = Math.Clamp(r.BottomLeft, 0, max);

        path.StartFigure();
        path.AddLine(x + tl, y, x + w - tr, y);
        if (tr > 0) path.AddArc(x + w - 2 * tr, y, 2 * tr, 2 * tr, 270, 90);
        path.AddLine(x + w, y + tr, x + w, y + h - br);
        if (br > 0) path.AddArc(x + w - 2 * br, y + h - 2 * br, 2 * br, 2 * br, 0, 90);
        path.AddLine(x + w - br, y + h, x + bl, y + h);
        if (bl > 0) path.AddArc(x, y + h - 2 * bl, 2 * bl, 2 * bl, 90, 90);
        path.AddLine(x, y + h - bl, x, y + tl);
        if (tl > 0) path.AddArc(x, y, 2 * tl, 2 * tl, 180, 90);
        path.CloseFigure();
        return path;
    }

    public static GraphicsPath Create(RectangleF rect, CornerRadii r) => Create(rect.X, rect.Y, rect.Width, rect.Height, r);
}
