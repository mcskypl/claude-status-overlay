<#
    claude-status-hook.ps1
    ----------------------
    Wywolywany przez hooki Claude Code. Czyta JSON ze stdin i zapisuje
    plik stanu dla danej sesji w %USERPROFILE%\.claude\status\<session>.json
    Nakladka (ClaudeStatusOverlay.ps1) odczytuje te pliki i pokazuje kropki.

    Uzycie:
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File claude-status-hook.ps1 -State working
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('idle', 'working', 'done', 'attention', 'error', 'end')]
    [string]$State,

    [string]$Note = ''
)

$ErrorActionPreference = 'SilentlyContinue'

$statusDir = Join-Path $env:USERPROFILE '.claude\status'
if (-not (Test-Path -LiteralPath $statusDir)) {
    New-Item -ItemType Directory -Path $statusDir -Force | Out-Null
}

# ---- odczyt danych ze stdin -------------------------------------------------
$raw = ''
try { $raw = [Console]::In.ReadToEnd() } catch { $raw = '' }

$data = $null
if ($raw -and $raw.Trim().StartsWith('{')) {
    try { $data = $raw | ConvertFrom-Json } catch { $data = $null }
}

function Get-Field {
    param($Obj, [string[]]$Names)
    if ($null -eq $Obj) { return '' }
    foreach ($n in $Names) {
        $p = $Obj.PSObject.Properties[$n]
        if ($p -and $p.Value) { return [string]$p.Value }
    }
    return ''
}

$sessionId = Get-Field $data @('session_id')
if (-not $sessionId) { $sessionId = 'default' }

$cwd     = Get-Field $data @('cwd')
$notType = Get-Field $data @('notification_type', 'type')
$message = Get-Field $data @('message', 'title', 'notification')

# Sciezka transkryptu sesji. Nakladka patrzy na czas modyfikacji tego pliku,
# zeby poznac, ze tura leci dalej po udzieleniu zgody - Claude Code nie ma
# zdarzenia "zgoda udzielona", wiec inaczej stan "attention" wisialby do konca tury.
$transcript = Get-Field $data @('transcript_path', 'transcript')

# ---- filtr dla zdarzen Notification ----------------------------------------
# Notification odpala sie tez przy logowaniu / odswiezeniu tokenu - to nie jest
# sytuacja "Claude czeka na Ciebie", wiec takie zdarzenia ignorujemy.
if ($State -eq 'attention') {
    $ignore = 'auth_success|auth_failure|auth_refresh|login|logout|update_available|installed'
    if ($notType -and ($notType -match $ignore)) { exit 0 }
    if (-not $notType -and $message -and ($message -match '(?i)logged in|signed in|update available')) { exit 0 }
}

# ---- nazwa pliku ------------------------------------------------------------
$safeId = ($sessionId -replace '[^A-Za-z0-9_\-\.]', '_')
if ($safeId.Length -gt 80) { $safeId = $safeId.Substring(0, 80) }
$target = Join-Path $statusDir ($safeId + '.json')

# ---- koniec sesji: kasujemy wpis -------------------------------------------
if ($State -eq 'end') {
    Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
    exit 0
}

# ---- poprzedni stan (potrzebny do decyzji o powiadomieniu) -----------------
$prevState = ''
$prevTs    = $null
if (Test-Path -LiteralPath $target) {
    try {
        $prev = Get-Content -LiteralPath $target -Raw | ConvertFrom-Json
        if ($prev.state) { $prevState = [string]$prev.state }
        if ($prev.ts) { $prevTs = [datetime]::Parse([string]$prev.ts) }
    } catch { }
}

# ---- projekt = nazwa katalogu roboczego ------------------------------------
$project = ''
if ($cwd) {
    try { $project = Split-Path -Path $cwd -Leaf } catch { $project = '' }
}
if (-not $project) { $project = 'Claude' }

if (-not $Note) {
    if ($message) { $Note = $message }
    elseif ($notType) { $Note = $notType }
}
if ($Note.Length -gt 120) { $Note = $Note.Substring(0, 120) }

$payload = [ordered]@{
    session_id = $sessionId
    state      = $State
    project    = $project
    cwd        = $cwd
    note       = $Note
    transcript = $transcript
    ts         = (Get-Date).ToString('o')
}

# zapis atomowy (tmp + move), zeby nakladka nie zlapala polowy pliku
$tmp = $target + '.tmp'
try {
    ($payload | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $tmp -Encoding UTF8 -Force
    Move-Item -LiteralPath $tmp -Destination $target -Force
} catch {
    try { ($payload | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $target -Encoding UTF8 -Force } catch { }
}

# ---- powiadomienie push (ntfy) ---------------------------------------------
# Wysylamy tylko wtedy, gdy stan faktycznie sie zmienil - zeby nie dublowac.
if ($State -ne $prevState) {
    $notifyLib = Join-Path $PSScriptRoot 'ClaudeStatusNotify.ps1'
    if (Test-Path -LiteralPath $notifyLib) {
        try {
            . $notifyLib
            $cfg = Get-NotifyConfig
            if ($cfg) {
                $states = @('attention', 'error')
                if ($cfg.notifyStates) { $states = @($cfg.notifyStates) }

                $doneAfter = 60
                if ($null -ne $cfg.notifyDoneAfterSeconds) { $doneAfter = [int]$cfg.notifyDoneAfterSeconds }

                $elapsed = 0
                if ($prevTs) { $elapsed = [int]((Get-Date) - $prevTs).TotalSeconds }

                $should = $false
                if ($states -contains $State) { $should = $true }
                if ($State -eq 'done' -and $doneAfter -ge 0 -and $prevState -eq 'working' -and $elapsed -ge $doneAfter) {
                    $should = $true
                }

                if ($should) {
                    Send-ClaudeNotification -State $State -Project $project -Note $Note `
                        -ElapsedSeconds $elapsed -Config $cfg
                }
            }
        } catch { }
    }
}

exit 0
