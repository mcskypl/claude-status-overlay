namespace ClaudeStatus.Overlay.Animation;

/// <summary>
/// Prosty fade 0..1 do zadanego celu (widoczna / niewidoczna), z własnym
/// czasem trwania. Trzy poziomy widgetu (brzeg / pasek / panel) przenikają
/// niezależnie od siebie - każdy ma swój fade, a nie wspólny współczynnik
/// mieszania jak w starej wersji z dwoma stanami.
/// </summary>
public sealed class FadeAnimation
{
    /// <summary>
    /// Najkrótszy czas zawrócenia, jako ułamek pełnego. Bez dolnej granicy ruch
    /// tuż po starcie kończyłby się skokiem, a nie animacją.
    /// </summary>
    private const double MinReturn = 0.35;

    private readonly double _durationMs;
    private double _currentMs;
    private float _from;
    private double _startMs = double.NegativeInfinity;

    public FadeAnimation(double durationMs, bool startVisible = false)
    {
        _durationMs = durationMs;
        _currentMs = durationMs;
        Visible = startVisible;
        Value = startVisible ? 1f : 0f;
    }

    public bool Visible { get; private set; }

    /// <summary>0 = niewidoczna, 1 = widoczna, po easingu.</summary>
    public float Value { get; private set; }

    public bool IsAnimating { get; private set; }

    /// <summary>
    /// Zmiana celu. Czas jest skracany proporcjonalnie do tego, ile drogi
    /// naprawdę zostało: zjechanie kursorem tuż po najechaniu ma wracać szybko,
    /// a nie mielić pełne 200 ms na przebycie dziesiątej części dystansu. To
    /// właśnie ta różnica czuła się jak guma.
    /// </summary>
    public void SetVisible(bool visible, double nowMs)
    {
        if (Visible == visible) return;
        Visible = visible;
        _from = Value;
        _startMs = nowMs;

        var distance = Math.Abs((visible ? 1f : 0f) - _from);
        _currentMs = _durationMs * Math.Max(MinReturn, distance);
    }

    public void Update(double nowMs)
    {
        var p = (float)Math.Clamp((nowMs - _startMs) / _currentMs, 0, 1);
        var target = Visible ? 1f : 0f;
        Value = _from + (target - _from) * Easing.OutQuint(p);
        IsAnimating = p < 1f;
    }
}
