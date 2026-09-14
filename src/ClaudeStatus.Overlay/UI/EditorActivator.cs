using ClaudeStatus.Core.Sessions;
using ClaudeStatus.Overlay.Interop;

namespace ClaudeStatus.Overlay.UI;

/// <summary>
/// Klik w sesję aktywuje okno edytora, którego tytuł zawiera nazwę katalogu
/// projektu (VS Code, terminal z Claude Code - wszystkie mają ją w tytule).
/// </summary>
internal static class EditorActivator
{
    public static void Activate(SessionInfo? session, IntPtr ownWindow)
    {
        if (session?.Cwd is not { Length: > 0 } cwd) return;

        string leaf;
        try
        {
            leaf = Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch (ArgumentException)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(leaf)) return;

        var hwnd = WindowFinder.FindByTitleFragment(leaf, ownWindow);
        WindowFinder.Activate(hwnd);
    }
}
