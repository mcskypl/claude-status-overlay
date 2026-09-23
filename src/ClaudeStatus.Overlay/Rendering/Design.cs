namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// Metryki z projektu (design_handoff_token_widget), w px przy 100 %.
/// Wszystko, co trafia na ekran, przechodzi przez <see cref="DpiScale"/>.
///
/// Widget ma dwa poziomy, każdy z własnym kształtem powłoki:
///   Rest - brzeg: cienka linia zawsze widoczna, tylko proporcje limitów
///   Slim - pasek: podgląd po najechaniu, pierścień + wartość + mierniki
///
/// Trzeci poziom - panel z listą sesji - został zwinięty do okna rozmowy:
/// limity siedzą w jego nagłówku, konto i odświeżanie w stopce.
/// </summary>
public static class Design
{
    // wspólne
    /// <summary>
    /// Krycie powłoki, gdy pod nią stoi szkło. Treść rysujemy jak zawsze na
    /// nieprzezroczystym tle - alfę nakłada dopiero ostatni przebieg po bitmapie.
    /// Dzięki temu tekst zachowuje hinting GDI, którego nie ma jak odtworzyć,
    /// rysując od razu na przezroczystym.
    ///
    /// Skalowanie jest jednostajne, więc kontrast litery względem tła pod nią
    /// zostaje równy <c>ink × GlassAlpha</c> bez względu na to, co jest pod
    /// spodem - stąd można zejść niżej, niż podpowiada intuicja.
    /// </summary>
    public const double GlassAlpha = 0.40;

    public const int ShadowMargin = 48;     // zapas na cień wokół widgetu (18 px offsetu + ogon rozmycia)
    public const int GapDetached = 18;      // odstęp od krawędzi po odklejeniu
    public const int MorphMs = 380;         // morfing kształtu (transition width/height/border-radius)

    // animacje
    public const int MeterMs = 150;         // stała czasowa dojazdu pasków limitów
    public const double BlinkDimAlpha = 0.25;
    public const int PulseMs = 2400;        // pulsowanie pierścienia "pracuje"
    public const double SpinDegPerMs = 0.12;
    public const double SpinSweepDeg = 234; // 0.65turn

    /// <summary>Poniżej tego procentu dostępnego limitu pasek i tekst świecą na czerwono.</summary>
    public const double UsageDangerFreeBelow = 15.0;

    // --- Rest (brzeg) --------------------------------------------------------
    public const int RestW = 148, RestH = 5;
    public const float RRest = 3;
    public const int RestMarkerLen = 12;      // wzdłuż dłuższej osi paska
    public const int RestMarkerGap = 2;       // odstęp paska od znacznika
    /// <summary>
    /// Długość jednego paska limitu na brzegu - od końca linii do znacznika stanu.
    /// Paski nie spotykają się na środku: granicą jest znacznik, więc zużycie
    /// widać jako dwa wypełnienia dobijające do pastylki z obu stron.
    /// </summary>
    public const int RestBarLen = (RestW - RestMarkerLen) / 2 - RestMarkerGap;
    public const double RestTrackAlpha = 0.13;
    public const int RestFadeMs = 160;        // transition opacity dla warstwy brzegu
    public const int RestGlowStep = 2, RestGlowRings = 3;   // poświata znacznika ("czeka"/"błąd")
    public const double RestGlowAlpha = 0.05;

    // --- Slim (pasek) ----------------------------------------------------------
    public const int SlimH = 33;

    /// <summary>
    /// Projekt ma tu 16,5 px, czyli kapsułkę. Szkło pod paskiem zaokrągla DWM
    /// swoim promieniem 8 px i nie da się go zmienić - przy 16,5 px jego rogi
    /// wychodziły spod kształtu jasnymi łukami. Wspólny promień to jedyna wartość,
    /// przy której materiał kończy się dokładnie tam, gdzie kształt.
    /// </summary>
    public const float RSlim = 8f;
    public const int SlimPadX = 28, SlimGap = 18;
    public const int SlimRing = 14, SlimRingHole = 7;
    public const int SlimDividerW = 1, SlimDividerH = 14;
    // SlimMeterBarW = RestBarLen: ta sama długość co pasek limitu na brzegu (5h/7d), żeby po
    // najechaniu pasek limitu sprawiał wrażenie, że się przenosi, a nie zamienia na inny.
    public const int SlimMeterGap = 7, SlimMeterBarW = RestBarLen, SlimMeterBarH = 4;
    public const int SlimVBarLen = 78;
    public const int SlimFadeMs = 200;        // transition opacity dla warstwy paska

    /// <summary>
    /// Najmniejsza szerokość paska. Została po panelu, który miał tę szerokość -
    /// pasek trzyma ją nadal, żeby nie kurczył się do samego tekstu i nie skakał
    /// przy każdej zmianie wartości.
    /// </summary>
    public const int PanelW = 392;

    public const int RefreshSpinMs = 8000;    // po tylu ms bez nowych danych ikona przestaje się kręcić

    // typografia
    public const float HeadTracking = 1.4f;   // letter-spacing .14em @ 10px
}

