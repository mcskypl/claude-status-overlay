namespace ClaudeStatus.Overlay.Animation;

/// <summary>Kształt powłoki widgetu: szerokość, wysokość, promień rogu (px ekranu).</summary>
public readonly record struct Shape(float W, float H, float R);

/// <summary>
/// Morfing kształtu powłoki między dowolną liczbą stanów (brzeg / pasek / panel).
/// Zamiast mieszać z góry znane dwa kształty przez współczynnik 0..1 (jak w
/// poprzedniej wersji z tylko dwoma stanami), animuje wprost bieżące piksele do
/// nowego celu - dokładnie tak, jak CSS <c>transition: width, height,
/// border-radius</c>. Zmiana celu w trakcie animacji startuje z aktualnie
/// wyliczonej wartości, więc nie ma skoku.
/// </summary>
public sealed class ShapeMorph
{
    private readonly double _durationMs;
    private Shape _from;
    private Shape _target;
    private double _startMs = double.NegativeInfinity;

    public ShapeMorph(double durationMs, Shape initial)
    {
        _durationMs = durationMs;
        _from = initial;
        _target = initial;
        Current = initial;
    }

    public Shape Current { get; private set; }

    public bool IsAnimating { get; private set; }

    public void SetTarget(Shape target, double nowMs)
    {
        if (target.Equals(_target)) return;
        _from = Current;
        _target = target;
        _startMs = nowMs;
    }

    public void Update(double nowMs)
    {
        var p = (float)Math.Clamp((nowMs - _startMs) / _durationMs, 0, 1);
        var eased = Easing.OutQuint(p);
        Current = new Shape(
            _from.W + (_target.W - _from.W) * eased,
            _from.H + (_target.H - _from.H) * eased,
            _from.R + (_target.R - _from.R) * eased);
        IsAnimating = p < 1f;
    }

    /// <summary>
    /// Przelicza wszystkie trzymane kształty (bieżący, cel, punkt startu) przez
    /// ten sam współczynnik - do użycia przy zmianie DPI, żeby widget nie
    /// "doskoczył" animacją do nowego rozmiaru, tylko zmienił się od razu.
    /// </summary>
    public void Rescale(float factor)
    {
        Current = Scale(Current, factor);
        _target = Scale(_target, factor);
        _from = Scale(_from, factor);
    }

    private static Shape Scale(Shape shape, float factor) => new(shape.W * factor, shape.H * factor, shape.R * factor);
}
