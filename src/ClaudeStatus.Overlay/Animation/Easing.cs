namespace ClaudeStatus.Overlay.Animation;

public static class Easing
{
    /// <summary>
    /// cubic-bezier(.22, 1, .36, 1) - ta sama krzywa, co w prototypie. Beziera
    /// parametryzuje t, a my mamy x (postęp czasu), więc t szukamy Newtonem.
    /// </summary>
    public static float OutQuint(float progress)
    {
        if (progress <= 0) return 0f;
        if (progress >= 1) return 1f;

        const double x1 = 0.22, y1 = 1.0, x2 = 0.36, y2 = 1.0;
        double p = progress, t = p;
        for (var i = 0; i < 8; i++)
        {
            var u = 1 - t;
            var x = 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t;
            var dx = 3 * u * u * x1 + 6 * u * t * (x2 - x1) + 3 * t * t * (1 - x2);
            if (Math.Abs(dx) < 1e-6) break;
            t = Math.Clamp(t - (x - p) / dx, 0, 1);
        }
        var v = 1 - t;
        return (float)(3 * v * v * t * y1 + 3 * v * t * t * y2 + t * t * t);
    }

    /// <summary>0 przed a, 1 po b, gładko pomiędzy.</summary>
    public static float SmoothStep(float value, float a, float b)
    {
        if (b <= a) return value >= b ? 1f : 0f;
        var x = Math.Clamp((value - a) / (b - a), 0f, 1f);
        return x * x * (3 - 2 * x);
    }
}
