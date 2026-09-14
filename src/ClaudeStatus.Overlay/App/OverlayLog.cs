using System.Globalization;

namespace ClaudeStatus.Overlay.App;

/// <summary>
/// Błędy nakładki lądują w pliku obok konfiguracji. Pętla rysowania może
/// zgłaszać ten sam błąd 60 razy na sekundę, więc powtórki są dławione.
/// </summary>
public sealed class OverlayLog
{
    private const int RepeatEvery = 50;

    private readonly string _path;
    private readonly object _gate = new();
    private string? _lastMessage;
    private int _repeats;

    public OverlayLog(string path)
    {
        _path = path;
    }

    public void Error(string where, Exception ex) => Write($"{where}: {ex.GetType().Name}: {ex.Message}");

    public void Write(string message)
    {
        lock (_gate)
        {
            if (message == _lastMessage)
            {
                if (++_repeats % RepeatEvery != 0) return;
                message = $"{message} (x{_repeats})";
            }
            else
            {
                _lastMessage = message;
                _repeats = 0;
            }

            try
            {
                File.AppendAllText(_path, $"{DateTime.Now.ToString("s", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}");
            }
            catch (Exception io) when (io is IOException or UnauthorizedAccessException)
            {
                // log nie może być powodem kolejnego błędu
            }
        }
    }
}
