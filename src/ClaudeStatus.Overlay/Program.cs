using ClaudeStatus.Core;
using ClaudeStatus.Core.Account;
using ClaudeStatus.Core.Liveness;
using ClaudeStatus.Core.Sessions;
using ClaudeStatus.Core.Update;
using ClaudeStatus.Core.Usage;
using ClaudeStatus.Overlay.App;
using ClaudeStatus.Overlay.Assistant;
using ClaudeStatus.Overlay.UI;

namespace ClaudeStatus.Overlay;

/// <summary>
/// Pastylka statusu w stylu notcha - czarna, przyklejona do krawędzi ekranu,
/// zawsze na wierzchu. Zwinięta pokazuje aktywną sesję (pierścień + czas)
/// i dwa mikro-paski limitów; po najechaniu morfuje w panel z listą sesji
/// i miernikami limitów 5 h / 7 dni.
/// <code>
/// ClaudeStatusOverlay.exe          uruchom (tylko jedna instancja naraz)
/// ClaudeStatusOverlay.exe --exit   poproś działającą nakładkę o zamknięcie
/// </code>
/// </summary>
internal static class Program
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(480);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase)))
        {
            return SingleInstance.SignalExit() ? 0 : 1;
        }

        using var instance = SingleInstance.TryAcquire();
        if (instance is null) return 0;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var log = new OverlayLog(ClaudePaths.OverlayLogFile);
        Application.ThreadException += (_, e) => log.Error("ui", e.Exception);

        var configStore = new OverlayConfigStore(ClaudePaths.OverlayConfigFile);
        var config = configStore.Load();

        var store = new SessionStatusStore(ClaudePaths.StatusDir);
        store.EnsureDirectory();

        // Wpisy po wcześniejszych sesjach asystenta - z czasów, gdy pisał je hook.
        // Od teraz asystent melduje się nakładce wprost, więc żadnych nie zostawia.
        AssistantSession.PurgeStale(store);

        var reader = new SessionReader(store, new LiveStateResolver(ClaudePaths.ProjectsDir, ClaudePaths.SessionsDir));

        // limity: API Anthropic tokenem Claude Code, cache z ~/.claude.json jako fallback
        using var usageApi = new UsageApiClient();
        using var usage = new LiveUsageSource(usageApi, new OAuthCredentialStore(ClaudePaths.CredentialsFile),
            new UsageCacheReader(ClaudePaths.UsageCacheFile));
        usage.Failed += ex => log.Error("usage", ex);

        var account = new AccountInfoReader(ClaudePaths.AccountFile);

        using var monitor = new SessionMonitor(reader, usage, account, store.Directory, PollInterval)
        {
            UsageEnabled = config.Usage,
            UsageInterval = config.UsageInterval,
        };
        monitor.Failed += ex => log.Error("monitor", ex);

        // aktualizacje: publiczne wydania na GitHubie, sprawdzane raz na dobę
        using var releases = new GitHubReleaseClient();
        using var updates = new UpdateChecker(releases) { Enabled = config.Updates };
        updates.Failed += ex => log.Error("update", ex);

        using var form = new OverlayForm(config, configStore, store, monitor, updates, log);

        // Asystent: rozmowa dostawiana do panelu pastylki. Osobne okno w tej samej
        // pętli komunikatów, dzielące z pastylką monitor sesji - dzięki temu nie ma
        // drugiego odpytywania dysku ani drugiego ruchu do API limitów.
        using var assistant = new AssistantForm(
            config, monitor, log, form.PlaceContent, () => form.ContentScreenBounds);

        // Panel i rozmowa są jedną całością: klik w pastylkę rozwija oba, klik poza
        // nimi zwija oba, a klik z jednego w drugie nie zamyka niczego.
        // Dymek z gotową odpowiedzią - osobne, malutkie okno pod pastylką.
        using var toast = new AnswerToastForm(config, log, () => form.ContentScreenBounds);
        assistant.AnswerReady = toast.Show;
        toast.OpenRequested = form.OpenPanelWithAssistant;

        form.AssistantVisibility = visible =>
        {
            if (visible)
            {
                // Rozmowa na wierzchu czyni dymek zbędnym - pokazuje to samo.
                toast.HideToast();
                assistant.ShowAssistant();
            }
            else
            {
                assistant.HideAssistant();
            }
        };
        form.UnitHasFocus = assistant.ForegroundIsOurs;

        // Powiadomienie o nowej wersji stało kiedyś na dole panelu pastylki.
        form.UpdateBannerChanged = assistant.ShowUpdate;
        assistant.InstallUpdateRequested = form.InstallUpdate;
        assistant.ShowUpdate(form.CurrentUpdateBanner);
        assistant.PanelOpenRequested = form.OpenPanelWithAssistant;
        assistant.PanelCloseRequested = form.ClosePanel;
        assistant.ExpandedChanged = form.SetAssistantExpanded;
        // Silnik woła to z wątku okna asystenta, a konfigurację trzyma pastylka.
        assistant.ModelChanged = model => form.BeginInvoke(() => form.SetAssistantModel(model));

        instance.ExitRequested += () =>
        {
            try
            {
                form.BeginInvoke(form.Close);
            }
            catch (InvalidOperationException)
            {
                // okno już zamknięte
            }
        };

        monitor.Start();
        Application.Run(form);
        return 0;
    }
}
