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
    /// <summary>Najkrótsze zawrócenie jako ułamek pełnego czasu - inaczej ruch tuż po starcie byłby skokiem.</summary>
    private const double MinReturn = 0.35;

    private readonly double _durationMs;
    private double _currentMs;
    private Shape _from;
    private Shape _target;
    private double _startMs = double.NegativeInfinity;

    public ShapeMorph(double durationMs, Shape initial)
    {
        _durationMs = durationMs;
        _currentMs = durationMs;
        _from = initial;
        _target = initial;
        Current = initial;
    }

    public Shape Current { get; private set; }

    public bool IsAnimating { get; private set; }

    /// <summary>
    /// Zmiana celu. Czas jest skracany proporcjonalnie do tego, ile drogi zostało:
    /// zawrócenie po dziesiątej części dystansu ma trwać dziesiątą część czasu,
    /// a nie pełne 380 ms. Bez tego zjechanie kursorem tuż po najechaniu wyglądało,
    /// jakby pastylka wracała przez gumę.
    /// </summary>
    public void SetTarget(Shape target, double nowMs)
    {
        if (target.Equals(_target)) return;

        // Pełna noga to droga między dotychczasowym celem a nowym; zostało tyle,
        // ile dzieli od nowego celu bieżący kształt.
        var span = Distance(_target, target);
        var left = Distance(Current, target);
        _currentMs = _durationMs * (span > 0.5f ? Math.Clamp(left / span, MinReturn, 1) : 1);

        _from = Current;
        _target = target;
        _startMs = nowMs;
    }

    private static double Distance(Shape a, Shape b)
    {
        var dw = a.W - b.W;
        var dh = a.H - b.H;
        return Math.Sqrt(dw * dw + dh * dh);
    }

    public void Update(double nowMs)
    {
        var p = (float)Math.Clamp((nowMs - _startMs) / _currentMs, 0, 1);
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
