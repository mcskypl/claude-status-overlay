using System.Media;
using ClaudeStatus.Core.Sessions;

namespace ClaudeStatus.Overlay.UI;

/// <summary>
/// Dźwięki systemowe na zmianę stanu: wykrzyknik, gdy Claude czeka albo padł,
/// gwiazdka, gdy skończył pracę. Pierwsze zobaczenie sesji nie gra.
/// </summary>
public sealed class StateSoundPlayer
{
    public void Play(IReadOnlyList<StateTransition> transitions)
    {
        var exclamation = false;
        var asterisk = false;

        foreach (var t in transitions)
        {
            if (t.Previous is null) continue;

            if (t.Session.State.NeedsAttention()) exclamation = true;
            else if (t.Session.State == SessionState.Done && t.Previous == SessionState.Working) asterisk = true;
        }

        try
        {
            if (exclamation) SystemSounds.Exclamation.Play();
            else if (asterisk) SystemSounds.Asterisk.Play();
        }
        catch (Exception)
        {
            // brak urządzenia audio nie jest powodem do przerwania klatki
        }
    }
}
