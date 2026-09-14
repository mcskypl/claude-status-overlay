namespace ClaudeStatus.Core.Liveness;

/// <summary>
/// Czy sesja odpaliła po alercie narzędzie, które nadal działa? Procesy
/// narzędzi są dziećmi procesu Claude Code. Liczy się tylko proces młodszy od
/// alertu (conhost i serwery MCP wstają razem z sesją) i widziany dwa razy
/// z rzędu - procesy hooków żyją ułamek sekundy i znikają do kolejnego
/// sprawdzenia.
/// </summary>
internal sealed class ToolActivityDetector
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    private sealed class Probe
    {
        public DateTime Last;
        public HashSet<uint> Seen = [];
        public bool Busy;
    }

    private readonly SessionProcessMap _processes;
    private readonly Dictionary<string, Probe> _probes = new(StringComparer.Ordinal);

    public ToolActivityDetector(SessionProcessMap processes)
    {
        _processes = processes;
    }

    public bool IsBusy(string sessionId, DateTime after)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;

        var now = DateTime.Now;
        _probes.TryGetValue(sessionId, out var previous);
        if (previous is not null && now - previous.Last < PollInterval) return previous.Busy;

        var pid = _processes.PidOf(sessionId);
        if (pid <= 0)
        {
            _probes[sessionId] = new Probe { Last = now };
            return false;
        }

        var probe = new Probe { Last = now };
        foreach (var child in NativeProcessTree.Children(pid))
        {
            if (child.CreatedAt is null || child.CreatedAt <= after) continue;

            probe.Seen.Add(child.Pid);
            if (previous is not null && previous.Seen.Contains(child.Pid)) probe.Busy = true;
        }

        _probes[sessionId] = probe;
        return probe.Busy;
    }
}
