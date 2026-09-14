using System.Text;
using static ClaudeStatus.Overlay.Interop.NativeMethods;

namespace ClaudeStatus.Overlay.Interop;

/// <summary>Szuka widocznych okien najwyższego poziomu po fragmencie tytułu i aktywuje je.</summary>
internal static class WindowFinder
{
    public static IntPtr FindByTitleFragment(string fragment, IntPtr exclude)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return IntPtr.Zero;

        var found = IntPtr.Zero;
        var buffer = new StringBuilder(512);

        EnumWindows((hwnd, _) =>
        {
            if (hwnd == exclude || !IsWindowVisible(hwnd)) return true;

            var length = GetWindowTextLength(hwnd);
            if (length <= 0) return true;

            buffer.Clear();
            buffer.EnsureCapacity(length + 1);
            GetWindowText(hwnd, buffer, buffer.Capacity);

            if (buffer.ToString().Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static void Activate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }
}
