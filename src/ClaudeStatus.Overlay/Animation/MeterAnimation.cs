using ClaudeStatus.Core.Usage;
using ClaudeStatus.Overlay.Rendering;

namespace ClaudeStatus.Overlay.Animation;

/// <summary>
/// Paski limitów dojeżdżają do wartości płynnie (wygładzanie wykładnicze,
/// stała czasowa <see cref="Design.MeterMs"/>). Pasek, który się pojawia,
/// startuje od razu z docelowej wartości - bez "wyjeżdżania" od zera.
/// </summary>
public sealed class MeterAnimation
{
    private const float SettledEpsilon = 0.05f;

    private float? _five;
    private float? _seven;

    public MeterValues Values => new(_five, _seven);

    public bool IsAnimating { get; private set; }

    public void Update(double dtMs, UsageSnapshot? usage)
    {
        var k = (float)Math.Exp(-dtMs / Design.MeterMs);
        var moving = false;
        _five = Step(_five, usage?.FiveHour, k, ref moving);
        _seven = Step(_seven, usage?.SevenDay, k, ref moving);
        IsAnimating = moving;
    }

    /// <summary>Ustaw od razu na wartości docelowe (start, przełączenie limitów w menu).</summary>
    public void Snap(UsageSnapshot? usage)
    {
        _five = (float?)usage?.FiveHour?.Used;
        _seven = (float?)usage?.SevenDay?.Used;
        IsAnimating = false;
    }

    private static float? Step(float? current, UsageWindow? target, float k, ref bool moving)
    {
        if (target is null) return null;
        var goal = (float)target.Used;
        if (current is not { } cur) return goal;

        var next = goal + (cur - goal) * k;
        if (Math.Abs(next - goal) < SettledEpsilon) return goal;
        moving = true;
        return next;
    }
}
