<#
    Setup-Notifications.ps1
    -----------------------
    Konfiguruje powiadomienia push na telefon przez ntfy.

    Domyślnie generuje losowy, trudny do zgadnięcia temat i zapisuje go w
    %USERPROFILE%\.claude\status-notify.config.json, a potem wysyła
    powiadomienie testowe.

    Użycie:
        powershell -ExecutionPolicy Bypass -File .\Setup-Notifications.ps1
        powershell -ExecutionPolicy Bypass -File .\Setup-Notifications.ps1 -Topic moj-wlasny-temat
        powershell -ExecutionPolicy Bypass -File .\Setup-Notifications.ps1 -Server https://ntfy.mojafirma.pl -Token tk_xxx
        powershell -ExecutionPolicy Bypass -File .\Setup-Notifications.ps1 -DoneAfterSeconds 120
        powershell -ExecutionPolicy Bypass -File .\Setup-Notifications.ps1 -Test
        powershell -ExecutionPolicy Bypass -File .\Setup-Notifications.ps1 -Disable
#>

[CmdletBinding()]
param(
    [string]$Topic,
    [string]$Server = 'https://ntfy.sh',
    [string]$Token,

    # "gotowe" wysyłamy tylko gdy tura trwała dłużej niż tyle sekund; -1 = nigdy
    [int]$DoneAfterSeconds = 60,

    # adres podglądu w sieci lokalnej — otwiera się po kliknięciu powiadomienia
    [string]$WebUrl,
    [int]$Port = 8787,

    [switch]$Test,
    [switch]$Disable
)

$ErrorActionPreference = 'Stop'

$CfgPath = Join-Path $env:USERPROFILE '.claude\status-notify.config.json'
$ClaudeDir = Split-Path -Parent $CfgPath
if (-not (Test-Path -LiteralPath $ClaudeDir)) { New-Item -ItemType Directory -Path $ClaudeDir -Force | Out-Null }

function Get-ExistingConfig {
    if (-not (Test-Path -LiteralPath $CfgPath)) { return $null }
    try { return Get-Content -LiteralPath $CfgPath -Raw | ConvertFrom-Json } catch { return $null }
}

# --------------------------------------------------------------------------- #
if ($Disable) {
    $c = Get-ExistingConfig
    if (-not $c) { Write-Host 'Powiadomienia nie były skonfigurowane.' -ForegroundColor DarkGray; return }
    $c.enabled = $false
    ($c | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $CfgPath -Encoding UTF8 -Force
    Write-Host 'Powiadomienia wyłączone (konfiguracja zachowana).' -ForegroundColor Green
    return
}

# --------------------------------------------------------------------------- #
$existing = Get-ExistingConfig

if ($Test) {
    if (-not $existing -or -not $existing.topic) {
        Write-Host 'Brak konfiguracji — uruchom najpierw skrypt bez -Test.' -ForegroundColor Red
        return
    }
    . (Join-Path $PSScriptRoot 'ClaudeStatusNotify.ps1')
    $existing.enabled = $true
    Send-ClaudeNotification -State 'attention' -Project 'Test' `
        -Note 'Powiadomienie testowe z komputera.' -Config $existing
    Write-Host ('Wysłano test na temat: ' + $existing.topic) -ForegroundColor Green
    return
}

# --- temat ------------------------------------------------------------------
if (-not $Topic) {
    if ($existing -and $existing.topic) {
        $Topic = [string]$existing.topic
        Write-Host ('Zachowuję istniejący temat: ' + $Topic) -ForegroundColor DarkGray
    } else {
        $chars = 'abcdefghijkmnpqrstuvwxyz23456789'
        $rnd = -join (1..12 | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })
        $Topic = 'claude-' + $rnd
    }
}

# --- adres podglądu ---------------------------------------------------------
if (-not $WebUrl) {
    $ip = $null
    try {
        $ip = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
               Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
               Select-Object -First 1 -ExpandProperty IPAddress)
    } catch { }
    if (-not $ip) {
        try {
            $ip = ([System.Net.Dns]::GetHostAddresses([System.Net.Dns]::GetHostName()) |
                   Where-Object { $_.AddressFamily -eq 'InterNetwork' -and $_.ToString() -notlike '127.*' } |
                   Select-Object -First 1).ToString()
        } catch { }
    }
    if ($ip) { $WebUrl = ('http://{0}:{1}/' -f $ip, $Port) }
}

$cfg = [ordered]@{
    enabled                = $true
    provider               = 'ntfy'
    server                 = $Server.TrimEnd('/')
    topic                  = $Topic
    notifyStates           = @('attention', 'error')
    notifyDoneAfterSeconds = $DoneAfterSeconds
    timeoutSeconds         = 6
}
if ($Token)  { $cfg['token']  = $Token }
if ($WebUrl) { $cfg['webUrl'] = $WebUrl }

($cfg | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $CfgPath -Encoding UTF8 -Force

$subUrl = $cfg.server + '/' + $Topic

Write-Host ''
Write-Host '  Powiadomienia push — skonfigurowane' -ForegroundColor Cyan
Write-Host '  -----------------------------------' -ForegroundColor Cyan
Write-Host ('  Serwer:  ' + $cfg.server)
Write-Host ('  Temat:   ' + $Topic) -ForegroundColor Yellow
Write-Host ('  Adres:   ' + $subUrl) -ForegroundColor Green
if ($WebUrl) { Write-Host ('  Podgląd: ' + $WebUrl + '  (otwiera się po kliknięciu powiadomienia)') }
Write-Host ''
Write-Host '  Na telefonie:' -ForegroundColor White
Write-Host '   1. Zainstaluj aplikację "ntfy" (Google Play / App Store).'
Write-Host '   2. Dodaj subskrypcję i wpisz dokładnie temat podany wyżej.'
if ($cfg.server -ne 'https://ntfy.sh') {
    Write-Host ('   3. W ustawieniach aplikacji ustaw własny serwer: ' + $cfg.server)
}
Write-Host ''
Write-Host '  Kiedy przyjdzie powiadomienie:' -ForegroundColor White
Write-Host '   - zawsze, gdy Claude prosi o zgodę albo tura padła na błędzie'
if ($DoneAfterSeconds -ge 0) {
    Write-Host ('   - przy zakończeniu pracy, jeśli tura trwała dłużej niż ' + $DoneAfterSeconds + ' s')
} else {
    Write-Host '   - przy zakończeniu pracy: nigdy'
}
Write-Host ''
Write-Host '  Uwaga: publiczny ntfy.sh nie wymaga konta, ale każdy, kto zna nazwę' -ForegroundColor DarkYellow
Write-Host '  tematu, może czytać te powiadomienia. Temat jest losowy; jeśli chcesz' -ForegroundColor DarkYellow
Write-Host '  pełnej prywatności, postaw własny ntfy i użyj -Server / -Token.' -ForegroundColor DarkYellow
Write-Host ''

# --- test -------------------------------------------------------------------
. (Join-Path $PSScriptRoot 'ClaudeStatusNotify.ps1')
Send-ClaudeNotification -State 'attention' -Project 'Claude Status' `
    -Note 'Konfiguracja zakończona — tak będą wyglądać powiadomienia.' `
    -Config ($cfg | ConvertTo-Json -Depth 6 | ConvertFrom-Json)

Write-Host '  Wysłano powiadomienie testowe. Jeśli nie dotarło, sprawdź nazwę tematu' -ForegroundColor DarkGray
Write-Host '  w aplikacji i połączenie z internetem.' -ForegroundColor DarkGray
Write-Host ''
