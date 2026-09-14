<#
    ClaudeStatusUsage.ps1
    ---------------------
    Odczyt limitów użycia (okno 5 h i 7 dni) z pliku, który Claude Code sam
    utrzymuje: ~/.claude.json -> cachedUsageUtilization.

      "cachedUsageUtilization": {
        "fetchedAtMs": 1789117228324,
        "utilization": {
          "five_hour": { "utilization": 0,  "resets_at": "2026-09-11T12:30:00+00:00" },
          "seven_day": { "utilization": 20, "resets_at": "2026-09-17T08:00:00+00:00" }
        }
      }

    "utilization" to procent ZUŻYCIA okna (0-100), "resets_at" to moment
    wyzerowania licznika (ISO, ze strefą). Claude Code odświeża ten wpis w
    trakcie pracy, więc nakładka nie musi nic wysyłać do sieci ani dotykać
    tokenów - tylko czyta gotowy plik.

    Get-ClaudeUsage zwraca:
      Ok        - czy mamy sensowne dane
      Stale     - dane starsze niż godzina (Claude Code chwilę nie odświeżał)
      FetchedAt - kiedy Claude Code je pobrał
      Five      - okno 5 h,  Seven - okno 7 dni, każde jako:
                    Used   procent zużyty
                    Free   procent dostępny
                    Resets kiedy się zeruje (albo $null)
#>

$script:CU_Path        = Join-Path $env:USERPROFILE '.claude.json'
$script:CU_MinReadSec  = 10      # jak często wolno w ogóle zerknąć na plik
$script:CU_StaleHours  = 1       # powyżej tego dane są "przykurzone"
$script:CU_MaxAgeHours = 24      # powyżej tego nie pokazujemy nic
$script:CU_ScanChars   = 8000    # tyle znaków za kluczem wystarcza na oba okna

$script:CU_Raw     = $null
$script:CU_LastTry = [datetime]::MinValue
$script:CU_Stamp   = ''

# procenty w pliku są zawsze z kropką - lokalny separator nie może przeszkadzać
function ConvertTo-CUNumber([string]$s) {
    $v = 0.0
    if ([double]::TryParse($s, [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture, [ref]$v)) { return $v }
    return $null
}

function ConvertTo-CUTime([string]$s) {
    if (-not $s) { return $null }
    try {
        return ([datetimeoffset]::Parse($s, [System.Globalization.CultureInfo]::InvariantCulture)).LocalDateTime
    } catch { return $null }
}

# jedno okno limitu; "[^{}]*" trzyma nas w obrębie właściwego obiektu,
# a cudzysłów po nazwie chroni przed trafieniem w seven_day_opus itp.
function Get-CUWindow([string]$seg, [string]$key) {
    $m = [regex]::Match($seg, ('"' + $key + '"\s*:\s*\{(?<body>[^{}]*)\}'))
    if (-not $m.Success) { return $null }
    $body = $m.Groups['body'].Value

    $mu = [regex]::Match($body, '"utilization"\s*:\s*([-+0-9.eE]+)')
    if (-not $mu.Success) { return $null }
    $pct = ConvertTo-CUNumber $mu.Groups[1].Value
    if ($null -eq $pct) { return $null }
    if ($pct -lt 0)   { $pct = 0.0 }
    if ($pct -gt 100) { $pct = 100.0 }

    $res = $null
    $mr = [regex]::Match($body, '"resets_at"\s*:\s*"([^"]+)"')
    if ($mr.Success) { $res = ConvertTo-CUTime $mr.Groups[1].Value }

    return [pscustomobject]@{ Used = $pct; Resets = $res }
}

function Read-ClaudeUsageFile {
    $now = Get-Date
    if (($now - $script:CU_LastTry).TotalSeconds -lt $script:CU_MinReadSec) { return }
    $script:CU_LastTry = $now

    $fi = $null
    try { $fi = Get-Item -LiteralPath $script:CU_Path -ErrorAction Stop } catch { return }

    # plik zmienia się rzadko, a ma kilkadziesiąt kB - czytamy tylko po zmianie
    $stamp = '{0}|{1}' -f $fi.LastWriteTimeUtc.Ticks, $fi.Length
    if ($stamp -eq $script:CU_Stamp -and $script:CU_Raw) { return }

    $txt = $null
    try { $txt = [System.IO.File]::ReadAllText($script:CU_Path) } catch { return }
    $script:CU_Stamp = $stamp

    $i = $txt.IndexOf('"cachedUsageUtilization"')
    if ($i -lt 0) { return }
    $seg = $txt.Substring($i, [Math]::Min($script:CU_ScanChars, $txt.Length - $i))

    $fetched = $null
    $mf = [regex]::Match($seg, '"fetchedAtMs"\s*:\s*(\d+)')
    if ($mf.Success) {
        try { $fetched = [datetimeoffset]::FromUnixTimeMilliseconds([int64]$mf.Groups[1].Value).LocalDateTime } catch { }
    }

    $five  = Get-CUWindow $seg 'five_hour'
    $seven = Get-CUWindow $seg 'seven_day'
    # połowicznie zapisany plik = brak dopasowania; zostajemy przy poprzednich danych
    if (-not $five -and -not $seven) { return }

    $script:CU_Raw = [pscustomobject]@{ FetchedAt = $fetched; Five = $five; Seven = $seven }
}

# okno, którego termin zerowania już minął, liczy od nowa od zera
function Resolve-CUWindow($w, [datetime]$now) {
    if (-not $w) { return $null }
    $used = [double]$w.Used
    $res  = $w.Resets
    if ($res -and $now -gt $res) { $used = 0.0; $res = $null }
    return [pscustomobject]@{ Used = $used; Free = (100.0 - $used); Resets = $res }
}

function Get-ClaudeUsage {
    Read-ClaudeUsageFile

    $empty = [pscustomobject]@{ Ok = $false; Stale = $false; FetchedAt = $null; Five = $null; Seven = $null }
    $raw = $script:CU_Raw
    if (-not $raw) { return $empty }

    $now = Get-Date
    $ageH = 9999.0
    if ($raw.FetchedAt) { $ageH = ($now - $raw.FetchedAt).TotalHours }
    if ($ageH -gt $script:CU_MaxAgeHours) { return $empty }

    $five  = Resolve-CUWindow $raw.Five  $now
    $seven = Resolve-CUWindow $raw.Seven $now
    if (-not $five -and -not $seven) { return $empty }

    return [pscustomobject]@{
        Ok        = $true
        Stale     = ($ageH -gt $script:CU_StaleHours)
        FetchedAt = $raw.FetchedAt
        Five      = $five
        Seven     = $seven
    }
}
