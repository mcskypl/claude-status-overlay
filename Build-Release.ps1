<#
    Build-Release.ps1
    -----------------
    Buduje paczkę wydania:
      publish\app\                        gotowe pliki (oba exe + zależności)
      publish\ClaudeStatusOverlay-Setup-<wersja>.exe   instalator (Inno Setup)
      publish\ClaudeStatusOverlay-<wersja>-portable.zip  wersja do rozpakowania

    Wersję bierze z Directory.Build.props (<Version>), chyba że podasz -Version.
    Tego samego skryptu używa workflow GitHuba przy tagu v*.

        powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1
        powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1 -Version 2.1.0
#>

[CmdletBinding()]
param(
    [string]$Version = '',
    [switch]$NoInstaller
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }

$publish = Join-Path $root 'publish'
$appDir = Join-Path $publish 'app'
$props = Join-Path $root 'Directory.Build.props'

if (-not $Version) {
    $xml = [xml](Get-Content -LiteralPath $props -Raw)
    $Version = ($xml.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
    if (-not $Version) { throw "Nie znalazłem <Version> w $props" }
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Wersja '$Version' nie wygląda jak 1.2.3" }

Write-Host "Claude Status Overlay $Version" -ForegroundColor Cyan

# --- 1. czysty katalog wydania ---------------------------------------------
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

# --- 2. ikona ---------------------------------------------------------------
$icon = Join-Path $root 'src\ClaudeStatus.Overlay\app.ico'
if (-not (Test-Path -LiteralPath $icon)) {
    Write-Host '  generowanie ikony...' -ForegroundColor DarkGray
    & (Join-Path $root 'tools\New-AppIcon.ps1') | Out-Null
}

# --- 3. build ---------------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Brak dotnet w PATH. Zainstaluj .NET SDK 10: https://dotnet.microsoft.com/download'
}

foreach ($proj in @('src\ClaudeStatus.Overlay', 'src\ClaudeStatus.Hook')) {
    Write-Host "  dotnet publish $proj" -ForegroundColor DarkGray
    & dotnet publish (Join-Path $root $proj) -c Release -o $appDir --nologo -v q `
        -p:Version=$Version -p:FileVersion=$Version -p:InformationalVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish nie powiódł się dla $proj" }
}
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $appDir -Force -ErrorAction SilentlyContinue

# --- 4. zip portable --------------------------------------------------------
$zip = Join-Path $publish "ClaudeStatusOverlay-$Version-portable.zip"
Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "  $([IO.Path]::GetFileName($zip))  ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)" -ForegroundColor Green

# --- 5. instalator ----------------------------------------------------------
if ($NoInstaller) { return }

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) {
    throw @'
Nie znalazłem ISCC.exe (Inno Setup 6). Zainstaluj:
    winget install --id JRSoftware.InnoSetup -e
albo uruchom z -NoInstaller, żeby zbudować samą paczkę portable.
'@
}

Write-Host "  ISCC: $iscc" -ForegroundColor DarkGray
& $iscc "/DAppVersion=$Version" "/DSourceDir=$appDir" (Join-Path $root 'installer\ClaudeStatusOverlay.iss') | ForEach-Object {
    if ($_ -match 'error|błąd' -or $env:VERBOSE_ISCC) { Write-Host "    $_" -ForegroundColor DarkGray }
}
if ($LASTEXITCODE -ne 0) { throw "Inno Setup zakończył się kodem $LASTEXITCODE" }

$setup = Join-Path $publish "ClaudeStatusOverlay-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "Instalator nie powstał: $setup" }
Write-Host "  $([IO.Path]::GetFileName($setup))  ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)" -ForegroundColor Green

Write-Host ''
Write-Host "Gotowe: $publish" -ForegroundColor Cyan
