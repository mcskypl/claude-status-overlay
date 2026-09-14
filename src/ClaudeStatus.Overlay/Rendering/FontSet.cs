using System.Drawing.Text;

namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// Fonty w rozmiarach z projektu, dla jednej skali DPI. Projekt używa IBM Plex;
/// jeśli nie ma go w systemie, bierzemy najbliższe zamienniki z Windows.
/// Waga 500 istnieje w GDI jako osobna rodzina "... Medium", więc ją preferujemy
/// tam, gdzie projekt jej chce.
/// </summary>
public sealed class FontSet : IDisposable
{
    private static readonly string[] Sans = ["IBM Plex Sans", "Segoe UI Variable Text", "Segoe UI"];
    private static readonly string[] SansMedium = ["IBM Plex Sans Medium", .. Sans];
    private static readonly string[] Mono = ["IBM Plex Mono", "Cascadia Mono", "Consolas"];
    private static readonly string[] MonoMedium = ["IBM Plex Mono Medium", .. Mono];

    private static readonly HashSet<string> Installed = LoadInstalledFamilies();

    public FontSet(DpiScale scale)
    {
        Scale = scale;
        Sans13 = Create(SansMedium, 13, scale);
        Sans12 = Create(Sans, 12, scale);
        Sans10 = Create(Sans, 10, scale);
        Sans95 = Create(Sans, 9.5, scale);
        Mono12 = Create(Mono, 12, scale);
        Mono11 = Create(Mono, 11, scale);
        Mono10 = Create(Mono, 10, scale);
    }

    public DpiScale Scale { get; }

    /// <summary>Nazwa sesji w panelu (500).</summary>
    public Font Sans13 { get; }
    /// <summary>Etykieta stanu w wierszu.</summary>
    public Font Sans12 { get; }
    /// <summary>Nagłówek SESJE.</summary>
    public Font Sans10 { get; }
    /// <summary>Termin resetu limitu.</summary>
    public Font Sans95 { get; }
    /// <summary>Czas na pastylce, etykiety i procenty mierników na pasku (po najechaniu).</summary>
    public Font Mono12 { get; }
    /// <summary>Wiek, licznik ×N, procenty, etykiety mierników (panel).</summary>
    public Font Mono11 { get; }
    /// <summary>"N aktywne" w nagłówku.</summary>
    public Font Mono10 { get; }

    private static Font Create(string[] families, double px, DpiScale scale)
    {
        var name = families[^1];
        foreach (var f in families)
        {
            if (Installed.Contains(f))
            {
                name = f;
                break;
            }
        }
        return new Font(name, scale.Px(px), FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private static HashSet<string> LoadInstalledFamilies()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var collection = new InstalledFontCollection();
            foreach (var family in collection.Families) set.Add(family.Name);
        }
        catch (Exception)
        {
            // brak listy = zostają zamienniki z końca listy
        }
        return set;
    }

    public void Dispose()
    {
        Sans13.Dispose();
        Sans12.Dispose();
        Sans10.Dispose();
        Sans95.Dispose();
        Mono12.Dispose();
        Mono11.Dispose();
        Mono10.Dispose();
    }
}
