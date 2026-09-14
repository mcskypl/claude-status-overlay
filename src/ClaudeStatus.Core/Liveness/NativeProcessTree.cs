using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ClaudeStatus.Core.Liveness;

/// <summary>Bezpośrednie dziecko procesu; <see cref="CreatedAt"/> jest null, gdy brak dostępu.</summary>
internal readonly record struct ChildProcess(uint Pid, DateTime? CreatedAt, string ExeName);

/// <summary>
/// Wyliczanie dzieci procesu przez Toolhelp32. Zapytanie WMI o to samo kosztuje
/// 200-350 ms, a to jest kilka milisekund - monitor odpytuje co pół sekundy.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeProcessTree
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private static readonly IntPtr InvalidHandle = new(-1);

    public static IReadOnlyList<ChildProcess> Children(int parentPid)
    {
        var result = new List<ChildProcess>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == InvalidHandle || snapshot == IntPtr.Zero) return result;

        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snapshot, ref entry)) return result;

            do
            {
                if (entry.th32ParentProcessID != (uint)parentPid) continue;
                result.Add(new ChildProcess(entry.th32ProcessID, CreationTime(entry.th32ProcessID), entry.szExeFile));
            }
            while (Process32NextW(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
    }

    private static DateTime? CreationTime(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;

        try
        {
            if (!GetProcessTimes(handle, out var creation, out _, out _, out _)) return null;
            return DateTime.FromFileTimeUtc(creation).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr hProcess, out long lpCreationTime, out long lpExitTime,
        out long lpKernelTime, out long lpUserTime);
}
