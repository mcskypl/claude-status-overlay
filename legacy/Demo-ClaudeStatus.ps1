<#
    Demo-ClaudeStatus.ps1
    ---------------------
    Symuluje kilka równoległych sesji Claude Code, żeby pokazać, jak zachowuje
    się nakładka — bez uruchamiania prawdziwego Claude'a.

    Zapisuje takie same pliki stanu jak hook, tylko z prefiksem "demo-",
    więc niczego nie miesza z prawdziwymi sesjami i sprząta po sobie.

    Użycie:
        powershell -ExecutionPolicy Bypass -File .\Demo-ClaudeStatus.ps1
        powershell -ExecutionPolicy Bypass -File .\Demo-ClaudeStatus.ps1 -Speed 2
        powershell -ExecutionPolicy Bypass -File .\Demo-ClaudeStatus.ps1 -Once
        powershell -ExecutionPolicy Bypass -File .\Demo-ClaudeStatus.ps1 -Cleanup

    Zatrzymanie: dowolny klawisz (albo Ctrl+C).
#>

[CmdletBinding()]
param(
    # mnożnik tempa: 2 = dwa razy szybciej, 0.5 = dwa razy wolniej
    [double]$Speed = 1.0,

    # jeden przebieg zamiast pętli
    [switch]$Once,

    # tylko posprzątaj pliki demo i wyjdź
    [switch]$Cleanup,

    # nie próbuj uruchamiać nakładki
    [switch]$NoOverlay,

    # wysyłaj też prawdziwe powiadomienia push (jeśli skonfigurowane)
    [switch]$Notify,

    # uruchom też serwer podglądu w sieci lokalnej
    [switch]$WithServer
)

$ErrorActionPreference = 'Stop'

$StatusDir = Join-Path $env:USERPROFILE '.claude\status'
$Prefix    = 'demo-'

if (-not (Test-Path -LiteralPath $StatusDir)) {
    New-Item -ItemType Directory -Path $StatusDir -Force | Out-Null
}

function Remove-DemoFiles {
    Get-ChildItem -LiteralPath $StatusDir -Filter "$Prefix*.json" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

if ($Cleanup) {
    Remove-DemoFiles
    Write-Host 'Pliki demo usunięte.' -ForegroundColor Green
    return
}

# --------------------------------------------------------------------------- #
#  Sesje w demie                                                               #
# --------------------------------------------------------------------------- #
$Sessions = @{
    'A' = @{ Id = $Prefix + 'optimes';   Project = 'OptiMES';                Cwd = 'C:\repo\OptiMES' }
    'B' = @{ Id = $Prefix + 'syncos';    Project = 'SyncOS';                 Cwd = 'C:\repo\SyncOS' }
    'C' = @{ Id = $Prefix + 'slingshot'; Project = 'Slingshot.WebApplication'; Cwd = 'C:\repo\Slingshot.WebApplication' }
}

function Set-DemoState {
    param(
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$State,
        [string]$Note = ''
    )
    $s = $Sessions[$Key]
    $target = Join-Path $StatusDir ($s.Id + '.json')

    if ($State -eq 'end') {
        Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        return
    }

    $payload = [ordered]@{
        session_id = $s.Id
        state      = $State
        project    = $s.Project
        cwd        = $s.Cwd
        note       = $Note
        ts         = (Get-Date).ToString('o')
    }
    $tmp = $target + '.tmp'
    ($payload | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $tmp -Encoding UTF8 -Force
    Move-Item -LiteralPath $tmp -Destination $target -Force

    if ($script:NotifyCfg -and @('attention', 'error', 'done') -contains $State) {
        try {
            Send-ClaudeNotification -State $State -Project $s.Project -Note $Note -Config $script:NotifyCfg
        } catch { }
    }
}

$StateColor = @{
    'idle'      = 'DarkGray'
    'working'   = 'Yellow'
    'done'      = 'Green'
    'attention' = 'Red'
    'error'     = 'Magenta'
    'end'       = 'DarkGray'
}

# --------------------------------------------------------------------------- #
#  Scenariusz                                                                  #
# --------------------------------------------------------------------------- #
# Wait = ile sekund odczekać PRZED wykonaniem kroku
$Script = @(
    @{ Wait = 0;   Key = 'A'; State = 'idle';      Say = 'Otwierasz sesję w projekcie OptiMES' }
    @{ Wait = 1.5; Key = 'A'; State = 'working';   Say = 'Wysyłasz prompt — Claude rusza do pracy' }
    @{ Wait = 2.5; Key = 'B'; State = 'idle';      Say = 'Otwierasz drugie okno: SyncOS' }
    @{ Wait = 1.5; Key = 'B'; State = 'working';   Say = 'I tam też dajesz zadanie' }
    @{ Wait = 4;   Key = 'A'; State = 'attention'; Say = 'OptiMES prosi o zgodę na uruchomienie testów'; Note = 'Claude needs your permission to use Bash' }
    @{ Wait = 5;   Key = 'A'; State = 'working';   Say = 'Klikasz "Allow" — praca leci dalej' }
    @{ Wait = 2;   Key = 'C'; State = 'working';   Say = 'Trzecia sesja: Slingshot.WebApplication' }
    @{ Wait = 3.5; Key = 'B'; State = 'done';      Say = 'SyncOS skończył — zielona kropka' }
    @{ Wait = 2.5; Key = 'A'; State = 'done';      Say = 'OptiMES też gotowy' }
    @{ Wait = 3;   Key = 'C'; State = 'attention'; Say = 'Slingshot pyta o zgodę na edycję pliku'; Note = 'Claude needs your permission to edit Default.aspx.cs' }
    @{ Wait = 4;   Key = 'C'; State = 'working';   Say = 'Zgoda udzielona' }
    @{ Wait = 3;   Key = 'C'; State = 'error';     Say = 'Slingshot: tura przerwana błędem API' }
    @{ Wait = 3;   Key = 'C'; State = 'working';   Say = 'Ponawiasz — znowu pracuje' }
    @{ Wait = 3;   Key = 'C'; State = 'done';      Say = 'Slingshot gotowy' }
    @{ Wait = 4;   Key = 'B'; State = 'end';       Say = 'Zamykasz sesję SyncOS — wiersz znika' }
    @{ Wait = 1.5; Key = 'C'; State = 'end';       Say = 'Zamykasz Slingshot' }
    @{ Wait = 1.5; Key = 'A'; State = 'idle';      Say = 'OptiMES wraca do bezczynności' }
    @{ Wait = 3;   Key = 'A'; State = 'end';       Say = 'Koniec przebiegu' }
)

# --------------------------------------------------------------------------- #
#  Przerywalne czekanie                                                        #
# --------------------------------------------------------------------------- #
$script:Stop = $false

function Wait-DemoStep([double]$Seconds) {
    $ms = [int](($Seconds / $Speed) * 1000)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) {
        try {
            if ([Console]::KeyAvailable) {
                [void][Console]::ReadKey($true)
                $script:Stop = $true
                return
            }
        } catch { }   # brak konsoli interaktywnej — po prostu czekamy dalej
        Start-Sleep -Milliseconds 60
    }
}

# --------------------------------------------------------------------------- #
#  Start                                                                       #
# --------------------------------------------------------------------------- #
Remove-DemoFiles

$script:NotifyCfg = $null
if ($Notify) {
    $lib = Join-Path $PSScriptRoot 'ClaudeStatusNotify.ps1'
    if (Test-Path -LiteralPath $lib) {
        . $lib
        $script:NotifyCfg = Get-NotifyConfig
    }
    if (-not $script:NotifyCfg) {
        Write-Host '  (powiadomienia push nie są skonfigurowane — uruchom Setup-Notifications.ps1)' -ForegroundColor DarkYellow
    }
}

if ($WithServer) {
    $svbs = Join-Path $PSScriptRoot 'Start-Server.vbs'
    if (Test-Path -LiteralPath $svbs) {
        Start-Process -FilePath 'wscript.exe' -ArgumentList ('"' + $svbs + '"') -ErrorAction SilentlyContinue | Out-Null
    }
}

if (-not $NoOverlay) {
    $vbs = Join-Path $PSScriptRoot 'Start-Overlay.vbs'
    if (Test-Path -LiteralPath $vbs) {
        # nakładka ma mutex, druga instancja sama się zamknie
        Start-Process -FilePath 'wscript.exe' -ArgumentList ('"' + $vbs + '"') -ErrorAction SilentlyContinue | Out-Null
        Start-Sleep -Milliseconds 800
    }
}

Write-Host ''
Write-Host '  DEMO — Claude Status Overlay' -ForegroundColor Cyan
Write-Host '  ----------------------------' -ForegroundColor Cyan
Write-Host '  Symulacja trzech równoległych sesji Claude Code.'
Write-Host '  Patrz na pasek: szary = bezczynny, żółty = pracuje,'
Write-Host '  zielony = gotowe, czerwony (miga) = czeka na Ciebie, fioletowy = błąd.'
Write-Host ''
Write-Host "  Tempo: x$Speed   |   Zatrzymanie: dowolny klawisz" -ForegroundColor DarkGray
Write-Host ''

try {
    do {
        foreach ($step in $Script) {
            Wait-DemoStep ([double]$step.Wait)
            if ($script:Stop) { break }

            $note = ''
            if ($step.ContainsKey('Note')) { $note = [string]$step.Note }
            Set-DemoState -Key $step.Key -State $step.State -Note $note

            $col = 'Gray'
            if ($StateColor.ContainsKey($step.State)) { $col = $StateColor[$step.State] }
            $stamp = (Get-Date).ToString('HH:mm:ss')
            Write-Host ("  {0}  " -f $stamp) -NoNewline -ForegroundColor DarkGray
            Write-Host ("{0,-11}" -f $step.State) -NoNewline -ForegroundColor $col
            Write-Host ("{0,-28}" -f $Sessions[$step.Key].Project) -NoNewline -ForegroundColor White
            Write-Host $step.Say -ForegroundColor DarkGray
        }

        if ($script:Stop) { break }
        if (-not $Once) {
            Write-Host ''
            Write-Host '  --- powtórka od początku ---' -ForegroundColor DarkCyan
            Write-Host ''
            Wait-DemoStep 2
        }
    } while (-not $Once -and -not $script:Stop)
}
finally {
    Remove-DemoFiles
    Write-Host ''
    Write-Host '  Demo zakończone, pliki symulowanych sesji usunięte.' -ForegroundColor Green
    Write-Host ''
}
