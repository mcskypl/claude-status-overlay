using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClaudeStatus.Overlay.Assistant;

/// <summary>
/// Proces mostka Node i linie JSON w obie strony - nic więcej. Nie wie, co
/// znaczą wiadomości; tłumaczenie na język panelu robi <see cref="AssistantEngine"/>.
/// </summary>
internal sealed class BridgeClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private Process? _process;

    /// <summary>Jedna linia z mostka, jeszcze nieprzetłumaczona. Wołane z wątku puli.</summary>
    public event Action<string>? Line;

    /// <summary>Mostek przestał żyć - przekazany jest kod wyjścia.</summary>
    public event Action<int>? Exited;

    /// <summary>Diagnostyka ze stderr mostka.</summary>
    public event Action<string>? Log;

    public bool Running
    {
        get
        {
            lock (_gate) return _process is { HasExited: false };
        }
    }

    public void Start(string workingDirectory, string? resume = null, string? model = null)
    {
        lock (_gate)
        {
            if (_process is { HasExited: false }) return;

            var process = new Process
            {
                StartInfo = AssistantPaths.BridgeStartInfo(workingDirectory, resume, model),
                EnableRaisingEvents = true,
            };

            // Wszystko, co przychodzi od procesu, który przestał być bieżący (Stop()
            // odpiął go i dobija w tle), jest już bez znaczenia - łącznie z jego
            // wyjściem, które nie jest awarią. Sprawdzane bez _gate: .NET woła
            // Exited pod własną blokadą procesu, a czekanie tu na _gate potrafiło
            // zakleszczyć się z Stop().
            bool Current() => ReferenceEquals(Volatile.Read(ref _process), process);

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data) && Current()) Line?.Invoke(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) Log?.Invoke(e.Data);
            };
            process.Exited += (_, _) =>
            {
                if (!Current()) return;

                var code = 0;
                try { code = process.ExitCode; } catch (InvalidOperationException) { /* już posprzątany */ }
                Exited?.Invoke(code);
            };

            process.Start();
            // Przed rozpoczęciem czytania - inaczej pierwsze linie trafiłyby na
            // proces, który jeszcze nie jest bieżący, i przepadły.
            Volatile.Write(ref _process, process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
    }

    public void Send(object message)
    {
        lock (_gate)
        {
            if (_process is not { HasExited: false }) return;
            try
            {
                _process.StandardInput.WriteLine(JsonSerializer.Serialize(message, Json));
                _process.StandardInput.Flush();
            }
            catch (IOException)
            {
                // mostek zamknął wejście; zdarzenie Exited i tak zaraz przyjdzie
            }
        }
    }

    /// <summary>
    /// Kończy mostek. Sesja zostaje na dysku, więc da się ją wznowić. Proces od
    /// razu przestaje być bieżący, a samo zamykanie (do 1,5 s grzecznie, potem
    /// siłą) idzie w tle - nowa rozmowa nie czeka na starą. Przy zamykaniu
    /// aplikacji czekamy, żeby nie zostawić sierot.
    /// </summary>
    public void Stop(bool wait = false)
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            if (process is null) return;
            Volatile.Write(ref _process, null);
        }

        if (wait) Shutdown(process);
        else _ = Task.Run(() => Shutdown(process));
    }

    private static void Shutdown(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("{\"t\":\"close\"}");
                process.StandardInput.Flush();
                if (!process.WaitForExit(1500)) process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { /* już go nie ma */ }
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose() => Stop(wait: true);
}
