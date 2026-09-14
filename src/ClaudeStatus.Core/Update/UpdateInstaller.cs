using System.Diagnostics;

namespace ClaudeStatus.Core.Update;

/// <summary>
/// Pobranie instalatora z wydania i oddanie mu sterów.
///
/// Instalator jest zrobiony Inno Setupem, więc w trybie cichym sam zamyka
/// działającą nakładkę (Restart Manager), podmienia pliki, odświeża hooki
/// i uruchamia nową wersję. Nasza rola kończy się na jego odpaleniu -
/// zaraz potem wychodzimy, żeby nie trzymać własnych plików.
/// </summary>
public sealed class UpdateInstaller : IDisposable
{
    // /SILENT pokazuje sam pasek postępu (bez klikania), reszta wycisza pytania
    private const string SilentArgs = "/SILENT /SUPPRESSMSGBOXES /NORESTART /FORCECLOSEAPPLICATIONS";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Pobiera instalator do katalogu tymczasowego. <paramref name="progress"/>
    /// dostaje 0..1 (albo -1, gdy serwer nie podał rozmiaru).
    /// </summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        if (info.SetupUrl is not { Length: > 0 } url) throw new InvalidOperationException("Wydanie nie ma instalatora.");

        var path = Path.Combine(Path.GetTempPath(), $"ClaudeStatusOverlay-Setup-{info.Version}.exe");

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? info.SetupSize;
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                progress?.Report(total > 0 ? (double)done / total : -1);
            }
        }

        return path;
    }

    /// <summary>
    /// Uruchamia pobrany instalator w trybie cichym. Zwraca false, gdy nie dało
    /// się go wystartować - wtedy nakładka zostaje przy starej wersji.
    /// </summary>
    public static bool Launch(string setupPath)
    {
        try
        {
            var info = new ProcessStartInfo(setupPath, SilentArgs)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetTempPath(),
            };
            return Process.Start(info) is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>Otwiera stronę wydania w przeglądarce (gdy nie ma czego zainstalować).</summary>
    public static void OpenPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // brak przeglądarki - trudno
        }
    }

    public void Dispose() => _http.Dispose();
}
