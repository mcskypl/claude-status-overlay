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

    /// <summary>
    /// Alfa "czeka na Ciebie" i "błąd". Dawniej była to fala prostokątna - znacznik
    /// przeskakiwał między pełnym a przygaszonym, a razem z nim skokowo pojawiała
    /// się poświata. Na żywej tapecie wyglądało to jak mrugająca plama, nie jak
    /// sygnał. Teraz to ten sam oddech, co przy "pracuje", tylko głębszy i w rytmie
    /// stanu (1 s / 1,6 s) - wzrok łapie go kątem oka, a nie razi.
    /// </summary>
    public static float BlinkAlpha(StateStyle style, double timeMs)
    {
        if (style.BlinkCycleMs <= 0) return 1f;

        var phase = timeMs % style.BlinkCycleMs / style.BlinkCycleMs;
        var wave = (1 + Math.Cos(phase * 2 * Math.PI)) / 2;   // 1 -> 0 -> 1
        return (float)(Design.BlinkDimAlpha + (1 - Design.BlinkDimAlpha) * wave);
    }

    /// <summary>Alfa łuku/pierścienia dla pierścienia w pasku i znacznika na brzegu.</summary>
    public static float RingAlpha(StateStyle style, double timeMs) => style.Glyph switch
    {
        Glyph.Spin => PulseAlpha(timeMs),
        Glyph.Blink => BlinkAlpha(style, timeMs),
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
