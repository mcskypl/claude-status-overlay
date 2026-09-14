namespace ClaudeStatus.Overlay.Rendering;

/// <summary>Przelicznik wartości z projektu (px przy 100 %) na piksele ekranu.</summary>
public readonly record struct DpiScale(float Factor)
{
    public static readonly DpiScale Identity = new(1f);

    public static DpiScale FromDpi(int dpi) => new(Math.Max(1f, dpi / 96f));

    public float Px(double value) => (float)(value * Factor);

    public int PxInt(double value) => (int)Math.Round(value * Factor);
}
