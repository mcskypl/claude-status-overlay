<#
    Publish-Version.ps1
    -------------------
    Jedno polecenie od "mam zmiany" do "wersja jest na GitHubie":

        powershell -ExecutionPolicy Bypass -File .\tools\Publish-Version.ps1 -Version 2.1.0

    Robi po kolei:
      1. podbija <Version> w Directory.Build.props,
      2. commit "wydanie 2.1.0" + tag v2.1.0,
      3. push gałęzi i tagu.

    Resztę robi workflow .github/workflows/release.yml: buduje instalator,
    paczkę portable i tworzy wydanie na GitHubie. Nakładki użytkowników
    zauważą je przy najbliższym sprawdzeniu (raz na dobę).

    -WhatIf pokazuje, co by się stało, bez ruszania repozytorium.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$Message = ''
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = Split-Path -Parent $root
$props = Join-Path $root 'Directory.Build.props'
$tag = "v$Version"

Push-Location $root
try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Brak git w PATH.' }

    $status = (git status --porcelain) -join "`n"
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()

    if (git tag --list $tag) { throw "Tag $tag już istnieje." }

    # --- 1. wersja w Directory.Build.props ---------------------------------
    $text = Get-Content -LiteralPath $props -Raw
    $current = [regex]::Match($text, '<Version>([^<]+)</Version>').Groups[1].Value
    if (-not $current) { throw "Nie znalazłem <Version> w $props" }

    if ($current -eq $Version) {
        Write-Host "Directory.Build.props ma już $Version" -ForegroundColor DarkGray
    } else {
        Write-Host "$current -> $Version" -ForegroundColor Cyan
        if ($PSCmdlet.ShouldProcess($props, "ustaw wersję $Version")) {
            $updated = [regex]::Replace($text, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
            [System.IO.File]::WriteAllText($props, $updated, (New-Object System.Text.UTF8Encoding($false)))
        }
    }

    # --- 2. commit + tag ----------------------------------------------------
    if (-not $Message) { $Message = "wydanie $Version" }

    if ($PSCmdlet.ShouldProcess($branch, "commit i tag $tag")) {
        git add -- $props
        if ($status -or ($current -ne $Version)) {
            git commit -m $Message
            if ($LASTEXITCODE -ne 0) { throw 'git commit nie powiódł się (nic do zatwierdzenia?)' }
        }
        git tag -a $tag -m "Claude Status Overlay $Version"
        if ($LASTEXITCODE -ne 0) { throw 'git tag nie powiódł się' }
    }

    # --- 3. push ------------------------------------------------------------
    if ($PSCmdlet.ShouldProcess('origin', "push $branch + $tag")) {
        git push origin $branch --follow-tags
        if ($LASTEXITCODE -ne 0) { throw 'git push nie powiódł się' }
    }

    Write-Host ''
    Write-Host "Wysłane. Wydanie zbuduje workflow 'release' - postęp:" -ForegroundColor Cyan
    $url = (git remote get-url origin) -replace '\.git$', '' -replace '^git@github\.com:', 'https://github.com/'
    Write-Host "  $url/actions" -ForegroundColor DarkGray
    Write-Host "  $url/releases" -ForegroundColor DarkGray
} finally {
    Pop-Location
}
