<#
    Install.ps1
    -----------
    Buduje i instaluje nakładkę statusu Claude Code:
      1. dotnet publish -> %USERPROFILE%\.claude\status-overlay
      2. ClaudeStatusHook.exe --install  (hooki w settings.json, z backupem)
      3. opcjonalnie skrót w autostarcie
      4. uruchamia nakładkę

    Użycie:
        powershell -ExecutionPolicy Bypass -File .\Install.ps1
        powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Autostart
        powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Uninstall

    Wymaga .NET SDK 10 (do zbudowania) i .NET Desktop Runtime 10 (do działania).
#>

[CmdletBinding()]
param(
    [switch]$Autostart,
    [switch]$NoStart,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$ClaudeDir  = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$Dest       = Join-Path $ClaudeDir 'status-overlay'
$OverlayExe = Join-Path $Dest 'ClaudeStatusOverlay.exe'
$HookExe    = Join-Path $Dest 'ClaudeStatusHook.exe'
$StartupDir = [Environment]::GetFolderPath('Startup')
$StartupLnk = Join-Path $StartupDir 'Claude Status Overlay.lnk'
$Src        = $PSScriptRoot
if (-not $Src) { $Src = Split-Path -Parent $MyInvocation.MyCommand.Path }

function Stop-Overlay {
    # grzecznie: nazwane zdarzenie, na które czeka działająca instancja
    if (Test-Path -LiteralPath $OverlayExe) {
        & $OverlayExe --exit 2>$null | Out-Null
    }
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Process ClaudeStatusOverlay -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    Get-Process ClaudeStatusOverlay -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

# --------------------------------------------------------------------------- #
#  Deinstalacja                                                                #
# --------------------------------------------------------------------------- #
if ($Uninstall) {
    Write-Host 'Usuwanie nakładki statusu Claude Code...' -ForegroundColor Cyan
    Stop-Overlay

    if (Test-Path -LiteralPath $HookExe) {
        & $HookExe --uninstall
    } else {
        Write-Host '  (brak ClaudeStatusHook.exe - hooki w settings.json zostawiam do ręcznego usunięcia)' -ForegroundColor Yellow
    }

    if (Test-Path -LiteralPath $StartupLnk) { Remove-Item -LiteralPath $StartupLnk -Force }
    if (Test-Path -LiteralPath $Dest) { Remove-Item -LiteralPath $Dest -Recurse -Force }
    Write-Host '  pliki usunięte' -ForegroundColor Green
    Write-Host 'Gotowe. Zamknij i otwórz ponownie sesje Claude Code.' -ForegroundColor Cyan
    return
}

# --------------------------------------------------------------------------- #
#  Instalacja                                                                  #
# --------------------------------------------------------------------------- #
Write-Host 'Instalacja nakładki statusu Claude Code' -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Brak dotnet w PATH. Zainstaluj .NET SDK 10: https://dotnet.microsoft.com/download'
}

# działająca nakładka (także uruchomiona z bin\ podczas prac) blokuje DLL-ki
Stop-Overlay

$Stage = Join-Path ([System.IO.Path]::GetTempPath()) ('claude-status-overlay-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Stage -Force | Out-Null
try {
    Write-Host '  budowanie (dotnet publish)...' -ForegroundColor DarkGray
    foreach ($proj in @('src\ClaudeStatus.Overlay', 'src\ClaudeStatus.Hook')) {
        & dotnet publish (Join-Path $Src $proj) -c Release -o $Stage --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish nie powiódł się dla $proj" }
    }

    New-Item -ItemType Directory -Path $Dest -Force | Out-Null
    Copy-Item -Path (Join-Path $Stage '*') -Destination $Dest -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $Src 'README.md') -Destination $Dest -Force -ErrorAction SilentlyContinue
    Write-Host "  pliki: $Dest" -ForegroundColor Green
} finally {
    Remove-Item -LiteralPath $Stage -Recurse -Force -ErrorAction SilentlyContinue
}

# --- hooki -----------------------------------------------------------------
& $HookExe --install
if ($LASTEXITCODE -ne 0) { throw 'Rejestracja hooków nie powiodła się.' }

# --- autostart -------------------------------------------------------------
if ($Autostart) {
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($StartupLnk)
    $sc.TargetPath       = $OverlayExe
    $sc.WorkingDirectory = $Dest
    $sc.Description      = 'Claude Code status overlay'
    $sc.Save()
    Write-Host "  autostart: $StartupLnk" -ForegroundColor Green
}

# --- start -----------------------------------------------------------------
if (-not $NoStart) {
    Start-Process -FilePath $OverlayExe -WorkingDirectory $Dest | Out-Null
    Write-Host '  nakładka uruchomiona (góra ekranu, na środku - prawy przycisk zmienia pozycję)' -ForegroundColor Green
}

Write-Host ''
Write-Host 'Gotowe. Zamknij i otwórz ponownie sesje Claude Code, żeby załadowały nowe hooki.' -ForegroundColor Cyan
