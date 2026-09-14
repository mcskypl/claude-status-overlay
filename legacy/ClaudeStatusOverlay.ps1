<#
    ClaudeStatusOverlay.ps1
    -----------------------
    Pastylka statusu w stylu notcha - czarna, przyklejona do krawędzi ekranu,
    zawsze na wierzchu. Zwinięta pokazuje aktywną sesję (pierścień + czas) i dwa
    mikro-paski limitów. Po najechaniu morfuje w panel z listą sesji i
    miernikami limitów 5 h / 7 dni.

    Stany sesji:
      bursztynowy pierścień (kręci się)  - Claude pracuje
      zielony pierścień                  - odpowiedź gotowa
      czerwony (miga)                    - czeka na Ciebie (zgoda / pytanie)
      fioletowy (miga)                   - tura przerwana błędem
      sam tor pierścienia                - brak aktywnych sesji

    Sterowanie:
      najedź myszką  - rozwija panel (można wyłączyć w menu; wtedy rozwija klik)
      lewy przycisk  - przeciąganie / klik w wiersz aktywuje okno edytora
      prawy przycisk - menu (pozycja, odklejenie od krawędzi, limity, dźwięki,
                       zawsze na wierzchu, wyczyść, zamknij)

    Warstwa wizualna to okno warstwowe (UpdateLayeredWindow) rysowane w całości
    ręcznie - stąd prawdziwy cień, wygładzone rogi i płynne przenikanie stanów.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# tylko jedna instancja nakładki
$script:Mutex = New-Object System.Threading.Mutex($false, 'Global\ClaudeStatusOverlay_v1')
if (-not $script:Mutex.WaitOne(0, $false)) {
    exit 0
}

# --------------------------------------------------------------------------- #
#  Okno warstwowe                                                              #
# --------------------------------------------------------------------------- #
# Zwykła forma WinForms nie umie cienia z rozmyciem ani wygładzonych rogów -
# region okna tnie piksele twardo. Dlatego cały widget renderujemy do bitmapy
# ARGB i wpychamy ją przez UpdateLayeredWindow: alfa per piksel załatwia rogi,
# cień i przenikanie pastylki z panelem.
if (-not ('ClaudeOverlayWin32' -as [type])) {
    Add-Type -ReferencedAssemblies @('System.Windows.Forms', 'System.Drawing') -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class ClaudeOverlayWin32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst,
        ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx; public int cy; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION {
        public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat;
    }

    public static void Push(IntPtr hwnd, Bitmap bmp, byte alpha) {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr hbm = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try {
            hbm = bmp.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(mem, hbm);
            SIZE size; size.cx = bmp.Width; size.cy = bmp.Height;
            POINT src; src.x = 0; src.y = 0;
            BLENDFUNCTION bf;
            bf.BlendOp = 0; bf.BlendFlags = 0; bf.SourceConstantAlpha = alpha; bf.AlphaFormat = 1;
            UpdateLayeredWindow(hwnd, screen, IntPtr.Zero, ref size, mem, ref src, 0, ref bf, 2);
        } finally {
            ReleaseDC(IntPtr.Zero, screen);
            if (hbm != IntPtr.Zero) { SelectObject(mem, old); DeleteObject(hbm); }
            DeleteDC(mem);
        }
    }
}

// Content to prostokat samego widgetu w bitmapie - wokol niego jest tylko cien,
// wiec klikniecia stamtad oddajemy oknu pod spodem.
public class ClaudeOverlayForm : Form {
    public Rectangle Content = Rectangle.Empty;
    protected override CreateParams CreateParams {
        get {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00080000 /* WS_EX_LAYERED */ | 0x00000080 /* WS_EX_TOOLWINDOW */;
            return cp;
        }
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void WndProc(ref Message m) {
        if (m.Msg == 0x0084 /* WM_NCHITTEST */ && !Content.IsEmpty) {
            int lp = (int)(long)m.LParam;
            Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            if (!Content.Contains(p)) { m.Result = (IntPtr)(-1) /* HTTRANSPARENT */; return; }
        }
        base.WndProc(ref m);
    }
}
'@
}

# rysujemy w pikselach fizycznych, więc prosimy Windows, żeby nas nie skalował
try { [void][ClaudeOverlayWin32]::SetProcessDPIAware() } catch { }

$script:Scale = 1.0
try {
    $gDpi = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
    $script:Scale = [double]$gDpi.DpiX / 96.0
    $gDpi.Dispose()
} catch { }
if ($script:Scale -lt 1.0) { $script:Scale = 1.0 }

# q = wartość z projektu (px przy 100 %) przeliczona na piksele ekranu
function q([double]$v)  { return [single]($v * $script:Scale) }
function qi([double]$v) { return [int][Math]::Round($v * $script:Scale) }

# --------------------------------------------------------------------------- #
#  Ścieżki i konfiguracja                                                      #
# --------------------------------------------------------------------------- #
$script:StatusDir = Join-Path $env:USERPROFILE '.claude\status'
$script:CfgPath   = Join-Path $env:USERPROFILE '.claude\status-overlay.config.json'

if (-not (Test-Path -LiteralPath $script:StatusDir)) {
    New-Item -ItemType Directory -Path $script:StatusDir -Force | Out-Null
}

# Anchor: 'Free' (X/Y z przeciągania) albo róg/krawędź ekranu, np. 'TopCenter'
# Detached: odstęp od krawędzi + zaokrąglenie wszystkich rogów
$script:Cfg = @{
    X = 60; Y = 60; Sound = $true; TopMost = $true
    Hover = $true; Anchor = 'TopCenter'; Usage = $true; Detached = $false
}
if (Test-Path -LiteralPath $script:CfgPath) {
    try {
        $loaded = Get-Content -LiteralPath $script:CfgPath -Raw | ConvertFrom-Json
        foreach ($k in @('X', 'Y', 'Sound', 'TopMost', 'Hover', 'Anchor', 'Usage', 'Detached')) {
            if ($null -ne $loaded.$k) { $script:Cfg[$k] = $loaded.$k }
        }
    } catch { }
}

function Save-OverlayConfig {
    try {
        ($script:Cfg | ConvertTo-Json -Depth 3) |
            Set-Content -LiteralPath $script:CfgPath -Encoding UTF8 -Force
    } catch { }
}

# --------------------------------------------------------------------------- #
#  Paleta                                                                      #
# --------------------------------------------------------------------------- #
# Akcenty z projektu (oklch przeliczone na sRGB):
#   niebieski oklch(0.72 0.13 250) | zielony  oklch(0.72 0.13 150)
#   bursztyn  oklch(0.78 0.13 75)  | czerwony oklch(0.72 0.13 20)
$CBlue   = [System.Drawing.Color]::FromArgb(96, 170, 243)
$CGreen  = [System.Drawing.Color]::FromArgb(98, 187, 120)
$CAmber  = [System.Drawing.Color]::FromArgb(232, 170, 78)
$CRed    = [System.Drawing.Color]::FromArgb(235, 129, 130)
$CViolet = [System.Drawing.Color]::FromArgb(184, 143, 230)
$CBlack  = [System.Drawing.Color]::FromArgb(0, 0, 0)

function WA([double]$a) {
    return [System.Drawing.Color]::FromArgb([int][Math]::Round(255 * $a), 255, 255, 255)
}
function CA([double]$a, $c) {
    return [System.Drawing.Color]::FromArgb([int][Math]::Round(255 * $a), $c.R, $c.G, $c.B)
}

# Glyph mówi, jak zachowuje się pierścień / kropka: spin - obrót łuku,
# full - pełny pierścień, blink - miganie, track - sam tor bez wypełnienia.
$script:Palette = @{
    'idle'      = @{ Color = (WA 0.40); Dot = (WA 0.16); Label = 'bezczynny';       Order = 4; Glyph = 'track' }
    'working'   = @{ Color = $CAmber;   Dot = $CAmber;   Label = 'pracuje';         Order = 2; Glyph = 'spin'  }
    'done'      = @{ Color = $CGreen;   Dot = $CGreen;   Label = 'gotowe';          Order = 3; Glyph = 'full'  }
    'attention' = @{ Color = $CRed;     Dot = $CRed;     Label = 'czeka na Ciebie'; Order = 1; Glyph = 'blink' }
    'error'     = @{ Color = $CViolet;  Dot = $CViolet;  Label = 'błąd';            Order = 1; Glyph = 'blink' }
}
function Get-StateInfo([string]$s) {
    if ($s -and $script:Palette.ContainsKey($s)) { return $script:Palette[$s] }
    return $script:Palette['idle']
}

# --------------------------------------------------------------------------- #
#  Czy zapisany stan jest jeszcze prawdziwy?                                   #
# --------------------------------------------------------------------------- #
# Claude Code nie ma zdarzenia "zgoda udzielona" - po kliknięciu Allow stan
# "czeka na Ciebie" wisiałby do końca tury. Wspólna biblioteka (ta sama dla
# serwera) szuka dowodów, że tura leci: rosnący transkrypt albo działający
# proces narzędzia odpalony po alercie.
$script:StateLib = Join-Path $PSScriptRoot 'ClaudeStatusState.ps1'
if (Test-Path -LiteralPath $script:StateLib) {
    . $script:StateLib
} else {
    function Resolve-LiveState([string]$state, [string]$session, [string]$hinted, [datetime]$ts) { return $state }
}

# --------------------------------------------------------------------------- #
#  Limity użycia (5 h / 7 dni)                                                 #
# --------------------------------------------------------------------------- #
# Dane bierzemy z ~/.claude.json (cachedUsageUtilization) - Claude Code sam je
# tam odświeża, więc nakładka nic nie wysyła i nie zna żadnych tokenów.
$script:UsageLib = Join-Path $PSScriptRoot 'ClaudeStatusUsage.ps1'
if (Test-Path -LiteralPath $script:UsageLib) {
    . $script:UsageLib
} else {
    function Get-ClaudeUsage {
        return [pscustomobject]@{ Ok = $false; Stale = $false; FetchedAt = $null; Five = $null; Seven = $null }
    }
}

# Widok limitów odświeżamy razem z danymi sesji (~0,5 s), a nie przy każdej
# klatce animacji - rysowanie ma tylko sięgnąć po gotową wartość.
$script:UsageView = $null

function Update-UsageView {
    if (-not [bool]$script:Cfg.Usage) { $script:UsageView = $null; return }
    $u = Get-ClaudeUsage
    if ($u -and $u.Ok) { $script:UsageView = $u } else { $script:UsageView = $null }
}
function Get-UsageView { return $script:UsageView }

# --------------------------------------------------------------------------- #
#  Odczyt plików stanu                                                         #
# --------------------------------------------------------------------------- #
function Get-ClaudeSessions {
    $rows = New-Object System.Collections.ArrayList
    $files = @()
    try {
        $files = @(Get-ChildItem -LiteralPath $script:StatusDir -Filter '*.json' -File -ErrorAction Stop)
    } catch { return @() }

    $now = Get-Date
    foreach ($f in $files) {
        $d = $null
        try { $d = Get-Content -LiteralPath $f.FullName -Raw -ErrorAction Stop | ConvertFrom-Json } catch { continue }
        if (-not $d) { continue }

        $ts = $f.LastWriteTime
        if ($d.ts) { try { $ts = [datetime]::Parse($d.ts) } catch { } }

        # wpisy starsze niż dobę traktujemy jako martwe
        if (($now - $ts).TotalHours -gt 24) {
            Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue
            continue
        }

        $st = Resolve-LiveState ([string]$d.state) ([string]$d.session_id) ([string]$d.transcript) $ts
        $info = Get-StateInfo $st

        [void]$rows.Add([pscustomobject]@{
            File    = $f.FullName
            Session = [string]$d.session_id
            State   = $st
            Project = [string]$d.project
            Cwd     = [string]$d.cwd
            Note    = [string]$d.note
            Ts      = $ts
            Order   = $info.Order
        })
    }
    return @($rows | Sort-Object Order, @{ Expression = 'Ts'; Descending = $true })
}

# --------------------------------------------------------------------------- #
#  Metryki projektu (px przy 100 %)                                            #
# --------------------------------------------------------------------------- #
$DS = @{
    PillW = 142; PillH = 32; PanelW = 392
    RPill = 16;  RPanel = 28
    Margin = 40                 # zapas na cień wokół widgetu
    Morph = 380                 # ms - morfing kształtu
    GapDetached = 18            # odstęp od krawędzi po odklejeniu
    # panel
    PadT = 16; PadX = 18; PadB = 18
    HeadH = 16; HeadGap = 12
    RowH = 30; RowGap = 2; RowR = 8; RowBleed = 6
    SepT = 16; SepB = 14
    MeterH = 30; MeterGap = 11; MeterLabelW = 34; MeterColGap = 12; MeterRightW = 82
    # pastylka
    PillPadX = 14; PillGap = 10; Ring = 16; RingHole = 9; BarW = 26; BarH = 3; BarGap = 3
}

# --------------------------------------------------------------------------- #
#  Fonty                                                                       #
# --------------------------------------------------------------------------- #
# Projekt używa IBM Plex; jeśli nie ma go w systemie, bierzemy najbliższe
# zamienniki wbudowane w Windows.
$script:Installed = @{}
try {
    foreach ($fam in (New-Object System.Drawing.Text.InstalledFontCollection).Families) { $script:Installed[$fam.Name] = $true }
} catch { }

function New-UIFont([string[]]$families, [double]$px, [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Regular) {
    $name = $families[$families.Count - 1]
    foreach ($f in $families) { if ($script:Installed.ContainsKey($f)) { $name = $f; break } }
    return New-Object System.Drawing.Font($name, (q $px), $style, [System.Drawing.GraphicsUnit]::Pixel)
}

$SansFam = @('IBM Plex Sans', 'Segoe UI Variable Text', 'Segoe UI')
$MonoFam = @('IBM Plex Mono', 'Cascadia Mono', 'Consolas')

$Fn = @{
    Sans13  = New-UIFont $SansFam 13
    Sans12  = New-UIFont $SansFam 12
    Sans10  = New-UIFont $SansFam 10
    Sans95  = New-UIFont $SansFam 9.5
    Mono13  = New-UIFont $MonoFam 13
    Mono11  = New-UIFont $MonoFam 11
    Mono10  = New-UIFont $MonoFam 10
}

$script:SfT = [System.Drawing.StringFormat]::GenericTypographic
$script:SfT.FormatFlags = $script:SfT.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces

$script:SfEll = New-Object System.Drawing.StringFormat ([System.Drawing.StringFormat]::GenericTypographic)
$script:SfEll.Trimming    = [System.Drawing.StringTrimming]::EllipsisCharacter
$script:SfEll.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap

# pędzle i pióra trzymamy w cache - rysujemy do 60 klatek/s
$script:BrushCache = @{}
function Br($color) {
    $k = $color.ToArgb()
    if (-not $script:BrushCache.ContainsKey($k)) {
        $script:BrushCache[$k] = New-Object System.Drawing.SolidBrush $color
    }
    return $script:BrushCache[$k]
}
$script:PenCache = @{}
function Pn($color, [single]$w) {
    $k = '{0}|{1}' -f $color.ToArgb(), $w
    if (-not $script:PenCache.ContainsKey($k)) {
        $script:PenCache[$k] = New-Object System.Drawing.Pen -ArgumentList $color, $w
    }
    return $script:PenCache[$k]
}

# --------------------------------------------------------------------------- #
#  Pomocnicze rysowanie                                                        #
# --------------------------------------------------------------------------- #
function MeasW($g, [string]$s, $font) {
    if (-not $s) { return [single]0 }
    return [single]$g.MeasureString($s, $font, [System.Drawing.PointF]::Empty, $script:SfT).Width
}

# tekst wyrównany do lewej / prawej, wyśrodkowany w pionie na $cy
function DrawT($g, [string]$s, $font, $brush, [single]$x, [single]$cy) {
    if (-not $s) { return }
    $h = $font.GetHeight($g)
    $g.DrawString($s, $font, $brush, $x, ($cy - $h / 2), $script:SfT)
}
function DrawTR($g, [string]$s, $font, $brush, [single]$right, [single]$cy) {
    if (-not $s) { return }
    $w = MeasW $g $s $font
    DrawT $g $s $font $brush ($right - $w) $cy
}
# tekst z ellipsis w ograniczonej szerokości
function DrawTClip($g, [string]$s, $font, $brush, [single]$x, [single]$cy, [single]$w) {
    if (-not $s -or $w -lt 8) { return }
    $h = $font.GetHeight($g)
    $rect = New-Object System.Drawing.RectangleF -ArgumentList $x, ($cy - $h / 2), $w, ($h + 2)
    $g.DrawString($s, $font, $brush, $rect, $script:SfEll)
}
# tekst z trackingiem (letter-spacing) - GDI+ nie ma tego natywnie
function MeasTracked($g, [string]$s, $font, [single]$track) {
    $w = [single]0
    foreach ($ch in $s.ToCharArray()) { $w += (MeasW $g ([string]$ch) $font) + $track }
    if ($s.Length -gt 0) { $w -= $track }
    return $w
}
function DrawTracked($g, [string]$s, $font, $brush, [single]$x, [single]$cy, [single]$track) {
    $h = $font.GetHeight($g)
    $y = $cy - $h / 2
    foreach ($ch in $s.ToCharArray()) {
        $cs = [string]$ch
        $g.DrawString($cs, $font, $brush, $x, $y, $script:SfT)
        $x += (MeasW $g $cs $font) + $track
    }
}

# zaokrąglony prostokąt z osobnym promieniem na każdy róg: TL, TR, BR, BL
function New-RoundPath([single]$x, [single]$y, [single]$w, [single]$h, [single[]]$r) {
    $p  = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mx = [Math]::Min($w, $h) / 2
    $tl = [Math]::Max(0, [Math]::Min($r[0], $mx)); $tr = [Math]::Max(0, [Math]::Min($r[1], $mx))
    $br = [Math]::Max(0, [Math]::Min($r[2], $mx)); $bl = [Math]::Max(0, [Math]::Min($r[3], $mx))
    $p.StartFigure()
    $p.AddLine(($x + $tl), $y, ($x + $w - $tr), $y)
    if ($tr -gt 0) { $p.AddArc(($x + $w - 2 * $tr), $y, (2 * $tr), (2 * $tr), 270, 90) }
    $p.AddLine(($x + $w), ($y + $tr), ($x + $w), ($y + $h - $br))
    if ($br -gt 0) { $p.AddArc(($x + $w - 2 * $br), ($y + $h - 2 * $br), (2 * $br), (2 * $br), 0, 90) }
    $p.AddLine(($x + $w - $br), ($y + $h), ($x + $bl), ($y + $h))
    if ($bl -gt 0) { $p.AddArc($x, ($y + $h - 2 * $bl), (2 * $bl), (2 * $bl), 90, 90) }
    $p.AddLine($x, ($y + $h - $bl), $x, ($y + $tl))
    if ($tl -gt 0) { $p.AddArc($x, $y, (2 * $tl), (2 * $tl), 180, 90) }
    $p.CloseFigure()
    return $p
}

function FillRound($g, $brush, [single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    if ($w -le 0 -or $h -le 0) { return }
    $p = New-RoundPath $x $y $w $h @($r, $r, $r, $r)
    $g.FillPath($brush, $p)
    $p.Dispose()
}

# cień z projektu: 0 18px 40px -14px rgba(0,0,0,.85). Rozmycie udajemy
# stosem coraz większych warstw o małej alfie - w środku sumują się do 0.85,
# na zewnątrz gasną przez ~20 px. Cień zależy tylko od kształtu, więc poza
# morfingiem bierzemy go z cache zamiast rysować 14 warstw co klatkę.
$script:ShadowBmp = $null
$script:ShadowKey = ''

function Draw-Shadow($g, [single]$x, [single]$y, [single]$w, [single]$h, [single[]]$r, [int]$bw, [int]$bh) {
    $key = '{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}' -f $bw, $bh, [int]$w, [int]$h, [int]$r[0], [int]$r[1], [int]$r[2], [int]$r[3]
    if ($key -ne $script:ShadowKey -or $null -eq $script:ShadowBmp) {
        $bmp = Get-LayerBitmap ([ref]$script:ShadowBmp) $bw $bh
        $gs = New-LayerGraphics $bmp
        $sx = $x + (q 14); $sy = $y + (q 14) + (q 18)
        $sw = $w - (q 28); $sh = $h - (q 28)
        if ($sw -gt 2 -and $sh -gt 2) {
            $N = 14
            $a = 1.0 - [Math]::Pow(0.15, 1.0 / $N)
            $brush = Br ([System.Drawing.Color]::FromArgb([int][Math]::Round(255 * $a), 0, 0, 0))
            for ($i = $N - 1; $i -ge 0; $i--) {
                $d  = (q 20) * ($i / [double]($N - 1))
                $rr = @(0, 0, 0, 0)
                for ($k = 0; $k -lt 4; $k++) { if ($r[$k] -gt 0) { $rr[$k] = $r[$k] + $d } }
                $p = New-RoundPath ($sx - $d) ($sy - $d) ($sw + 2 * $d) ($sh + 2 * $d) ([single[]]$rr)
                $gs.FillPath($brush, $p)
                $p.Dispose()
            }
        }
        $gs.Dispose()
        $script:ShadowKey = $key
    }
    $g.DrawImageUnscaled($script:ShadowBmp, 0, 0)
}

# pierścień: tor + łuk w kolorze stanu + czarny otwór. $sweep w stopniach,
# $start liczony od godziny 12 zgodnie z ruchem wskazówek.
function Draw-Ring($g, [single]$cx, [single]$cy, [single]$d, [single]$hole, $col, [single]$start, [single]$sweep, [double]$alpha) {
    $rx = $cx - $d / 2; $ry = $cy - $d / 2
    $g.FillEllipse((Br (WA (0.12 * $alpha))), $rx, $ry, $d, $d)
    if ($sweep -gt 0) {
        $c = CA $alpha $col
        if ($sweep -ge 360) { $g.FillEllipse((Br $c), $rx, $ry, $d, $d) }
        else { $g.FillPie((Br $c), $rx, $ry, $d, $d, ($start - 90), $sweep) }
    }
    $g.FillEllipse((Br $CBlack), ($cx - $hole / 2), ($cy - $hole / 2), $hole, $hole)
}

# pasek limitu; $free 0-100 to ile ZOSTAŁO
function Draw-Bar($g, [single]$x, [single]$y, [single]$w, [single]$h, [double]$free, $col, [double]$trackA, [bool]$dim) {
    if ($w -lt 4) { return }
    FillRound $g (Br (WA $trackA)) $x $y $w $h ($h / 2)
    $fw = [single]($w * ([Math]::Max(0.0, [Math]::Min(100.0, $free)) / 100.0))
    if ($fw -lt $h) { if ($free -le 0) { return } ; $fw = $h }
    $c = $col
    if ($dim) { $c = CA 0.5 $col }
    FillRound $g (Br $c) $x $y $fw $h ($h / 2)
}

# --------------------------------------------------------------------------- #
#  Formatowanie                                                                #
# --------------------------------------------------------------------------- #
function Format-Elapsed([datetime]$ts) {
    $sp = (Get-Date) - $ts
    if ($sp.TotalSeconds -lt 60)  { return ('{0}s' -f [int]$sp.TotalSeconds) }
    if ($sp.TotalMinutes -lt 60)  { return ('{0}m' -f [int]$sp.TotalMinutes) }
    if ($sp.TotalHours -lt 24)    { return ('{0}h' -f [int]$sp.TotalHours) }
    return ('{0}d' -f [int]$sp.TotalDays)
}

$script:DayShort = @('ndz', 'pon', 'wt', 'śr', 'czw', 'pt', 'sob')
function Format-ResetTime([datetime]$t) {
    $today = (Get-Date).Date
    if ($t.Date -eq $today)             { return $t.ToString('HH:mm') }
    if ($t.Date -eq $today.AddDays(1))  { return 'jutro ' + $t.ToString('HH:mm') }
    return $script:DayShort[[int]$t.DayOfWeek] + ' ' + $t.ToString('HH:mm')
}
function Format-UsageFree($win) { return ('{0}% wolne' -f [int][Math]::Round($win.Free)) }
function Format-UsageReset($win) {
    if (-not $win.Resets) { return '' }
    return ('reset ' + (Format-ResetTime $win.Resets))
}

function Format-Active([int]$n) {
    if ($n -eq 1) { return '1 aktywna' }
    $r100 = $n % 100; $r10 = $n % 10
    if ($r10 -ge 2 -and $r10 -le 4 -and -not ($r100 -ge 12 -and $r100 -le 14)) { return "$n aktywne" }
    return "$n aktywnych"
}

# --------------------------------------------------------------------------- #
#  Stan widgetu                                                                #
# --------------------------------------------------------------------------- #
$script:Rows       = @()
$script:Prev       = @{}
$script:Tick       = 0
$script:Clock      = [System.Diagnostics.Stopwatch]::StartNew()
$script:Open       = $false        # docelowy stan: rozwinięty?
$script:MorphFrom  = 0.0           # wartość morfingu w chwili przełączenia
$script:MorphStart = -100000.0     # ms zegara, kiedy ruszył morfing
$script:Morph      = 0.0           # 0 = pastylka, 1 = panel (po easingu)
$script:MorphLin   = 0.0           # to samo liniowo (do przenikania)
$script:HoverRow   = -1
$script:Dragging   = $false
$script:Moved      = $false
$script:DragOrigin = New-Object System.Drawing.Point(0, 0)
$script:FormOrigin = New-Object System.Drawing.Point(0, 0)
$script:PillWidth  = qi $DS.PillW
$script:PanelH     = qi 100
$script:MeterAnim  = @{ five = -1.0; seven = -1.0; t = 0.0 }

# cubic-bezier(.22, 1, .36, 1) - ta sama krzywa, co w prototypie
function Ease([double]$p) {
    if ($p -le 0) { return 0.0 }
    if ($p -ge 1) { return 1.0 }
    $x1 = 0.22; $y1 = 1.0; $x2 = 0.36; $y2 = 1.0
    $t = $p
    for ($i = 0; $i -lt 8; $i++) {
        $x = 3 * (1 - $t) * (1 - $t) * $t * $x1 + 3 * (1 - $t) * $t * $t * $x2 + $t * $t * $t
        $dx = 3 * (1 - $t) * (1 - $t) * $x1 + 6 * (1 - $t) * $t * ($x2 - $x1) + 3 * $t * $t * (1 - $x2)
        if ([Math]::Abs($dx) -lt 1e-6) { break }
        $t -= ($x - $p) / $dx
        if ($t -lt 0) { $t = 0 } elseif ($t -gt 1) { $t = 1 }
    }
    return 3 * (1 - $t) * (1 - $t) * $t * $y1 + 3 * (1 - $t) * $t * $t * $y2 + $t * $t * $t
}
function Smooth([double]$v, [double]$a, [double]$b) {
    if ($b -le $a) { return [double]($v -ge $b) }
    $x = ($v - $a) / ($b - $a)
    if ($x -le 0) { return 0.0 }
    if ($x -ge 1) { return 1.0 }
    return $x * $x * (3 - 2 * $x)
}

function Set-Open([bool]$v) {
    if ($script:Open -eq $v) { return }
    $script:Open       = $v
    $script:MorphFrom  = $script:Morph
    $script:MorphStart = $script:Clock.Elapsed.TotalMilliseconds
}

function Update-Morph {
    $now = $script:Clock.Elapsed.TotalMilliseconds
    $p = ($now - $script:MorphStart) / $DS.Morph
    if ($p -gt 1) { $p = 1 }
    if ($p -lt 0) { $p = 0 }
    $target = 0.0
    if ($script:Open) { $target = 1.0 }
    $script:Morph    = $script:MorphFrom + ($target - $script:MorphFrom) * (Ease $p)
    $script:MorphLin = $script:MorphFrom + ($target - $script:MorphFrom) * $p
    return ($p -lt 1)
}

function Get-Summary {
    if (-not $script:Rows -or $script:Rows.Count -eq 0) {
        return [pscustomobject]@{ State = ''; Info = $script:Palette['idle']; Text = ''; Count = 0; Row = $null }
    }
    $top   = $script:Rows[0]
    $info  = Get-StateInfo $top.State
    $count = @($script:Rows | Where-Object { $_.Order -eq $top.Order }).Count
    $text  = Format-Elapsed $top.Ts
    if ($top.State -eq 'idle') { $text = '' }
    return [pscustomobject]@{ State = $top.State; Info = $info; Text = $text; Count = $count; Row = $top }
}

# miganie stanów wymagających uwagi (~420 ms) - wspólny zegar dla całego UI
function Get-BlinkOn {
    return (([int]($script:Clock.Elapsed.TotalMilliseconds / 420)) % 2) -eq 0
}

# paski limitów dojeżdżają do wartości płynnie (transition 600 ms)
function Update-MeterAnim([double]$dtMs) {
    $u = Get-UsageView
    $k = [Math]::Exp(-$dtMs / 150.0)
    foreach ($pair in @(@('five', 'Five'), @('seven', 'Seven'))) {
        $key = $pair[0]; $prop = $pair[1]
        $target = -1.0
        if ($u -and $u.$prop) { $target = [double]$u.$prop.Free }
        $cur = [double]$script:MeterAnim[$key]
        if ($target -lt 0)   { $script:MeterAnim[$key] = -1.0; continue }
        if ($cur -lt 0)      { $script:MeterAnim[$key] = $target; continue }
        $script:MeterAnim[$key] = $target + ($cur - $target) * $k
    }
}

# --------------------------------------------------------------------------- #
#  Warstwa: pastylka                                                           #
# --------------------------------------------------------------------------- #
$script:PillBmp  = $null
$script:PanelBmp = $null

function Get-LayerBitmap([ref]$slot, [int]$w, [int]$h) {
    $b = $slot.Value
    if ($null -eq $b -or $b.Width -ne $w -or $b.Height -ne $h) {
        if ($b) { $b.Dispose() }
        $b = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $slot.Value = $b
    }
    return $b
}

function New-LayerGraphics($bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return $g
}

# układ pastylki liczymy raz na klatkę: szerokość zależy od tekstu
function Get-PillLayout($g) {
    $s  = Get-Summary
    $u  = Get-UsageView
    $bars = ($null -ne $u) -and (($null -ne $u.Five) -or ($null -ne $u.Seven))

    $x = (q $DS.PillPadX) + (q $DS.Ring) + (q $DS.PillGap)
    $timeX = $x
    $timeW = MeasW $g $s.Text $Fn.Mono13
    if ($s.Text) { $x += $timeW + (q $DS.PillGap) }
    $barsX = $x + (q 2)
    if ($bars) { $x = $barsX + (q $DS.BarW) }

    $badge = ''
    if ($s.Count -gt 1) { $badge = [string]([char]0x00D7) + $s.Count }
    $badgeW = MeasW $g $badge $Fn.Mono11
    if ($badge) { $x += (q $DS.PillGap) + $badgeW }

    $natural = $x + (q $DS.PillPadX)
    $min = q 96
    if ($bars) { $min = q $DS.PillW }
    $w = [int][Math]::Ceiling([Math]::Max($min, $natural))

    return [pscustomobject]@{
        Summary = $s; Usage = $u; Bars = $bars; W = $w
        TimeX = $timeX; BarsX = $barsX; Badge = $badge; BadgeW = $badgeW
    }
}

function Render-Pill($lay) {
    $w = $lay.W; $h = qi $DS.PillH
    $bmp = Get-LayerBitmap ([ref]$script:PillBmp) $w $h
    $g = New-LayerGraphics $bmp
    $cy = [single]($h / 2)
    $s = $lay.Summary
    $info = $s.Info
    $blinkOn = Get-BlinkOn

    # pierścień
    $rcx = (q $DS.PillPadX) + (q $DS.Ring) / 2
    $start = 0; $sweep = 0; $alpha = 1.0
    switch ($info.Glyph) {
        'spin'  {
            $sweep = 234
            $start = ($script:Clock.Elapsed.TotalMilliseconds * 0.12) % 360
            # pulseRing z prototypu: 1 -> .45 -> 1 co 2,4 s
            $ph = ($script:Clock.Elapsed.TotalMilliseconds % 2400) / 2400.0
            $alpha = 0.725 + 0.275 * [Math]::Cos($ph * 2 * [Math]::PI)
        }
        'full'  { $sweep = 360 }
        'blink' { $sweep = 360; if (-not $blinkOn) { $alpha = 0.3 } }
        default { $sweep = 0 }
    }
    Draw-Ring $g $rcx $cy (q $DS.Ring) (q $DS.RingHole) $info.Color $start $sweep $alpha

    # czas
    if ($s.Text) { DrawT $g $s.Text $Fn.Mono13 (Br (WA 1.0)) $lay.TimeX $cy }

    # mikro-paski limitów
    if ($lay.Bars) {
        $u = $lay.Usage
        $bh = q $DS.BarH; $bw = q $DS.BarW; $gap = q $DS.BarGap
        $top = $cy - ($bh * 2 + $gap) / 2
        $five = [double]$script:MeterAnim.five
        $seven = [double]$script:MeterAnim.seven
        if ($u.Five)  { Draw-Bar $g $lay.BarsX $top $bw $bh $five $CBlue 0.14 $u.Stale }
        else          { Draw-Bar $g $lay.BarsX $top $bw $bh 0 $CBlue 0.14 $false }
        if ($u.Seven) { Draw-Bar $g $lay.BarsX ($top + $bh + $gap) $bw $bh $seven $CGreen 0.14 $u.Stale }
        else          { Draw-Bar $g $lay.BarsX ($top + $bh + $gap) $bw $bh 0 $CGreen 0.14 $false }
    }

    # licznik sesji w tym samym stanie, w wolnej przestrzeni po prawej
    if ($lay.Badge) {
        DrawTR $g $lay.Badge $Fn.Mono11 (Br (CA 0.75 $info.Color)) ($w - (q $DS.PillPadX)) $cy
    }

    $g.Dispose()
    return $bmp
}

# --------------------------------------------------------------------------- #
#  Warstwa: panel                                                              #
# --------------------------------------------------------------------------- #
function Get-MeterCount {
    $u = Get-UsageView
    if (-not $u) { return 0 }
    $n = 0
    if ($u.Five)  { $n++ }
    if ($u.Seven) { $n++ }
    return $n
}

function Get-PanelHeight {
    $n = [Math]::Max(1, $script:Rows.Count)
    $k = Get-MeterCount
    $h = $DS.PadT + $DS.HeadH + $DS.HeadGap + ($n * $DS.RowH) + (($n - 1) * $DS.RowGap)
    if ($k -gt 0) { $h += $DS.SepT + 1 + $DS.SepB + ($k * $DS.MeterH) + (($k - 1) * $DS.MeterGap) }
    $h += $DS.PadB
    return (qi $h)
}

function Get-RowsTop { return (q ($DS.PadT + $DS.HeadH + $DS.HeadGap)) }

function Render-Panel {
    $w = qi $DS.PanelW; $h = $script:PanelH
    $bmp = Get-LayerBitmap ([ref]$script:PanelBmp) $w $h
    $g = New-LayerGraphics $bmp
    $blinkOn = Get-BlinkOn

    $x0 = q $DS.PadX
    $x1 = $w - (q $DS.PadX)

    # nagłówek: SESJE | N aktywne
    $hcy = (q $DS.PadT) + (q $DS.HeadH) / 2
    DrawTracked $g 'SESJE' $Fn.Sans10 (Br (WA 0.34)) $x0 $hcy (q 1.4)
    $right = 'brak sesji'
    if ($script:Rows.Count -gt 0) { $right = Format-Active $script:Rows.Count }
    DrawTR $g $right $Fn.Mono10 (Br (WA 0.34)) $x1 $hcy

    # wiersze sesji
    $y = Get-RowsTop
    $rowH = q $DS.RowH
    if ($script:Rows.Count -eq 0) {
        $cy = $y + $rowH / 2
        $g.FillEllipse((Br (WA 0.16)), ($x0 + (q 7) - (q 4)), ($cy - (q 4)), (q 8), (q 8))
        DrawT $g 'brak aktywnych sesji' $Fn.Sans13 (Br (WA 0.4)) ($x0 + (q 14) + (q 10)) $cy
        $y += $rowH
    } else {
        $idx = 0
        foreach ($r in $script:Rows) {
            $info = Get-StateInfo $r.State
            $cy = $y + $rowH / 2

            if ($idx -eq $script:HoverRow) {
                FillRound $g (Br (WA 0.05)) ($x0 - (q $DS.RowBleed)) $y (($x1 - $x0) + 2 * (q $DS.RowBleed)) $rowH (q $DS.RowR)
            }

            # kropka z poświatą
            $dot = $info.Dot
            $glow = ($info.Glyph -ne 'track')
            if ($info.Glyph -eq 'blink' -and -not $blinkOn) { $dot = CA 0.25 $info.Color; $glow = $false }
            $dcx = $x0 + (q 7)
            if ($glow) {
                for ($k = 4; $k -ge 1; $k--) {
                    $rr = (q 4) + (q 2) * $k
                    $g.FillEllipse((Br (CA 0.05 $info.Color)), ($dcx - $rr), ($cy - $rr), (2 * $rr), (2 * $rr))
                }
            }
            $g.FillEllipse((Br $dot), ($dcx - (q 4)), ($cy - (q 4)), (q 8), (q 8))

            # wiek | stan | nazwa (od prawej, nazwa dostaje resztę)
            $age = Format-Elapsed $r.Ts
            $ageW = [Math]::Max((q 26), (MeasW $g $age $Fn.Mono11))
            DrawTR $g $age $Fn.Mono11 (Br (WA 0.3)) $x1 $cy

            $stW = MeasW $g $info.Label $Fn.Sans12
            $stX = $x1 - $ageW - (q 10) - $stW
            DrawT $g $info.Label $Fn.Sans12 (Br $info.Color) $stX $cy

            $nameX = $x0 + (q 14) + (q 10)
            $nameW = $stX - (q 10) - $nameX
            $name = $r.Project
            if (-not $name) { $name = $r.Session }
            DrawTClip $g $name $Fn.Sans13 (Br (WA 0.9)) $nameX $cy $nameW

            $y += $rowH + (q $DS.RowGap)
            $idx++
        }
        $y -= q $DS.RowGap
    }

    # separator + mierniki
    $u = Get-UsageView
    if ((Get-MeterCount) -gt 0) {
        $y += q $DS.SepT
        $g.FillRectangle((Br (WA 0.08)), $x0, $y, ($x1 - $x0), (q 1))
        $y += (q 1) + (q $DS.SepB)

        $mh = q $DS.MeterH
        $trackX = $x0 + (q $DS.MeterLabelW) + (q $DS.MeterColGap)
        $rightX = $x1 - (q $DS.MeterRightW)
        $trackW = $rightX - (q $DS.MeterColGap) - $trackX

        $meters = @()
        if ($u.Five)  { $meters += ,@('5 h',   $u.Five,  $CBlue,  [double]$script:MeterAnim.five) }
        if ($u.Seven) { $meters += ,@('7 dni', $u.Seven, $CGreen, [double]$script:MeterAnim.seven) }

        $first = $true
        foreach ($m in $meters) {
            if (-not $first) { $y += q $DS.MeterGap }
            $first = $false
            $cy = $y + $mh / 2
            DrawT $g $m[0] $Fn.Mono11 (Br (WA 0.42)) $x0 $cy
            Draw-Bar $g $trackX ($cy - (q 2)) $trackW (q 4) $m[3] $m[2] 0.10 $u.Stale

            # prawa kolumna: procent nad terminem resetu
            $lineA = $Fn.Mono11.GetHeight($g)
            $lineB = $Fn.Sans95.GetHeight($g)
            $blockH = $lineA + (q 1) + $lineB
            $top = $cy - $blockH / 2
            DrawTR $g (Format-UsageFree $m[1]) $Fn.Mono11 (Br (WA 0.82)) $x1 ($top + $lineA / 2)
            $rtxt = Format-UsageReset $m[1]
            if ($u.Stale -and $rtxt) { $rtxt = $rtxt + ' ?' }
            DrawTracked $g $rtxt $Fn.Sans95 (Br (WA 0.3)) ($x1 - (MeasTracked $g $rtxt $Fn.Sans95 (q 0.19))) ($top + $lineA + (q 1) + $lineB / 2) (q 0.19)
            $y += $mh
        }
    }

    $g.Dispose()
    return $bmp
}

# --------------------------------------------------------------------------- #
#  Kotwica: krawędź ekranu albo pozycja dowolna                                #
# --------------------------------------------------------------------------- #
$script:Anchors = [ordered]@{
    'TopLeft'      = 'Góra, po lewej'
    'TopCenter'    = 'Góra, na środku'
    'TopRight'     = 'Góra, po prawej'
    'MiddleLeft'   = 'Lewa krawędź'
    'MiddleRight'  = 'Prawa krawędź'
    'BottomLeft'   = 'Dół, po lewej'
    'BottomCenter' = 'Dół, na środku'
    'BottomRight'  = 'Dół, po prawej'
}
$CornerInset = 24   # w rogach pastylka nie siedzi w samym narożniku, tylko kawałek dalej

function Get-Anchor {
    $a = [string]$script:Cfg.Anchor
    if ($a -and $script:Anchors.Contains($a)) { return $a }
    return 'Free'
}

# którą krawędź widget "dotyka" - od tego zależą płaskie rogi
function Get-Edge {
    $a = Get-Anchor
    if ($a -eq 'Free' -or [bool]$script:Cfg.Detached) { return 'none' }
    if ($a -like 'Top*')    { return 'top' }
    if ($a -like 'Bottom*') { return 'bottom' }
    if ($a -eq 'MiddleLeft')  { return 'left' }
    if ($a -eq 'MiddleRight') { return 'right' }
    return 'none'
}

function Get-Radii([single]$r) {
    switch (Get-Edge) {
        'top'    { return [single[]]@(0, 0, $r, $r) }
        'bottom' { return [single[]]@($r, $r, 0, 0) }
        'left'   { return [single[]]@(0, $r, $r, 0) }
        'right'  { return [single[]]@($r, 0, 0, $r) }
        default  { return [single[]]@($r, $r, $r, $r) }
    }
}

$ShadowM = qi $DS.Margin   # margines na cień wokół widgetu (w bitmapie i oknie)

# pozycja lewego górnego rogu WIDGETU (nie okna) na ekranie
function Get-ContentOrigin([int]$w, [int]$h) {
    $a = Get-Anchor
    if ($a -eq 'Free') { return (New-Object System.Drawing.Point ([int]$script:Cfg.X), ([int]$script:Cfg.Y)) }

    $probe = New-Object System.Drawing.Point (($form.Left + $ShadowM), ($form.Top + $ShadowM))
    $wa = [System.Windows.Forms.Screen]::FromPoint($probe).WorkingArea
    $gap = 0
    if ([bool]$script:Cfg.Detached) { $gap = qi $DS.GapDetached }
    $ci = qi $CornerInset

    $x = $wa.Left + $gap
    if ($a -like 'Top*' -or $a -like 'Bottom*') {
        $x = $wa.Left + $ci + $gap
        if ($a -like '*Center') { $x = $wa.Left + [int](($wa.Width - $w) / 2) }
        if ($a -like '*Right')  { $x = $wa.Right - $ci - $gap - $w }
    }
    if ($a -eq 'MiddleRight') { $x = $wa.Right - $gap - $w }

    $y = $wa.Top + $gap
    if ($a -like 'Middle*') { $y = $wa.Top + [int](($wa.Height - $h) / 2) }
    if ($a -like 'Bottom*') { $y = $wa.Bottom - $gap - $h }

    return (New-Object System.Drawing.Point $x, $y)
}

# --------------------------------------------------------------------------- #
#  Formularz                                                                   #
# --------------------------------------------------------------------------- #
$form = New-Object ClaudeOverlayForm
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
$form.StartPosition   = [System.Windows.Forms.FormStartPosition]::Manual
$form.ShowInTaskbar   = $false
$form.TopMost         = [bool]$script:Cfg.TopMost
$form.Text            = 'Claude Status'
$form.Location        = New-Object System.Drawing.Point (([int]$script:Cfg.X - $ShadowM), ([int]$script:Cfg.Y - $ShadowM))
$form.Size            = New-Object System.Drawing.Size (($script:PillWidth + 2 * $ShadowM), ((qi $DS.PillH) + 2 * $ShadowM))

$script:Surface = $null

# --------------------------------------------------------------------------- #
#  Klatka: układ + kompozycja + wypchnięcie do okna                            #
# --------------------------------------------------------------------------- #
function Render-Frame {
    # 1. warstwy w naturalnych rozmiarach
    $gm = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
    $lay = Get-PillLayout $gm
    $gm.Dispose()
    $script:PillWidth = $lay.W
    $script:PanelH    = Get-PanelHeight

    $pillBmp  = Render-Pill $lay
    $panelBmp = $null
    if ($script:MorphLin -gt 0.001) { $panelBmp = Render-Panel }

    # 2. kształt powłoki
    $m  = $script:Morph
    $pw = [double]$script:PillWidth; $ph = [double](qi $DS.PillH)
    $xw = [double](qi $DS.PanelW);    $xh = [double]$script:PanelH
    $w  = [single]($pw + ($xw - $pw) * $m)
    $h  = [single]($ph + ($xh - $ph) * $m)
    $r  = [single]((q $DS.RPill) + ((q $DS.RPanel) - (q $DS.RPill)) * $m)
    $radii = Get-Radii $r

    $cw = [int][Math]::Ceiling($w); $ch = [int][Math]::Ceiling($h)
    $bw = $cw + 2 * $ShadowM; $bh = $ch + 2 * $ShadowM

    # 3. okno: pozycja z kotwicy, rozmiar z bitmapy
    $origin = Get-ContentOrigin $cw $ch
    if (-not $script:Dragging) {
        $form.Location = New-Object System.Drawing.Point (($origin.X - $ShadowM), ($origin.Y - $ShadowM))
    }
    if ($form.Width -ne $bw -or $form.Height -ne $bh) {
        $form.Size = New-Object System.Drawing.Size $bw, $bh
    }
    $form.Content = New-Object System.Drawing.Rectangle $ShadowM, $ShadowM, $cw, $ch

    # 4. kompozycja
    $surf = Get-LayerBitmap ([ref]$script:Surface) $bw $bh
    $g = New-LayerGraphics $surf
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    Draw-Shadow $g $ShadowM $ShadowM $w $h $radii $bw $bh

    $shell = New-RoundPath $ShadowM $ShadowM $w $h $radii
    $g.SetClip($shell)
    $g.FillPath((Br $CBlack), $shell)

    # przenikanie: pastylka gaśnie przez pierwsze ~200 ms, panel wchodzi
    # z opóźnieniem 60 ms przez 220 ms; skala 0.9 / 0.94 -> 1
    $lin = $script:MorphLin
    $pillA  = 1.0 - (Smooth $lin 0.0 0.53)
    $panelA = Smooth $lin 0.16 0.74
    $pillS  = 1.0 - 0.10 * $lin
    $panelS = 0.94 + 0.06 * $lin

    if ($pillA -gt 0.005) {
        $dw = $pillBmp.Width * $pillS; $dh = $pillBmp.Height * $pillS
        $dx = $ShadowM + ($w - $dw) / 2; $dy = $ShadowM + ($h - $dh) / 2
        Draw-Layer $g $pillBmp $dx $dy $dw $dh $pillA
    }
    if ($panelBmp -and $panelA -gt 0.005) {
        $dw = $panelBmp.Width * $panelS; $dh = $panelBmp.Height * $panelS
        $dx = $ShadowM + ($w - $dw) / 2; $dy = $ShadowM
        Draw-Layer $g $panelBmp $dx $dy $dw $dh $panelA
    }

    $g.ResetClip()
    # wewnętrzny hairline 0.5 px rgba(255,255,255,.08)
    $hl = New-RoundPath ($ShadowM + 0.5) ($ShadowM + 0.5) ($w - 1) ($h - 1) $radii
    $g.DrawPath((Pn (WA 0.08) 1), $hl)
    $hl.Dispose()
    $shell.Dispose()
    $g.Dispose()

    [ClaudeOverlayWin32]::Push($form.Handle, $surf, 255)
}

$script:ImgAttr = New-Object System.Drawing.Imaging.ImageAttributes
function Draw-Layer($g, $bmp, [single]$x, [single]$y, [single]$w, [single]$h, [double]$alpha) {
    $cm = New-Object System.Drawing.Imaging.ColorMatrix
    $cm.Matrix33 = [single]$alpha
    $script:ImgAttr.SetColorMatrix($cm, [System.Drawing.Imaging.ColorMatrixFlag]::Default, [System.Drawing.Imaging.ColorAdjustType]::Bitmap)
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $x, $y),
        (New-Object System.Drawing.PointF ($x + $w), $y),
        (New-Object System.Drawing.PointF $x, ($y + $h))
    )
    $src = New-Object System.Drawing.RectangleF 0, 0, $bmp.Width, $bmp.Height
    $g.DrawImage($bmp, $pts, $src, [System.Drawing.GraphicsUnit]::Pixel, $script:ImgAttr)
}

# --------------------------------------------------------------------------- #
#  Dane                                                                        #
# --------------------------------------------------------------------------- #
function Update-Data {
    $script:Rows = @(Get-ClaudeSessions)
    Update-UsageView

    # dźwięki na zmianę stanu
    if ($script:Cfg.Sound) {
        foreach ($r in $script:Rows) {
            $old = $null
            if ($script:Prev.ContainsKey($r.Session)) { $old = $script:Prev[$r.Session] }
            if ($old -ne $r.State) {
                if ($r.State -eq 'attention' -or $r.State -eq 'error') {
                    try { [System.Media.SystemSounds]::Exclamation.Play() } catch { }
                } elseif ($r.State -eq 'done' -and $old -eq 'working') {
                    try { [System.Media.SystemSounds]::Asterisk.Play() } catch { }
                }
            }
        }
    }

    $script:Prev = @{}
    foreach ($r in $script:Rows) { $script:Prev[$r.Session] = $r.State }
}

# pozycja kursora w układzie widgetu (0,0 = lewy górny róg pastylki/panelu)
function Get-CursorInContent {
    $p = [System.Windows.Forms.Cursor]::Position
    return (New-Object System.Drawing.Point (($p.X - $form.Left - $ShadowM), ($p.Y - $form.Top - $ShadowM)))
}
function Test-CursorInside {
    $c = Get-CursorInContent
    return ($c.X -ge 0 -and $c.Y -ge 0 -and $c.X -lt $form.Content.Width -and $c.Y -lt $form.Content.Height)
}
function Get-RowAt([System.Drawing.Point]$c) {
    if (-not $script:Open -or $script:Morph -lt 0.99) { return -1 }
    $top = Get-RowsTop
    $pitch = (q $DS.RowH) + (q $DS.RowGap)
    $idx = [Math]::Floor(($c.Y - $top) / $pitch)
    if ($idx -lt 0 -or $idx -ge $script:Rows.Count) { return -1 }
    if (($c.Y - $top) - $idx * $pitch -gt (q $DS.RowH)) { return -1 }
    return [int]$idx
}

# --------------------------------------------------------------------------- #
#  Pętla klatek                                                                #
# --------------------------------------------------------------------------- #
$script:LastFrame = 0.0
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 60
# błędy z pętli nie mogą po cichu zabić animacji - lądują w logu obok configu
$script:LogPath = Join-Path $env:USERPROFILE '.claude\status-overlay.log'
function Write-OverlayLog([string]$msg) {
    try { Add-Content -LiteralPath $script:LogPath -Value ((Get-Date).ToString('s') + ' ' + $msg) -Encoding UTF8 } catch { }
}

$timer.Add_Tick({
  try {
    $script:Tick++
    $now = $script:Clock.Elapsed.TotalMilliseconds
    $dt  = $now - $script:LastFrame
    $script:LastFrame = $now

    # dane z dysku co ~480 ms
    if (($now - $script:LastData) -ge 480) { $script:LastData = $now; Update-Data }

    # rozwijanie po kursorze (bez migotania na Enter/Leave)
    if ([bool]$script:Cfg.Hover -and -not $script:Dragging) {
        Set-Open (Test-CursorInside)
    }

    $animating = Update-Morph
    Update-MeterAnim $dt
    $script:HoverRow = Get-RowAt (Get-CursorInContent)

    Render-Frame

    # szybsze klatki tylko gdy coś się rusza
    $want = 60
    $s = Get-Summary
    if ($s.Info.Glyph -eq 'spin') { $want = 33 }
    if ($animating) { $want = 16 }
    if ($timer.Interval -ne $want) { $timer.Interval = $want }
  } catch {
    if (($script:Tick % 50) -eq 1) { Write-OverlayLog ("tick: " + $_.Exception.Message + ' @ ' + $_.InvocationInfo.PositionMessage) }
  }
})
$script:LastData = -1000.0

# --------------------------------------------------------------------------- #
#  Mysz                                                                        #
# --------------------------------------------------------------------------- #
function Invoke-FocusEditor($row) {
    if (-not $row -or -not $row.Cwd) { return }
    $leaf = ''
    try { $leaf = Split-Path -Path $row.Cwd -Leaf } catch { }
    if (-not $leaf) { return }
    $p = Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -like "*$leaf*" } |
        Select-Object -First 1
    if ($p) {
        try {
            if ([ClaudeOverlayWin32]::IsIconic($p.MainWindowHandle)) {
                [void][ClaudeOverlayWin32]::ShowWindow($p.MainWindowHandle, 9)
            }
            [void][ClaudeOverlayWin32]::SetForegroundWindow($p.MainWindowHandle)
        } catch { }
    }
}

$form.Add_MouseDown({
    param($sender, $e)
    if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
        $script:Dragging   = $true
        $script:Moved      = $false
        $script:DragOrigin = [System.Windows.Forms.Cursor]::Position
        $script:FormOrigin = $form.Location
    }
})

$form.Add_MouseMove({
    param($sender, $e)
    if ($script:Dragging) {
        $p  = [System.Windows.Forms.Cursor]::Position
        $dx = $p.X - $script:DragOrigin.X
        $dy = $p.Y - $script:DragOrigin.Y
        if ([Math]::Abs($dx) + [Math]::Abs($dy) -gt 3) { $script:Moved = $true }
        if ($script:Moved) {
            $form.Location = New-Object System.Drawing.Point (($script:FormOrigin.X + $dx), ($script:FormOrigin.Y + $dy))
        }
    }
})

$form.Add_MouseUp({
    param($sender, $e)
    if ($e.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }
    $script:Dragging = $false
    if ($script:Moved) {
        # przeciągnięcie = pozycja dowolna; kotwica przestaje obowiązywać
        $script:Cfg.Anchor = 'Free'
        $script:Cfg.X = $form.Left + $ShadowM
        $script:Cfg.Y = $form.Top + $ShadowM
        Update-AnchorMenu
        Save-OverlayConfig
        return
    }

    $c = Get-CursorInContent
    if ($script:Open) {
        $idx = Get-RowAt $c
        if ($idx -ge 0) { Invoke-FocusEditor $script:Rows[$idx] }
        elseif (-not [bool]$script:Cfg.Hover) { Set-Open $false }
    } else {
        if (-not [bool]$script:Cfg.Hover) { Set-Open $true }
        elseif ($script:Rows.Count -gt 0) { Invoke-FocusEditor $script:Rows[0] }
    }
})

# --------------------------------------------------------------------------- #
#  Menu kontekstowe                                                            #
# --------------------------------------------------------------------------- #
$menu = New-Object System.Windows.Forms.ContextMenuStrip

$miSound = New-Object System.Windows.Forms.ToolStripMenuItem 'Dźwięki'
$miSound.CheckOnClick = $true
$miSound.Checked = [bool]$script:Cfg.Sound
$miSound.Add_Click({ $script:Cfg.Sound = $miSound.Checked; Save-OverlayConfig })

$miHover = New-Object System.Windows.Forms.ToolStripMenuItem 'Rozwijaj po najechaniu'
$miHover.CheckOnClick = $true
$miHover.Checked = [bool]$script:Cfg.Hover
$miHover.Add_Click({
    $script:Cfg.Hover = $miHover.Checked
    if (-not $miHover.Checked) { Set-Open $false }
    Save-OverlayConfig
})

$miUsage = New-Object System.Windows.Forms.ToolStripMenuItem 'Limity 5 h / 7 dni'
$miUsage.CheckOnClick = $true
$miUsage.Checked = [bool]$script:Cfg.Usage
$miUsage.Add_Click({
    $script:Cfg.Usage = $miUsage.Checked
    Update-UsageView
    Save-OverlayConfig
})

$miDetached = New-Object System.Windows.Forms.ToolStripMenuItem 'Odklejona od krawędzi'
$miDetached.CheckOnClick = $true
$miDetached.Checked = [bool]$script:Cfg.Detached
$miDetached.Add_Click({
    $script:Cfg.Detached = $miDetached.Checked
    Save-OverlayConfig
})

$miTop = New-Object System.Windows.Forms.ToolStripMenuItem 'Zawsze na wierzchu'
$miTop.CheckOnClick = $true
$miTop.Checked = [bool]$script:Cfg.TopMost
$miTop.Add_Click({
    $script:Cfg.TopMost = $miTop.Checked
    $form.TopMost = $miTop.Checked
    Save-OverlayConfig
})

$miClear = New-Object System.Windows.Forms.ToolStripMenuItem 'Wyczyść zakończone sesje'
$miClear.Add_Click({
    foreach ($r in $script:Rows) {
        if ($r.State -eq 'done' -or $r.State -eq 'idle') {
            Remove-Item -LiteralPath $r.File -Force -ErrorAction SilentlyContinue
        }
    }
    Update-Data
})

# Pozycja: krawędzie ekranu jako "radio", plus pozycja dowolna
$miPos = New-Object System.Windows.Forms.ToolStripMenuItem 'Pozycja'
$script:AnchorItems = @{}

function Set-Anchor([string]$a) {
    $script:Cfg.Anchor = $a
    if ($a -eq 'Free') {
        $script:Cfg.X = $form.Left + $ShadowM
        $script:Cfg.Y = $form.Top + $ShadowM
    }
    Update-AnchorMenu
    Save-OverlayConfig
}

function Update-AnchorMenu {
    $cur = Get-Anchor
    foreach ($k in @($script:AnchorItems.Keys)) { $script:AnchorItems[$k].Checked = ($k -eq $cur) }
}

foreach ($k in $script:Anchors.Keys) {
    $mi = New-Object System.Windows.Forms.ToolStripMenuItem $script:Anchors[$k]
    $mi.Tag = $k
    $mi.Add_Click({ param($s, $e) Set-Anchor ([string]$s.Tag) })
    [void]$miPos.DropDownItems.Add($mi)
    $script:AnchorItems[$k] = $mi
    # separator po każdym rzędzie (góra / środek / dół)
    if ($k -eq 'TopRight' -or $k -eq 'MiddleRight') {
        [void]$miPos.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
    }
}
[void]$miPos.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$miFree = New-Object System.Windows.Forms.ToolStripMenuItem 'Dowolna (przeciągnij pastylkę)'
$miFree.Tag = 'Free'
$miFree.Add_Click({ Set-Anchor 'Free' })
[void]$miPos.DropDownItems.Add($miFree)
$script:AnchorItems['Free'] = $miFree
Update-AnchorMenu

$miExit = New-Object System.Windows.Forms.ToolStripMenuItem 'Zamknij nakładkę'
$miExit.Add_Click({ $form.Close() })

[void]$menu.Items.Add($miPos)
[void]$menu.Items.Add($miDetached)
[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$menu.Items.Add($miSound)
[void]$menu.Items.Add($miHover)
[void]$menu.Items.Add($miUsage)
[void]$menu.Items.Add($miTop)
[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$menu.Items.Add($miClear)
[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$menu.Items.Add($miExit)

$form.ContextMenuStrip = $menu

# --------------------------------------------------------------------------- #
#  Start                                                                       #
# --------------------------------------------------------------------------- #
$form.Add_Shown({
    Update-Data
    Update-MeterAnim 1000
    $script:LastFrame = $script:Clock.Elapsed.TotalMilliseconds
    $script:LastData  = $script:LastFrame
    Render-Frame
    $timer.Start()
})

$form.Add_FormClosing({
    $timer.Stop()
    if ((Get-Anchor) -eq 'Free') {
        $script:Cfg.X = $form.Left + $ShadowM
        $script:Cfg.Y = $form.Top + $ShadowM
    }
    Save-OverlayConfig
})

[System.Windows.Forms.Application]::EnableVisualStyles()
[System.Windows.Forms.Application]::Run($form)
