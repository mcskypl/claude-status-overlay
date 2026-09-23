using System.Runtime.InteropServices;

namespace ClaudeStatus.Overlay.Interop;

/// <summary>
/// Szkło okna: żywe rozmycie tego, co pod nim, zaokrąglone rogi i brak obwódki.
/// Jedno miejsce dla pastylki, dymka i rozmowy, żeby wszystkie trzy miały ten sam
/// materiał.
/// </summary>
/// <remarks>
/// Nie używamy <c>DWMWA_SYSTEMBACKDROP_TYPE</c>, choć to droga udokumentowana.
/// DWM rysuje ten materiał wyłącznie dla okna AKTYWNEGO, a okna nakładki są
/// <c>WS_EX_NOACTIVATE</c> i aktywne nie będą nigdy - bo nie wolno im zabierać
/// kursora z tego, przy czym akurat pracujesz. Nieaktywne okno dostaje płaski
/// kolor zastępczy zamiast rozmycia.
///
/// Zmierzone: to samo okno, ten sam kod, dwa procesy. W osobnym procesie, który
/// miał aktywne okno, akryl próbkował tło (54 nad czernią, 146 nad bielą,
/// 255,7,7 nad czerwienią). W procesie nakładki - płaskie 84,84,84 nad każdym
/// z tych trzech teł. Sonda wstawiona do procesu nakładki zachowywała się tak
/// samo, więc rzecz jest w procesie, nie w oknie.
///
/// <c>SetWindowCompositionAttribute</c> z polityką akcentu jest nieudokumentowane,
/// ale od aktywności nie zależy i po zmianie szkło czyta 13 nad czernią i 170 nad
/// bielą. Gdyby kiedyś przestało działać, zostaje wariant bez rozmycia: powłoka
/// rysuje się wtedy nieprzezroczyście i nic się nie psuje.
/// </remarks>
public static class WindowMaterial
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerDoNotRound = 1;
    private const int DwmCornerRound = 2;
    private const int DwmSystemBackdropType = 38;
    private const int DwmSbtNone = 1;
    private const int DwmBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;


    /// <summary>
    /// Tryb ciemny okna. Akryl ma wariant jasny i ciemny, a DWM wybiera po trybie
    /// OKNA, nie systemu - bez tego materiał wychodzi mleczną szarością.
    /// </summary>
    private const int DwmUseImmersiveDarkMode = 20;

    /// <summary>ACCENT_ENABLE_ACRYLICBLURBEHIND.</summary>
    private const int AccentEnableAcrylic = 4;

    /// <summary>WCA_ACCENT_POLICY.</summary>
    private const int WindowCompositionAccentPolicy = 19;

    /// <summary>Promień, który nakłada DWM - nie da się go zmienić, więc reszta kształtu się do niego dopasowuje.</summary>
    public const int Radius = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// Zakłada materiał na okno.
    /// </summary>
    /// <param name="hwnd">Uchwyt okna.</param>
    /// <param name="tint">Przyciemnienie nakładane na rozmycie, w formacie AABBGGRR.</param>
    /// <param name="extendFrame">
    /// Czy rozciągnąć ramkę na całą powierzchnię klienta. Potrzebne, gdy okno
    /// zamalowuje swoje tło na czarno (czerń w rozciągniętej ramce jest dla
    /// kompozytora przezroczysta). Okna z przezroczystą treścią - jak host
    /// WebView2 - tego nie potrzebują.
    /// </param>
    /// <param name="noShadow">
    /// Czy zdjąć cień, który DWM rysuje pod oknem. Dla szkła pastylki - tak:
    /// pod pięciopikselową kreską przy krawędzi ekranu robił plamę szerszą
    /// i ciemniejszą niż sama pastylka. Okna rozmowy i dymka cień zachowują,
    /// bo tam faktycznie unosi je nad pulpitem.
    /// </param>
    /// <returns>Czy udało się założyć ramkę; gdy nie - wywołujący rysuje nieprzezroczyście.</returns>
    public static bool Apply(IntPtr hwnd, uint tint, bool extendFrame, bool noShadow = false)
    {
        var frame = 0;
        if (extendFrame)
        {
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            frame = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }

        var dark = 1;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        // Zaokrąglenie DWM przychodzi w Windows 11 w pakiecie z cieniem okna i nie
        // da się ich rozdzielić: ani DWMWA_NCRENDERING_POLICY, ani region, ani
        // rezygnacja z rozciągniętej ramki cienia nie zdejmują (wszystkie trzy
        // sprawdzone pomiarem). Okna, które cienia nie chcą, biorą więc rogi
        // z własnego regionu - zobacz GlassWindow.
        var corner = noShadow ? DwmCornerDoNotRound : DwmCornerRound;
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref corner, sizeof(int));

        // Bez tego DWM obrysowuje zaokrąglone okno własnym jasnym hairlinem,
        // który nie ma nic wspólnego z kształtem rysowanym nad nim.
        var border = DwmColorNone;
        DwmSetWindowAttribute(hwnd, DwmBorderColor, ref border, sizeof(uint));

        var none = DwmSbtNone;
        DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref none, sizeof(int));

        ApplyAccent(hwnd, tint);
        return frame == 0;
    }

    private static void ApplyAccent(IntPtr hwnd, uint tint)
    {
        var accent = new AccentPolicy
        {
            AccentState = AccentEnableAcrylic,
            AccentFlags = 0,
            GradientColor = tint,
            AnimationId = 0,
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, buffer, fDeleteOld: false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAccentPolicy,
                Data = buffer,
                SizeOfData = size,
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
