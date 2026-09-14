<#
    ClaudeStatusState.ps1
    ---------------------
    Wspólna odpowiedź na pytanie: "czy zapisany stan jest jeszcze prawdziwy?"
    Plik jest dot-source'owany przez nakładkę i serwer, żeby obie strony
    pokazywały to samo.

    Problem: Claude Code nie ma zdarzenia "zgoda udzielona" ani "odmowa"
    (kolejność to PreToolUse -> PermissionRequest -> pytanie -> narzędzie),
    więc po kliknięciu Allow żaden hook się nie odpala i stan "czeka na Ciebie"
    wisiałby do końca tury. Szukamy więc dowodów, że tura znowu leci:

      1. plik transkryptu sesji urósł po alercie - Claude coś dopisał
         (odpowiedź, wynik narzędzia, także informację o odmowie),
      2. proces narzędzia odpalony po alercie nadal działa - to przypadek
         "zgoda na długie polecenie": build czy prekompilacja trwa minutami
         i w tym czasie do transkryptu nie trafia ani jedna linia.

    Dowód nr 2 opiera się na ~/.claude/sessions/<pid>.json, gdzie Claude Code
    zapisuje mapowanie sesji na swój proces. Procesy narzędzi są jego dziećmi.
#>

$script:CS_ProjectsDir = Join-Path $env:USERPROFILE '.claude\projects'
$script:CS_SessionsDir = Join-Path $env:USERPROFILE '.claude\sessions'

# ile sekund zapasu na kolejność zdarzeń (hook alertu vs zapis transkryptu)
$script:CS_GraceSec = 3
# jak często wolno pytać system o procesy i o mapę sesji
$script:CS_BusyPollMs   = 1500
$script:CS_PidMapAgeSec = 10

# Wyliczanie dzieci procesu przez Toolhelp32. Odpowiednik zapytania WMI
# (Get-CimInstance Win32_Process -Filter ParentProcessId=...) kosztuje 200-350 ms,
# a to jest kilka milisekund - nakładka odświeża się co pół sekundy.
if (-not ('ClaudeStatusProc' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class ClaudeStatusProc {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32 {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr h, ref PROCESSENTRY32 pe);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr h, ref PROCESSENTRY32 pe);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

    // Zwraca bezpośrednie dzieci procesu jako "pid|czasUtworzeniaFILETIME|exe".
    public static string[] Children(int parentPid) {
        var res = new List<string>();
        IntPtr snap = CreateToolhelp32Snapshot(2, 0);   // TH32CS_SNAPPROCESS
        if (snap == new IntPtr(-1)) { return res.ToArray(); }
        try {
            var pe = new PROCESSENTRY32();
            pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
            if (Process32FirstW(snap, ref pe)) {
                do {
                    if (pe.th32ParentProcessID == (uint)parentPid) {
                        long created = 0;
                        IntPtr h = OpenProcess(0x1000, false, pe.th32ProcessID);   // PROCESS_QUERY_LIMITED_INFORMATION
                        if (h != IntPtr.Zero) {
                            long ex, kt, ut;
                            GetProcessTimes(h, out created, out ex, out kt, out ut);
                            CloseHandle(h);
                        }
                        res.Add(pe.th32ProcessID + "|" + created + "|" + pe.szExeFile);
                    }
                } while (Process32NextW(snap, ref pe));
            }
        } finally {
            CloseHandle(snap);
        }
        return res.ToArray();
    }
}
'@
}

# --------------------------------------------------------------------------- #
#  Transkrypt sesji                                                            #
# --------------------------------------------------------------------------- #
$script:CS_TranscriptCache = @{}

function Get-TranscriptPath([string]$session, [string]$hinted) {
    if ($hinted -and (Test-Path -LiteralPath $hinted)) { return $hinted }
    if (-not $session) { return '' }

    $c = $null
    if ($script:CS_TranscriptCache.ContainsKey($session)) { $c = $script:CS_TranscriptCache[$session] }
    if ($c) {
        if ($c.Path -and (Test-Path -LiteralPath $c.Path)) { return $c.Path }
        # nieudane szukanie powtarzamy najwyżej raz na minutę - przeszukanie
        # katalogu projektów jest rekurencyjne, więc nie jest darmowe
        if (((Get-Date) - $c.Checked).TotalSeconds -lt 60) { return $c.Path }
    }

    $found = ''
    try {
        $hit = @(Get-ChildItem -LiteralPath $script:CS_ProjectsDir -Filter ($session + '.jsonl') `
                    -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1)
        if ($hit.Count -gt 0) { $found = $hit[0].FullName }
    } catch { }
    $script:CS_TranscriptCache[$session] = @{ Path = $found; Checked = (Get-Date) }
    return $found
}

# --------------------------------------------------------------------------- #
#  Sesja -> proces Claude Code -> działające narzędzia                         #
# --------------------------------------------------------------------------- #
$script:CS_PidMap   = @{}
$script:CS_PidStamp = [datetime]::MinValue
$script:CS_Busy     = @{}

function Update-SessionPidMap {
    if (((Get-Date) - $script:CS_PidStamp).TotalSeconds -lt $script:CS_PidMapAgeSec) { return }
    $script:CS_PidStamp = Get-Date

    $files = @()
    try { $files = @(Get-ChildItem -LiteralPath $script:CS_SessionsDir -Filter '*.json' -File -ErrorAction Stop) } catch { }

    $map = @{}
    foreach ($f in $files) {
        try {
            $j = Get-Content -LiteralPath $f.FullName -Raw -ErrorAction Stop | ConvertFrom-Json
            if (-not $j.sessionId -or -not $j.pid) { continue }
            $p = Get-Process -Id ([int]$j.pid) -ErrorAction SilentlyContinue
            if (-not $p) { continue }
            # wpisy zostają po zakończonych sesjach, więc upewniamy się, że pod
            # tym numerem siedzi Claude Code, a nie przypadkowy nowy proces
            if ($p.ProcessName -notmatch '^(claude|node)$') { continue }
            $map[[string]$j.sessionId] = [int]$j.pid
        } catch { }
    }
    $script:CS_PidMap = $map
}

function Get-SessionPid([string]$session) {
    Update-SessionPidMap
    if ($session -and $script:CS_PidMap.ContainsKey($session)) { return [int]$script:CS_PidMap[$session] }
    return 0
}

# Czy sesja odpaliła po alercie narzędzie, które nadal działa?
function Test-SessionBusy([string]$session, [datetime]$after) {
    if (-not $session) { return $false }
    $now = Get-Date

    $st = $null
    if ($script:CS_Busy.ContainsKey($session)) { $st = $script:CS_Busy[$session] }
    if ($st -and ($now - $st.Last).TotalMilliseconds -lt $script:CS_BusyPollMs) { return [bool]$st.Busy }

    $sessPid = Get-SessionPid $session
    if ($sessPid -le 0) {
        $script:CS_Busy[$session] = @{ Last = $now; Seen = @{}; Busy = $false }
        return $false
    }

    $seen = @{}
    $busy = $false
    foreach ($row in @([ClaudeStatusProc]::Children($sessPid))) {
        $parts = $row.Split('|')
        if ($parts.Count -lt 2) { continue }
        $cpid = $parts[0]

        $created = $null
        try { $created = [datetime]::FromFileTimeUtc([int64]$parts[1]).ToLocalTime() } catch { continue }

        # conhost i serwery MCP wstają razem z sesją, więc same z siebie nic
        # nie znaczą - liczy się tylko proces młodszy od alertu
        if ($created -le $after) { continue }

        $seen[$cpid] = $true

        # pracą uznajemy dopiero drugie spotkanie tego samego procesu (>1,5 s):
        # procesy hooków żyją ułamek sekundy i do kolejnego sprawdzenia znikają
        if ($st -and $st.Seen.ContainsKey($cpid)) { $busy = $true }
    }

    $script:CS_Busy[$session] = @{ Last = $now; Seen = $seen; Busy = $busy }
    return $busy
}

# --------------------------------------------------------------------------- #
#  Wynik: stan, który naprawdę obowiązuje                                      #
# --------------------------------------------------------------------------- #
function Resolve-LiveState([string]$state, [string]$session, [string]$hinted, [datetime]$ts) {
    if ($state -ne 'attention' -and $state -ne 'error') { return $state }

    # 1) transkrypt urósł po alercie
    $tp = Get-TranscriptPath $session $hinted
    if ($tp) {
        try {
            if ((Get-Item -LiteralPath $tp -ErrorAction Stop).LastWriteTime -gt $ts.AddSeconds($script:CS_GraceSec)) {
                return 'working'
            }
        } catch { }
    }

    # 2) narzędzie odpalone po alercie nadal działa
    if (Test-SessionBusy $session $ts) { return 'working' }

    return $state
}
