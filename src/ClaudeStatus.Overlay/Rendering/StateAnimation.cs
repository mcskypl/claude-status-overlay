namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// Animacja glifu stanu, wspólna dla pierścienia w pasku, znacznika na brzegu
/// i kropki w panelu - żeby "pracuje" pulsowało, a "czeka"/"błąd" migało
/// dokładnie tak samo, gdziekolwiek się pojawią.
/// </summary>
public static class StateAnimation
{
    /// <summary>Kąt startowy łuku "pracuje" (stopnie), obracający się w czasie.</summary>
    public static float SpinStartDeg(double timeMs) => (float)(timeMs * Design.SpinDegPerMs % 360);

    /// <summary>Alfa pulsowania "pracuje": 1 -&gt; .45 -&gt; 1 co 2,4 s (ease-in-out przez cosinus).</summary>
    public static float PulseAlpha(double timeMs)
    {
        var phase = timeMs % Design.PulseMs / Design.PulseMs;
        return (float)(0.725 + 0.275 * Math.Cos(phase * 2 * Math.PI));
    }

    /// <summary>Alfa łuku/pierścienia dla pierścienia w pasku i znacznika na brzegu (z pulsowaniem "pracuje").</summary>
    public static float RingAlpha(StateStyle style, double timeMs) => style.Glyph switch
    {
        Glyph.Spin => PulseAlpha(timeMs),
        Glyph.Blink => style.IsBlinkOnAt(timeMs) ? 1f : (float)Design.BlinkDimAlpha,
        _ => 1f,
    };

    /// <summary>Kąt zamiatania łuku (stopnie) - częściowy dla "pracuje", pełny dla reszty poza torem.</summary>
    public static float SweepFor(Glyph glyph) => glyph switch
    {
        Glyph.Track => 0f,
        Glyph.Spin => (float)Design.SpinSweepDeg,
        _ => 360f,
    };
}
