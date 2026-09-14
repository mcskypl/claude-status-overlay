<#
    ClaudeStatusNotify.ps1
    ----------------------
    Wspólna logika wysyłania powiadomień push (ntfy).
    Dot-source'owany przez claude-status-hook.ps1 i Demo-ClaudeStatus.ps1.

    Konfiguracja: %USERPROFILE%\.claude\status-notify.config.json
#>

function Get-NotifyConfig {
    $path = Join-Path $env:USERPROFILE '.claude\status-notify.config.json'
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try {
        $c = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    } catch { return $null }
    if (-not $c) { return $null }
    if ($null -ne $c.enabled -and -not $c.enabled) { return $null }
    if (-not $c.topic) { return $null }
    if (-not $c.server) { $c | Add-Member -NotePropertyName server -NotePropertyValue 'https://ntfy.sh' -Force }
    return $c
}

function Send-ClaudeNotification {
    param(
        [Parameter(Mandatory = $true)][string]$State,
        [Parameter(Mandatory = $true)][string]$Project,
        [string]$Note = '',
        [int]$ElapsedSeconds = 0,
        $Config = $null
    )

    if (-not $Config) { $Config = Get-NotifyConfig }
    if (-not $Config) { return }

    $titles = @{
        'attention' = 'czeka na Ciebie'
        'error'     = 'błąd — tura przerwana'
        'done'      = 'gotowe'
        'working'   = 'pracuje'
        'idle'      = 'bezczynny'
    }
    $tags = @{
        'attention' = @('raised_hand')
        'error'     = @('x')
        'done'      = @('white_check_mark')
        'working'   = @('hourglass_flowing_sand')
        'idle'      = @('zzz')
    }
    $prio = @{
        'attention' = 4
        'error'     = 4
        'done'      = 3
        'working'   = 2
        'idle'      = 2
    }

    $suffix = 'zmiana stanu'
    if ($titles.ContainsKey($State)) { $suffix = $titles[$State] }

    $body = $Note
    if (-not $body) {
        switch ($State) {
            'attention' { $body = 'Claude czeka na Twoją decyzję.' }
            'error'     { $body = 'Tura zakończona błędem — trzeba ponowić.' }
            'done'      { $body = 'Odpowiedź gotowa.' }
            default     { $body = 'Zmiana stanu sesji.' }
        }
    }
    if ($State -eq 'done' -and $ElapsedSeconds -gt 0) {
        if ($ElapsedSeconds -lt 90) {
            $body += (' (praca: {0} s)' -f $ElapsedSeconds)
        } else {
            $body += (' (praca: {0} min)' -f [int]([Math]::Round($ElapsedSeconds / 60)))
        }
    }

    $payload = [ordered]@{
        topic    = [string]$Config.topic
        title    = ('{0} — {1}' -f $Project, $suffix)
        message  = $body
        priority = 3
        tags     = @('bell')
    }
    if ($prio.ContainsKey($State)) { $payload['priority'] = $prio[$State] }
    if ($tags.ContainsKey($State)) { $payload['tags'] = $tags[$State] }
    if ($Config.webUrl) {
        $payload['click'] = [string]$Config.webUrl
    }

    $timeout = 6
    if ($Config.timeoutSeconds) { $timeout = [int]$Config.timeoutSeconds }

    $json = $payload | ConvertTo-Json -Depth 5 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)

    try {
        $server = ([string]$Config.server).TrimEnd('/')
        $req = [System.Net.HttpWebRequest]::Create($server + '/')
        $req.Method      = 'POST'
        $req.ContentType = 'application/json; charset=utf-8'
        $req.Timeout     = $timeout * 1000
        $req.ReadWriteTimeout = $timeout * 1000
        $req.ContentLength = $bytes.Length
        if ($Config.token) {
            $req.Headers.Add('Authorization', 'Bearer ' + [string]$Config.token)
        }
        $st = $req.GetRequestStream()
        $st.Write($bytes, 0, $bytes.Length)
        $st.Close()
        $resp = $req.GetResponse()
        $resp.Close()
    } catch {
        # powiadomienia nie mogą wywrócić hooka — cisza
    }
}
