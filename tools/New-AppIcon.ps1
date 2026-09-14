<#
    New-AppIcon.ps1
    ---------------
    Generuje app.ico dla nakładki - miniaturę samego widgetu: czarny zaokrąglony
    kwadrat, w środku barwny znacznik stanu, po bokach paski limitów 5 h / 7 dni.

    Rozmiary 16-128 lecą jako BMP (klasyczny format ikony), 256 jako PNG -
    tak wygląda typowa ikona aplikacji na Windows i tak ją czyta zarówno
    kompilator C# (ApplicationIcon), jak i Inno Setup.

        powershell -ExecutionPolicy Bypass -File .\tools\New-AppIcon.ps1
#>

[CmdletBinding()]
param(
    [string]$Out = ''
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $Out) { $Out = Join-Path $root '..\src\ClaudeStatus.Overlay\app.ico' }
Add-Type -AssemblyName System.Drawing

$Sizes = @(16, 24, 32, 48, 64, 128, 256)

# kolory z palety nakładki (Palette.cs)
$Black = [System.Drawing.Color]::FromArgb(255, 11, 11, 12)
$Amber = [System.Drawing.Color]::FromArgb(255, 232, 170, 78)
$Blue = [System.Drawing.Color]::FromArgb(255, 96, 170, 243)
$Green = [System.Drawing.Color]::FromArgb(255, 98, 187, 120)

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = [Math]::Min($r, [Math]::Min($w, $h) / 2)
    if ($r -le 0.01) { $p.AddRectangle((New-Object System.Drawing.RectangleF($x, $y, $w, $h))); return $p }
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # tło: czarny zaokrąglony kwadrat z cienką obwódką (jak powłoka widgetu)
    $pad = [single]($size * 0.045)
    $side = [single]($size - 2 * $pad)
    $bg = New-RoundedPath $pad $pad $side $side ([single]($size * 0.235))
    $g.FillPath((New-Object System.Drawing.SolidBrush($Black)), $bg)
    if ($size -ge 32) {
        $pen = New-Object System.Drawing.Pen((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(28, 255, 255, 255))), [single]([Math]::Max(1, $size / 96)))
        $g.DrawPath($pen, $bg)
        $pen.Dispose()
    }

    # linia widgetu: paski limitów po bokach, znacznik stanu w środku
    $cy = [single]($size / 2)
    $barH  = [single]([Math]::Max(2, [Math]::Round($size * 0.085)))
    $markW = [single]([Math]::Max(3, [Math]::Round($size * 0.19)))
    $markH = [single]([Math]::Max(3, [Math]::Round($size * 0.125)))
    $inner = [single]($size * 0.11)
    $gap   = [single]([Math]::Max(1, $size * 0.035))

    if ($size -ge 32) {
        $barLen = [single](($size / 2) - $inner - ($markW / 2) - $gap)
        if ($barLen -gt 1) {
            $left = New-Object System.Drawing.RectangleF($inner, ($cy - $barH / 2), $barLen, $barH)
            $right = New-Object System.Drawing.RectangleF(($size - $inner - $barLen), ($cy - $barH / 2), $barLen, $barH)

            # tory
            $track = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(46, 255, 255, 255))
            foreach ($r in @($left, $right)) {
                $p = New-RoundedPath $r.X $r.Y $r.Width $r.Height ($barH / 2)
                $g.FillPath($track, $p); $p.Dispose()
            }
            $track.Dispose()

            # wypełnienia: 5 h rośnie od lewej ku środkowi, 7 dni od prawej
            $fillL = [single]($barLen * 0.62)
            $fillR = [single]($barLen * 0.41)
            $pL = New-RoundedPath $left.X $left.Y $fillL $barH ($barH / 2)
            $g.FillPath((New-Object System.Drawing.SolidBrush($Blue)), $pL); $pL.Dispose()
            $pR = New-RoundedPath ($right.Right - $fillR) $right.Y $fillR $barH ($barH / 2)
            $g.FillPath((New-Object System.Drawing.SolidBrush($Green)), $pR); $pR.Dispose()
        }
    }

    $mark = New-RoundedPath ($cy - $markW / 2) ($cy - $markH / 2) $markW $markH ($markH / 2)
    $g.FillPath((New-Object System.Drawing.SolidBrush($Amber)), $mark)
    $mark.Dispose()

    $bg.Dispose()
    $g.Dispose()
    return $bmp
}

# --- składanie pliku .ico ---------------------------------------------------
$entries = @()
foreach ($size in $Sizes) {
    $bmp = New-IconBitmap $size
    if ($size -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $data = $ms.ToArray()
        $ms.Dispose()
    } else {
        # BITMAPINFOHEADER + piksele BGRA od dołu + maska AND (zerowa, alfa wystarcza)
        $w = $bmp.Width; $h = $bmp.Height
        $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
        $locked = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $pixels = New-Object byte[] ($locked.Stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $pixels, 0, $pixels.Length)
        $bmp.UnlockBits($locked)

        $maskStride = [int](([Math]::Floor(($w + 31) / 32)) * 4)
        $dib = New-Object System.IO.MemoryStream
        $bw = New-Object System.IO.BinaryWriter($dib)
        $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32]($h * 2))
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
        $bw.Write([uint32]($w * $h * 4 + $maskStride * $h))
        $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        for ($y = $h - 1; $y -ge 0; $y--) {
            $bw.Write($pixels, $y * $locked.Stride, $w * 4)
        }
        $bw.Write((New-Object byte[] ($maskStride * $h)), 0, $maskStride * $h)
        $bw.Flush()
        $data = $dib.ToArray()
        $bw.Dispose(); $dib.Dispose()
    }
    $entries += [pscustomobject]@{ Size = $size; Data = $data }
    $bmp.Dispose()
}

$Out = [System.IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $bw.Write([byte]($(if ($e.Size -ge 256) { 0 } else { $e.Size })))
    $bw.Write([byte]($(if ($e.Size -ge 256) { 0 } else { $e.Size })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e.Data.Length)
    $bw.Write([uint32]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) { $bw.Write($e.Data, 0, $e.Data.Length) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

"ikona: $Out ($((Get-Item $Out).Length) B, rozmiary: $($Sizes -join ', '))"
