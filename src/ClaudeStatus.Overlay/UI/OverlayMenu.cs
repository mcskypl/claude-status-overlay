using System.ComponentModel;
using ClaudeStatus.Core.Update;
using ClaudeStatus.Overlay.App;
using ClaudeStatus.Overlay.Placement;

namespace ClaudeStatus.Overlay.UI;

/// <summary>Co menu może zlecić oknu - okno decyduje, jak to wykonać.</summary>
public interface IOverlayCommands
{
    /// <summary>Konfiguracja zmieniona - zapisz i przerysuj.</summary>
    void ConfigChanged();

    /// <summary>Kotwica zmieniona z menu; dla Free trzeba zapamiętać bieżącą pozycję.</summary>
    void AnchorChanged(WidgetAnchor anchor);

    void ClearFinishedSessions();

    /// <summary>Nazwa czekającej wersji albo null - menu pokazuje wtedy wpis "zaktualizuj".</summary>
    string? PendingUpdateName { get; }

    /// <summary>Czy właśnie trwa sprawdzanie na GitHubie.</summary>
    bool UpdateCheckBusy { get; }

    void CheckForUpdates();

    void InstallUpdate();

    void Exit();
}

/// <summary>
/// Menu kontekstowe: pozycja, odklejenie od krawędzi, blokada przesuwania, dźwięki, rozwijanie
/// po najechaniu, limity wraz z częstością ich odświeżania, zawsze na wierzchu, wyczyść, zamknij.
/// </summary>
public sealed class OverlayMenu : ContextMenuStrip
{
    /// <summary>Do wyboru w menu; rzadziej = mniej zapytań do API limitów.</summary>
    private static readonly (int Seconds, string Label)[] UsageIntervals =
    [
        (60, "co minutę"),
        (300, "co 5 minut"),
        (600, "co 10 minut"),
        (900, "co 15 minut"),
        (1800, "co 30 minut"),
    ];

    private readonly OverlayConfig _config;
    private readonly IOverlayCommands _commands;
    private readonly Dictionary<WidgetAnchor, ToolStripMenuItem> _anchorItems = new();
    private readonly Dictionary<int, ToolStripMenuItem> _intervalItems = new();
    private readonly ToolStripMenuItem _intervalMenu;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripSeparator _updateSeparator = new();
    private readonly Font _boldFont;

    public OverlayMenu(OverlayConfig config, IOverlayCommands commands)
    {
        _config = config;
        _commands = commands;
        _intervalMenu = BuildUsageIntervalMenu(commands);
        _boldFont = new Font(Font, FontStyle.Bold);
        _updateItem = new ToolStripMenuItem("Zaktualizuj", null, (_, _) => commands.InstallUpdate())
        {
            Font = _boldFont,
            Visible = false,
        };
        _updateSeparator.Visible = false;

        // wpis o nowej wersji siada na samej górze - to jedyna rzecz w tym menu,
        // która pojawia się sama z siebie i warto, żeby rzucała się w oczy
        Items.Add(_updateItem);
        Items.Add(_updateSeparator);

        Items.Add(BuildPositionMenu(commands));
        Items.Add(Toggle("Odklejona od krawędzi", () => _config.Detached, v => _config.Detached = v, commands));
        Items.Add(Toggle("Zablokuj przesuwanie", () => _config.Locked, v => _config.Locked = v, commands));
        Items.Add(new ToolStripSeparator());
        Items.Add(Toggle("Dźwięki", () => _config.Sound, v => _config.Sound = v, commands));
        Items.Add(Toggle("Rozwijaj po najechaniu", () => _config.Hover, v => _config.Hover = v, commands));
        Items.Add(Toggle("Limity 5 h / 7 dni", () => _config.Usage, v => _config.Usage = v, commands));
        Items.Add(_intervalMenu);
        Items.Add(Toggle("Zawsze na wierzchu", () => _config.TopMost, v => _config.TopMost = v, commands));
        Items.Add(new ToolStripSeparator());
        Items.Add(new ToolStripMenuItem("Wyczyść zakończone sesje", null, (_, _) => commands.ClearFinishedSessions()));
        Items.Add(new ToolStripSeparator());
        Items.Add(BuildUpdatesMenu(commands));
        Items.Add(new ToolStripSeparator());
        Items.Add(new ToolStripMenuItem("Zamknij nakładkę", null, (_, _) => commands.Exit()));

        SyncAnchor();
        SyncUsageInterval();
    }

    /// <summary>Odśwież "radio" kotwic - po przeciągnięciu pastylki kotwica staje się Free.</summary>
    public void SyncAnchor()
    {
        foreach (var (anchor, item) in _anchorItems) item.Checked = anchor == _config.Anchor;
    }

    /// <summary>Stan zależny od innych punktów menu ustawiamy przy otwieraniu, nie przy budowie.</summary>
    protected override void OnOpening(CancelEventArgs e)
    {
        base.OnOpening(e);
        _intervalMenu.Enabled = _config.Usage;   // przy wyłączonych limitach nie ma czego odświeżać
        SyncUsageInterval();
        SyncUpdate();
    }

    /// <summary>Wpis "Zaktualizuj do X" istnieje tylko wtedy, gdy naprawdę jest co instalować.</summary>
    private void SyncUpdate()
    {
        var pending = _commands.PendingUpdateName;
        _updateItem.Text = pending is null ? "Zaktualizuj" : $"Zaktualizuj do {pending}";
        _updateItem.Visible = pending is not null;
        _updateSeparator.Visible = pending is not null;
    }

    private ToolStripMenuItem BuildUpdatesMenu(IOverlayCommands commands)
    {
        var menu = new ToolStripMenuItem("Aktualizacje");

        var check = new ToolStripMenuItem("Sprawdź teraz", null, (_, _) => commands.CheckForUpdates());
        menu.DropDownItems.Add(check);
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(Toggle("Sprawdzaj codziennie", () => _config.Updates, v => _config.Updates = v, commands));
        menu.DropDownItems.Add(new ToolStripSeparator());

        var version = new ToolStripMenuItem($"Wersja {UpdateSource.Current}") { Enabled = false };
        menu.DropDownItems.Add(version);

        menu.DropDownOpening += (_, _) =>
        {
            check.Enabled = !commands.UpdateCheckBusy;
            check.Text = commands.UpdateCheckBusy ? "Sprawdzam..." : "Sprawdź teraz";
        };

        return menu;
    }

    /// <summary>"Radio" odstępów; wartość spoza listy (ręcznie wpisana w pliku) nie zaznacza nic.</summary>
    private void SyncUsageInterval()
    {
        foreach (var (seconds, item) in _intervalItems) item.Checked = seconds == _config.UsageIntervalSeconds;
    }

    private ToolStripMenuItem BuildUsageIntervalMenu(IOverlayCommands commands)
    {
        var menu = new ToolStripMenuItem("Odświeżaj limity");

        foreach (var (seconds, label) in UsageIntervals)
        {
            var item = new ToolStripMenuItem(label, null, (_, _) =>
            {
                _config.UsageIntervalSeconds = seconds;
                commands.ConfigChanged();
                SyncUsageInterval();
            });
            _intervalItems[seconds] = item;
            menu.DropDownItems.Add(item);
        }

        return menu;
    }

    private ToolStripMenuItem BuildPositionMenu(IOverlayCommands commands)
    {
        var position = new ToolStripMenuItem("Pozycja");

        foreach (var anchor in AnchorExtensions.EdgeAnchors)
        {
            position.DropDownItems.Add(AnchorItem(anchor, commands));
            if (anchor.EndsMenuRow()) position.DropDownItems.Add(new ToolStripSeparator());
        }

        position.DropDownItems.Add(new ToolStripSeparator());
        position.DropDownItems.Add(AnchorItem(WidgetAnchor.Free, commands));
        return position;
    }

    private ToolStripMenuItem AnchorItem(WidgetAnchor anchor, IOverlayCommands commands)
    {
        var item = new ToolStripMenuItem(anchor.Label(), null, (_, _) =>
        {
            commands.AnchorChanged(anchor);
            SyncAnchor();
        });
        _anchorItems[anchor] = item;
        return item;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _boldFont.Dispose();
        base.Dispose(disposing);
    }

    private static ToolStripMenuItem Toggle(string text, Func<bool> get, Action<bool> set, IOverlayCommands commands)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = get() };
        item.Click += (_, _) =>
        {
            set(item.Checked);
            commands.ConfigChanged();
        };
        return item;
    }
}
