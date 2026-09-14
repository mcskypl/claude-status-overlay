namespace ClaudeStatus.Overlay.Rendering;

/// <summary>Pędzle i pióra trzymane w cache - rysujemy do 60 klatek/s.</summary>
public sealed class ResourceCache : IDisposable
{
    private readonly Dictionary<int, SolidBrush> _brushes = new();
    private readonly Dictionary<(int Argb, float Width), Pen> _pens = new();

    public SolidBrush Brush(Color color)
    {
        var key = color.ToArgb();
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidBrush(color);
            _brushes[key] = brush;
        }
        return brush;
    }

    public Pen Pen(Color color, float width)
    {
        var key = (color.ToArgb(), width);
        if (!_pens.TryGetValue(key, out var pen))
        {
            pen = new Pen(color, width);
            _pens[key] = pen;
        }
        return pen;
    }

    public void Dispose()
    {
        foreach (var b in _brushes.Values) b.Dispose();
        foreach (var p in _pens.Values) p.Dispose();
        _brushes.Clear();
        _pens.Clear();
    }
}
