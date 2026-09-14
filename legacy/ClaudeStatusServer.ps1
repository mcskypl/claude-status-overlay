<#
    ClaudeStatusServer.ps1
    ----------------------
    Mały serwer HTTP w sieci lokalnej — podgląd stanu sesji Claude Code
    z telefonu albo z drugiego komputera.

        http://<ip-komputera>:8787/

    Oparty na TcpListenerze, więc NIE wymaga uprawnień administratora
    (HttpListener wymagałby rezerwacji URL-a przez netsh).

    Użycie:
        powershell -ExecutionPolicy Bypass -File .\ClaudeStatusServer.ps1
        powershell -ExecutionPolicy Bypass -File .\ClaudeStatusServer.ps1 -Port 9000
        powershell -ExecutionPolicy Bypass -File .\ClaudeStatusServer.ps1 -LocalOnly

    Zatrzymanie: dowolny klawisz albo Ctrl+C.
#>

[CmdletBinding()]
param(
    [int]$Port = 8787,
    [switch]$LocalOnly,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$StatusDir  = Join-Path $env:USERPROFILE '.claude\status'
$NotifyCfg  = Join-Path $env:USERPROFILE '.claude\status-notify.config.json'

if (-not (Test-Path -LiteralPath $StatusDir)) {
    New-Item -ItemType Directory -Path $StatusDir -Force | Out-Null
}

# --------------------------------------------------------------------------- #
#  Dane                                                                        #
# --------------------------------------------------------------------------- #
$Order = @{ 'attention' = 1; 'error' = 1; 'working' = 2; 'done' = 3; 'idle' = 4 }

# Claude Code nie ma zdarzenia "zgoda udzielona" - po kliknięciu Allow stan
# "attention" wisiałby do końca tury. Wspólna z nakładką biblioteka szuka
# dowodów, że tura leci (rosnący transkrypt, działający proces narzędzia).
$StateLib = Join-Path $PSScriptRoot 'ClaudeStatusState.ps1'
if (Test-Path -LiteralPath $StateLib) {
    . $StateLib
} else {
    function Resolve-LiveState([string]$state, [string]$session, [string]$hinted, [datetime]$ts) { return $state }
}

function Get-StatusJson {
    $rows = @()
    $files = @()
    try { $files = @(Get-ChildItem -LiteralPath $StatusDir -Filter '*.json' -File -ErrorAction Stop) } catch { }

    $now = Get-Date
    foreach ($f in $files) {
        $d = $null
        try { $d = Get-Content -LiteralPath $f.FullName -Raw -ErrorAction Stop | ConvertFrom-Json } catch { continue }
        if (-not $d) { continue }

        $ts = $f.LastWriteTime
        if ($d.ts) { try { $ts = [datetime]::Parse([string]$d.ts) } catch { } }
        if (($now - $ts).TotalHours -gt 24) { continue }

        $st = Resolve-LiveState ([string]$d.state) ([string]$d.session_id) ([string]$d.transcript) $ts
        $o = 5
        if ($Order.ContainsKey($st)) { $o = $Order[$st] }

        $rows += , [pscustomobject]@{
            id         = [string]$d.session_id
            project    = [string]$d.project
            state      = $st
            note       = [string]$d.note
            cwd        = [string]$d.cwd
            ageSeconds = [int]($now - $ts).TotalSeconds
            order      = $o
        }
    }

    $rows = @($rows | Sort-Object order, ageSeconds)

    $ntfy = $null
    if (Test-Path -LiteralPath $NotifyCfg) {
        try {
            $c = Get-Content -LiteralPath $NotifyCfg -Raw | ConvertFrom-Json
            if ($c -and $c.topic -and ($null -eq $c.enabled -or $c.enabled)) {
                $srv = 'https://ntfy.sh'
                if ($c.server) { $srv = ([string]$c.server).TrimEnd('/') }
                $ntfy = [ordered]@{ server = $srv; topic = [string]$c.topic; url = ($srv + '/' + [string]$c.topic) }
            }
        } catch { }
    }

    $payload = [ordered]@{
        ok       = $true
        now      = $now.ToString('HH:mm:ss')
        sessions = $rows
        ntfy     = $ntfy
    }
    return ($payload | ConvertTo-Json -Depth 6 -Compress)
}

# --------------------------------------------------------------------------- #
#  Strona                                                                      #
# --------------------------------------------------------------------------- #
$Html = @'
<!doctype html>
<html lang="pl">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="theme-color" content="#141518">
<title>Claude — status sesji</title>
<style>
  :root{
    --bg:#141518; --card:#1e2025; --card2:#25272e; --line:#31343c;
    --fg:#e9ebf0; --muted:#8b909c;
    --idle:#7d828c; --working:#f0b028; --done:#40c870; --attention:#ee4848; --error:#c660dc;
  }
  *{box-sizing:border-box}
  html,body{margin:0;padding:0}
  body{
    background:var(--bg); color:var(--fg);
    font:15px/1.45 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif;
    padding:16px 14px calc(24px + env(safe-area-inset-bottom));
    -webkit-font-smoothing:antialiased;
  }
  header{display:flex;align-items:center;gap:10px;margin-bottom:14px}
  h1{font-size:16px;font-weight:650;margin:0;letter-spacing:.2px}
  .live{margin-left:auto;display:flex;align-items:center;gap:6px;font-size:12px;color:var(--muted)}
  .live i{width:7px;height:7px;border-radius:50%;background:var(--done);display:block}
  .live.off i{background:var(--attention)}

  .banner{
    display:none;background:rgba(238,72,72,.14);border:1px solid rgba(238,72,72,.45);
    color:#ff9b9b;border-radius:12px;padding:11px 13px;margin-bottom:12px;
    font-weight:600;font-size:14px;animation:pulse 1.1s ease-in-out infinite;
  }
  .banner.on{display:block}
  @keyframes pulse{0%,100%{opacity:1}50%{opacity:.55}}

  .card{
    background:var(--card);border:1px solid var(--line);border-radius:13px;
    padding:13px 14px;margin-bottom:9px;display:flex;align-items:flex-start;gap:12px;
    border-left:4px solid var(--idle);
  }
  .card.attention,.card.error{border-left-color:var(--attention);background:var(--card2)}
  .card.error{border-left-color:var(--error)}
  .card.working{border-left-color:var(--working)}
  .card.done{border-left-color:var(--done)}

  .dot{width:11px;height:11px;border-radius:50%;flex:none;margin-top:5px;background:var(--idle)}
  .working .dot{background:var(--working)}
  .done .dot{background:var(--done)}
  .attention .dot{background:var(--attention);animation:pulse .85s ease-in-out infinite}
  .error .dot{background:var(--error);animation:pulse .85s ease-in-out infinite}

  .body{min-width:0;flex:1}
  .proj{font-weight:650;font-size:15px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
  .meta{font-size:12.5px;color:var(--muted);margin-top:2px}
  .state{font-weight:600}
  .working .state{color:var(--working)}
  .done .state{color:var(--done)}
  .attention .state{color:var(--attention)}
  .error .state{color:var(--error)}
  .note{font-size:12.5px;color:var(--muted);margin-top:5px;overflow-wrap:anywhere}
  .age{font-size:12px;color:var(--muted);flex:none;margin-top:3px;font-variant-numeric:tabular-nums}

  .empty{color:var(--muted);text-align:center;padding:36px 10px;font-size:14px}
  footer{margin-top:18px;font-size:12px;color:var(--muted);line-height:1.7}
  footer a{color:#8fb8ff}
  button{
    background:var(--card2);color:var(--fg);border:1px solid var(--line);
    border-radius:9px;padding:7px 12px;font-size:12.5px;font-family:inherit;cursor:pointer;
  }
  button.on{border-color:var(--done);color:var(--done)}
</style>
</head>
<body>

<header>
  <h1>Claude Code — status</h1>
  <span class="live" id="live"><i></i><span id="clock">…</span></span>
</header>

<div class="banner" id="banner"></div>
<div id="list"><div class="empty">Ładowanie…</div></div>

<footer>
  <button id="snd">🔔 Dźwięk w przeglądarce: wył.</button>
  <div id="ntfy" style="margin-top:10px"></div>
</footer>

<script>
(function(){
  var LABEL = {idle:'bezczynny', working:'pracuje', done:'gotowe', attention:'czeka na Ciebie', error:'błąd'};
  var list = document.getElementById('list');
  var banner = document.getElementById('banner');
  var live = document.getElementById('live');
  var clock = document.getElementById('clock');
  var ntfyBox = document.getElementById('ntfy');
  var sndBtn = document.getElementById('snd');
  var sound = false, ac = null, prev = {}, first = true;

  sndBtn.onclick = function(){
    sound = !sound;
    sndBtn.className = sound ? 'on' : '';
    sndBtn.textContent = '🔔 Dźwięk w przeglądarce: ' + (sound ? 'wł.' : 'wył.');
    if (sound) { beep(660, 0.08); }
  };

  function beep(freq, dur){
    try{
      if(!ac){ ac = new (window.AudioContext || window.webkitAudioContext)(); }
      var o = ac.createOscillator(), g = ac.createGain();
      o.type='sine'; o.frequency.value=freq;
      g.gain.setValueAtTime(0.001, ac.currentTime);
      g.gain.exponentialRampToValueAtTime(0.22, ac.currentTime+0.01);
      g.gain.exponentialRampToValueAtTime(0.001, ac.currentTime+dur);
      o.connect(g); g.connect(ac.destination);
      o.start(); o.stop(ac.currentTime+dur+0.02);
    }catch(e){}
  }

  function age(s){
    if (s < 60) return s + ' s';
    if (s < 3600) return Math.floor(s/60) + ' min';
    return Math.floor(s/3600) + ' h';
  }

  function render(d){
    clock.textContent = d.now;
    live.className = 'live';

    var s = d.sessions || [];
    if (!s.length){
      list.innerHTML = '<div class="empty">Brak aktywnych sesji Claude Code</div>';
    } else {
      var h = '';
      for (var i=0;i<s.length;i++){
        var r = s[i];
        var lbl = LABEL[r.state] || r.state;
        h += '<div class="card ' + r.state + '">'
           +   '<span class="dot"></span>'
           +   '<div class="body">'
           +     '<div class="proj">' + esc(r.project) + '</div>'
           +     '<div class="meta"><span class="state">' + esc(lbl) + '</span></div>'
           +     (r.note ? '<div class="note">' + esc(r.note) + '</div>' : '')
           +   '</div>'
           +   '<span class="age">' + age(r.ageSeconds) + '</span>'
           + '</div>';
      }
      list.innerHTML = h;
    }

    var need = s.filter(function(r){ return r.state === 'attention' || r.state === 'error'; });
    if (need.length){
      banner.className = 'banner on';
      banner.textContent = need.length === 1
        ? need[0].project + ' — Claude czeka na Ciebie'
        : need.length + ' sesje czekają na Ciebie';
      document.title = '(!) Claude — czeka';
    } else {
      banner.className = 'banner';
      document.title = 'Claude — status sesji';
    }

    if (sound && !first){
      for (var j=0;j<s.length;j++){
        var r2 = s[j];
        if (prev[r2.id] !== r2.state){
          if (r2.state === 'attention' || r2.state === 'error'){ beep(880, 0.16); setTimeout(function(){beep(880,0.16);}, 220); }
          else if (r2.state === 'done'){ beep(560, 0.12); }
        }
      }
    }
    prev = {}; s.forEach(function(r){ prev[r.id] = r.state; });
    first = false;

    if (d.ntfy){
      ntfyBox.innerHTML = 'Powiadomienia push: temat <b>' + esc(d.ntfy.topic) + '</b><br>'
        + 'Subskrybuj w aplikacji ntfy albo otwórz <a href="' + esc(d.ntfy.url) + '" target="_blank" rel="noreferrer">' + esc(d.ntfy.url) + '</a>';
    } else {
      ntfyBox.textContent = 'Powiadomienia push nie są skonfigurowane (uruchom Setup-Notifications.ps1 na komputerze).';
    }
  }

  function esc(t){
    return String(t == null ? '' : t)
      .replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
  }

  function tick(){
    fetch('/api/status', {cache:'no-store'})
      .then(function(r){ return r.json(); })
      .then(render)
      .catch(function(){
        live.className = 'live off';
        clock.textContent = 'brak połączenia';
      });
  }

  tick();
  setInterval(tick, 2000);
  document.addEventListener('visibilitychange', function(){ if(!document.hidden) tick(); });
})();
</script>
</body>
</html>
'@

$HtmlBytes = [System.Text.Encoding]::UTF8.GetBytes($Html)

# --------------------------------------------------------------------------- #
#  Mini-HTTP                                                                   #
# --------------------------------------------------------------------------- #
function Write-HttpResponse {
    param(
        [System.IO.Stream]$Stream,
        [int]$Code = 200,
        [string]$Status = 'OK',
        [string]$ContentType = 'text/plain; charset=utf-8',
        [byte[]]$Body
    )
    if ($null -eq $Body) { $Body = New-Object byte[] 0 }
    $head = "HTTP/1.1 $Code $Status`r`n" +
            "Content-Type: $ContentType`r`n" +
            "Content-Length: $($Body.Length)`r`n" +
            "Cache-Control: no-store`r`n" +
            "Connection: close`r`n`r`n"
    $hb = [System.Text.Encoding]::ASCII.GetBytes($head)
    $Stream.Write($hb, 0, $hb.Length)
    if ($Body.Length -gt 0) { $Stream.Write($Body, 0, $Body.Length) }
    $Stream.Flush()
}

function Get-LocalAddresses {
    $ips = @()
    try {
        $ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
                 Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
                 Select-Object -ExpandProperty IPAddress)
    } catch {
        try {
            $ips = @([System.Net.Dns]::GetHostAddresses([System.Net.Dns]::GetHostName()) |
                     Where-Object { $_.AddressFamily -eq 'InterNetwork' -and $_.ToString() -notlike '127.*' } |
                     ForEach-Object { $_.ToString() })
        } catch { }
    }
    return $ips
}

$bindAddr = [System.Net.IPAddress]::Any
if ($LocalOnly) { $bindAddr = [System.Net.IPAddress]::Loopback }

$listener = New-Object System.Net.Sockets.TcpListener($bindAddr, $Port)
try {
    $listener.Start()
} catch {
    Write-Host ''
    Write-Host "  Nie udało się zająć portu $Port." -ForegroundColor Red
    Write-Host '  Prawdopodobnie serwer już działa albo port jest zajęty — spróbuj -Port 8788.' -ForegroundColor DarkGray
    Write-Host ''
    return
}

if (-not $Quiet) {
    Write-Host ''
    Write-Host '  Claude Status — serwer sieciowy' -ForegroundColor Cyan
    Write-Host '  -------------------------------' -ForegroundColor Cyan
    if ($LocalOnly) {
        Write-Host "  http://localhost:$Port/" -ForegroundColor Green
    } else {
        foreach ($ip in Get-LocalAddresses) {
            Write-Host ("  http://{0}:{1}/" -f $ip, $Port) -ForegroundColor Green
        }
        Write-Host "  http://localhost:$Port/" -ForegroundColor DarkGray
    }
    Write-Host ''
    Write-Host '  Otwórz ten adres na telefonie w tej samej sieci Wi-Fi.' -ForegroundColor DarkGray
    Write-Host '  Jeśli strona się nie ładuje, przepuść port w zaporze Windows:' -ForegroundColor DarkGray
    Write-Host ("    netsh advfirewall firewall add rule name=""Claude Status"" dir=in action=allow protocol=TCP localport={0}" -f $Port) -ForegroundColor DarkGray
    Write-Host '  (PowerShell jako administrator, jednorazowo)' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host '  Zatrzymanie: dowolny klawisz' -ForegroundColor DarkGray
    Write-Host ''
}

$stop = $false
try {
    while (-not $stop) {
        if (-not $listener.Pending()) {
            try {
                if ([Console]::KeyAvailable) { [void][Console]::ReadKey($true); $stop = $true; break }
            } catch { }
            Start-Sleep -Milliseconds 40
            continue
        }

        $client = $null
        try {
            $client = $listener.AcceptTcpClient()
            $client.ReceiveTimeout = 4000
            $client.SendTimeout    = 4000
            $stream = $client.GetStream()

            $buf  = New-Object byte[] 4096
            $read = $stream.Read($buf, 0, $buf.Length)
            if ($read -le 0) { continue }
            $req = [System.Text.Encoding]::ASCII.GetString($buf, 0, $read)

            $firstLine = ($req -split "`r`n")[0]
            $parts = $firstLine -split ' '
            $path = '/'
            if ($parts.Count -ge 2) { $path = $parts[1] }
            $path = ($path -split '\?')[0]

            switch -Regex ($path) {
                '^/api/status/?$' {
                    $json = Get-StatusJson
                    Write-HttpResponse -Stream $stream -ContentType 'application/json; charset=utf-8' `
                        -Body ([System.Text.Encoding]::UTF8.GetBytes($json))
                    break
                }
                '^/(index\.html)?$' {
                    Write-HttpResponse -Stream $stream -ContentType 'text/html; charset=utf-8' -Body $HtmlBytes
                    break
                }
                '^/favicon\.ico$' {
                    Write-HttpResponse -Stream $stream -Code 204 -Status 'No Content' -Body (New-Object byte[] 0)
                    break
                }
                default {
                    Write-HttpResponse -Stream $stream -Code 404 -Status 'Not Found' `
                        -Body ([System.Text.Encoding]::UTF8.GetBytes('404'))
                    break
                }
            }
        } catch {
            # pojedyncze zerwane połączenie nie może położyć serwera
        } finally {
            if ($client) { try { $client.Close() } catch { } }
        }
    }
}
finally {
    try { $listener.Stop() } catch { }
    if (-not $Quiet) {
        Write-Host ''
        Write-Host '  Serwer zatrzymany.' -ForegroundColor Green
        Write-Host ''
    }
}
