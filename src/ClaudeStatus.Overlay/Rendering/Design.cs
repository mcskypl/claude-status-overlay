namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// Metryki z projektu (design_handoff_token_widget), w px przy 100 %.
/// Wszystko, co trafia na ekran, przechodzi przez <see cref="DpiScale"/>.
///
/// Widget ma trzy poziomy, każdy z własnym kształtem powłoki:
///   Rest  - brzeg: cienka linia zawsze widoczna, tylko proporcje limitów
///   Slim  - pasek: podgląd po najechaniu, pierścień + wartość + mierniki
///   Panel - pełna lista sesji po kliknięciu (układ bez zmian od poprzedniej wersji)
/// </summary>
public static class Design
{
    // wspólne
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
    public const int RestGlowStep = 3, RestGlowRings = 5;   // migająca poświata znacznika ("czeka"/"błąd")
    public const double RestGlowAlpha = 0.06;

    // --- Slim (pasek) ----------------------------------------------------------
    public const int SlimH = 33;
    public const float RSlim = 16.5f;         // = SlimH / 2, pełne zaokrąglenie
    public const int SlimPadX = 28, SlimGap = 18;
    public const int SlimRing = 14, SlimRingHole = 7;
    public const int SlimDividerW = 1, SlimDividerH = 14;
    // SlimMeterBarW = RestBarLen: ta sama długość co pasek limitu na brzegu (5h/7d), żeby po
    // najechaniu pasek limitu sprawiał wrażenie, że się przenosi, a nie zamienia na inny.
    public const int SlimMeterGap = 7, SlimMeterBarW = RestBarLen, SlimMeterBarH = 4;
    public const int SlimVBarLen = 78;
    public const int SlimFadeMs = 200;        // transition opacity dla warstwy paska

    // --- Panel (bez zmian) -------------------------------------------------
    public const int PanelW = 392;
    public const float RPanel = 28;
    public const int PadT = 16, PadX = 18, PadB = 18;
    public const int HeadH = 16, HeadGap = 12;
    public const int RowH = 30, RowGap = 2, RowR = 8, RowBleed = 6;
    public const int SepT = 16, SepB = 14;
    public const int MeterH = 30, MeterGap = 11, MeterLabelW = 34, MeterColGap = 12, MeterRightW = 82;
    public const int CaptionH = 14;           // wiersz z kontem i czasem ostatniego odświeżenia limitów
    public const int RefreshBtn = 16;         // przycisk ręcznego odświeżenia limitów (obszar klikalny)
    public const float RefreshIconR = 5f;     // promień kołowej strzałki
    public const float RefreshStroke = 1.4f;
    public const int RefreshGap = 8;          // odstęp między napisem "odświeżono..." a przyciskiem
    public const int RefreshSpinMs = 8000;    // po tylu ms bez nowych danych ikona przestaje się kręcić
    public const int UpdateH = 20;            // wiersz "nowa wersja ... - zaktualizuj" na dole panelu
    public const int UpdateDotR = 3;
    public const int UpdateBarH = 3;          // pasek postępu pobierania pod napisem
    public const int DotR = 4, DotGlowStep = 2, DotGlowRings = 4;
    public const int NameIndent = 24;         // 14 px kolumna kropki + 10 px gap
    public const int PanelFadeMs = 220, PanelFadeDelayMs = 60;   // opacity 220ms ease 60ms
    public const double PanelScaleFrom = 0.94;

    // typografia
    public const float HeadTracking = 1.4f;   // letter-spacing .14em @ 10px
}
