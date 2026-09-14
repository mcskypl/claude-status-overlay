<#
    Install-ClaudeStatusOverlay.ps1
    -------------------------------
    Instaluje nakładkę statusu Claude Code:
      1. kopiuje pliki do %USERPROFILE%\.claude\status-overlay
      2. dopisuje hooki do %USERPROFILE%\.claude\settings.json (z backupem)
      3. opcjonalnie dodaje skrót do autostartu
      4. uruchamia nakładkę

    Użycie:
        powershell -ExecutionPolicy Bypass -File .\Install-ClaudeStatusOverlay.ps1
        powershell -ExecutionPolicy Bypass -File .\Install-ClaudeStatusOverlay.ps1 -Autostart
        powershell -ExecutionPolicy Bypass -File .\Install-ClaudeStatusOverlay.ps1 -Uninstall
#>

[CmdletBinding()]
param(
    [switch]$Autostart,
    [switch]$NoStart,
    [switch]$Uninstall,

    # uruchom też serwer podglądu w sieci lokalnej (telefon)
    [switch]$WithServer,
    [int]$Port = 8787,

    # dodaj regułę zapory dla portu serwera (wymaga uruchomienia jako administrator)
    [switch]$OpenFirewall
)

$ErrorActionPreference = 'Stop'

$ClaudeDir    = Join-Path $env:USERPROFILE '.claude'
$Dest         = Join-Path $ClaudeDir 'status-overlay'
$SettingsPath = Join-Path $ClaudeDir 'settings.json'
$HookPath     = Join-Path $Dest 'claude-status-hook.ps1'
$OverlayPath  = Join-Path $Dest 'ClaudeStatusOverlay.ps1'
$VbsPath      = Join-Path $Dest 'Start-Overlay.vbs'
$ServerVbs    = Join-Path $Dest 'Start-Server.vbs'
$StartupDir   = [Environment]::GetFolderPath('Startup')
if (-not $StartupDir) { $StartupDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup' }
$StartupLnk   = Join-Path $StartupDir 'Claude Status Overlay.lnk'
$StartupSrvLnk = Join-Path $StartupDir 'Claude Status Server.lnk'
$Marker       = 'claude-status-hook.ps1'

$EventMap = [ordered]@{
    'SessionStart'     = 'idle'
    'UserPromptSubmit' = 'working'
    'Notification'     = 'attention'
    'Stop'             = 'done'
    'StopFailure'      = 'error'
    'SessionEnd'       = 'end'
}

function ConvertTo-PlainHashtable {
    param($InputObject)
    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) {
        $h = [ordered]@{}
        foreach ($k in $InputObject.Keys) { $h[$k] = ConvertTo-PlainHashtable $InputObject[$k] }
        return $h
    }
    if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
        $h = [ordered]@{}
        foreach ($p in $InputObject.PSObject.Properties) { $h[$p.Name] = ConvertTo-PlainHashtable $p.Value }
        return $h
    }
    if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) {
        $a = @()
        foreach ($i in $InputObject) { $a += , (ConvertTo-PlainHashtable $i) }
        return , $a
    }
    return $InputObject
}

function Get-SettingsObject {
    if (-not (Test-Path -LiteralPath $SettingsPath)) { return [ordered]@{} }
    $raw = Get-Content -LiteralPath $SettingsPath -Raw
    if (-not $raw -or -not $raw.Trim()) { return [ordered]@{} }
    try {
        return ConvertTo-PlainHashtable ($raw | ConvertFrom-Json)
    } catch {
        throw "Plik $SettingsPath nie jest poprawnym JSON-em. Popraw go albo zmień nazwę i uruchom instalator ponownie."
    }
}

function Save-Settings($obj) {
    if (Test-Path -LiteralPath $SettingsPath) {
        $bak = "$SettingsPath.bak-" + (Get-Date -Format 'yyyyMMdd-HHmmss')
        Copy-Item -LiteralPath $SettingsPath -Destination $bak -Force
        Write-Host "  backup: $bak" -ForegroundColor DarkGray
    }
    ($obj | ConvertTo-Json -Depth 25) | Set-Content -LiteralPath $SettingsPath -Encoding UTF8 -Force
}

function Remove-OurHooks($settings) {
    if (-not $settings.Contains('hooks') -or $null -eq $settings['hooks']) { return $settings }
    $hooks = $settings['hooks']
    foreach ($evt in @($hooks.Keys)) {
        $kept = @()
        foreach ($entry in @($hooks[$evt])) {
            $isOurs = $false
            if ($entry -and $entry.Contains('hooks')) {
                foreach ($h in @($entry['hooks'])) {
                    $txt = ''
                    if ($h -and $h.Contains('command')) { $txt += [string]$h['command'] }
                    if ($h -and $h.Contains('args')) { $txt += ' ' + (@($h['args']) -join ' ') }
                    if ($txt -like "*$Marker*") { $isOurs = $true }
                }
            }
            if (-not $isOurs) { $kept += , $entry }
        }
        if ($kept.Count -eq 0) { $hooks.Remove($evt) } else { $hooks[$evt] = $kept }
    }
    if ($hooks.Keys.Count -eq 0) { $settings.Remove('hooks') }
    return $settings
}

# --------------------------------------------------------------------------- #
#  Deinstalacja                                                                #
# --------------------------------------------------------------------------- #
if ($Uninstall) {
    Write-Host 'Usuwanie nakładki statusu Claude Code...' -ForegroundColor Cyan
    Get-Process powershell, pwsh -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle -eq 'Claude Status' } |
        ForEach-Object { $_.CloseMainWindow() | Out-Null }

    $s = Get-SettingsObject
    $s = Remove-OurHooks $s
    Save-Settings $s
    Write-Host '  hooki usunięte z settings.json' -ForegroundColor Green

    if (Test-Path -LiteralPath $StartupLnk) { Remove-Item -LiteralPath $StartupLnk -Force }
    if (Test-Path -LiteralPath $StartupSrvLnk) { Remove-Item -LiteralPath $StartupSrvLnk -Force }
    if (Test-Path -LiteralPath $Dest) { Remove-Item -LiteralPath $Dest -Recurse -Force }
    Write-Host '  pliki usunięte' -ForegroundColor Green
    Write-Host '  (konfiguracja powiadomień w .claude\status-notify.config.json została' -ForegroundColor DarkGray
    Write-Host '   zachowana — skasuj ją ręcznie, jeśli chcesz)' -ForegroundColor DarkGray
    Write-Host 'Gotowe. Zamknij i otwórz ponownie sesje Claude Code.' -ForegroundColor Cyan
    return
}

# --------------------------------------------------------------------------- #
#  Instalacja                                                                  #
# --------------------------------------------------------------------------- #
Write-Host 'Instalacja nakładki statusu Claude Code' -ForegroundColor Cyan

$src = $PSScriptRoot
if (-not $src) { $src = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $src) { $src = (Get-Location).Path }
foreach ($f in @('claude-status-hook.ps1', 'ClaudeStatusOverlay.ps1', 'Start-Overlay.vbs')) {
    if (-not (Test-Path -LiteralPath (Join-Path $src $f))) {
        throw "Brakuje pliku $f obok instalatora. Rozpakuj cały ZIP do jednego katalogu."
    }
}

$FilesToCopy = @(
    'claude-status-hook.ps1',
    'ClaudeStatusOverlay.ps1',
    'ClaudeStatusState.ps1',
    'ClaudeStatusUsage.ps1',
    'ClaudeStatusNotify.ps1',
    'ClaudeStatusServer.ps1',
    'Setup-Notifications.ps1',
    'Start-Overlay.vbs',
    'Start-Server.vbs',
    'Install-ClaudeStatusOverlay.ps1',
    'Demo-ClaudeStatus.ps1',
    'Demo.cmd',
    'Serwer.cmd',
    'README.md'
)

New-Item -ItemType Directory -Path $Dest -Force | Out-Null
foreach ($f in $FilesToCopy) {
    $p = Join-Path $src $f
    if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $Dest -Force }
}
New-Item -ItemType Directory -Path (Join-Path $ClaudeDir 'status') -Force | Out-Null
Write-Host "  pliki: $Dest" -ForegroundColor Green

# --- hooki -----------------------------------------------------------------
$settings = Get-SettingsObject
$settings = Remove-OurHooks $settings
if (-not $settings.Contains('hooks') -or $null -eq $settings['hooks']) { $settings['hooks'] = [ordered]@{} }

foreach ($evt in $EventMap.Keys) {
    $state = $EventMap[$evt]
    $handler = [ordered]@{
        type    = 'command'
        command = 'powershell.exe'
        args    = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $HookPath, '-State', $state)
        async   = $true
        timeout = 15
    }
    $entry = [ordered]@{ hooks = @($handler) }

    $existing = @()
    if ($settings['hooks'].Contains($evt) -and $null -ne $settings['hooks'][$evt]) {
        $existing = @($settings['hooks'][$evt])
    }
    $settings['hooks'][$evt] = @($existing) + @($entry)
}

Save-Settings $settings
Write-Host "  hooki dopisane do: $SettingsPath" -ForegroundColor Green
Write-Host ('  zdarzenia: ' + ($EventMap.Keys -join ', ')) -ForegroundColor DarkGray

# --- autostart -------------------------------------------------------------
if ($Autostart) {
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($StartupLnk)
    $sc.TargetPath       = 'wscript.exe'
    $sc.Arguments        = '"' + $VbsPath + '"'
    $sc.WorkingDirectory = $Dest
    $sc.Description      = 'Claude Code status overlay'
    $sc.Save()
    Write-Host "  autostart: $StartupLnk" -ForegroundColor Green

    if ($WithServer) {
        $sc2 = $ws.CreateShortcut($StartupSrvLnk)
        $sc2.TargetPath       = 'wscript.exe'
        $sc2.Arguments        = '"' + $ServerVbs + '"'
        $sc2.WorkingDirectory = $Dest
        $sc2.Description      = 'Claude Code status — serwer sieciowy'
        $sc2.Save()
        Write-Host "  autostart serwera: $StartupSrvLnk" -ForegroundColor Green
    }
}

# --- zapora ----------------------------------------------------------------
if ($OpenFirewall) {
    $isAdmin = $false
    try {
        $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $isAdmin = (New-Object System.Security.Principal.WindowsPrincipal($id)).IsInRole(
            [System.Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch { }

    if (-not $isAdmin) {
        Write-Host '  UWAGA: reguła zapory wymaga PowerShella uruchomionego jako administrator.' -ForegroundColor Yellow
        Write-Host ('  Wykonaj ręcznie:  netsh advfirewall firewall add rule name="Claude Status" dir=in action=allow protocol=TCP localport={0}' -f $Port) -ForegroundColor DarkGray
    } else {
        try {
            & netsh advfirewall firewall delete rule name="Claude Status" 2>&1 | Out-Null
            & netsh advfirewall firewall add rule name="Claude Status" dir=in action=allow protocol=TCP localport=$Port 2>&1 | Out-Null
            Write-Host "  reguła zapory dodana dla portu $Port" -ForegroundColor Green
        } catch {
            Write-Host '  nie udało się dodać reguły zapory — dodaj ją ręcznie' -ForegroundColor Yellow
        }
    }
}

# --- start -----------------------------------------------------------------
if (-not $NoStart) {
    Start-Process -FilePath 'wscript.exe' -ArgumentList ('"' + $VbsPath + '"') | Out-Null
    Write-Host '  nakładka uruchomiona (lewy górny róg ekranu — przeciągnij gdzie chcesz)' -ForegroundColor Green

    if ($WithServer) {
        Start-Process -FilePath 'wscript.exe' -ArgumentList ('"' + $ServerVbs + '"') | Out-Null
        $ip = $null
        try {
            $ip = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
                   Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
                   Select-Object -First 1 -ExpandProperty IPAddress)
        } catch { }
        if ($ip) {
            Write-Host ("  serwer podglądu: http://{0}:{1}/  (otwórz na telefonie)" -f $ip, $Port) -ForegroundColor Green
        } else {
            Write-Host ("  serwer podglądu działa na porcie {0}" -f $Port) -ForegroundColor Green
        }
    }
}

Write-Host ''
Write-Host 'Gotowe. Zamknij i otwórz ponownie sesje Claude Code w VS Code,' -ForegroundColor Cyan
Write-Host 'żeby załadowały nowe hooki.' -ForegroundColor Cyan
Write-Host ''
Write-Host 'Powiadomienia na telefon:  .\Setup-Notifications.ps1' -ForegroundColor Cyan
