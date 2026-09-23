using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClaudeStatus.Core.Sessions;

namespace ClaudeStatus.Overlay.Assistant;

/// <summary>
/// Tłumacz między mostkiem a panelem. Dwie strony mówią o tym samym innym
/// językiem: mostek zna surowe zdarzenia silnika (<c>t</c>), panel - stany
/// widoku (<c>type</c>). Cała wiedza o tym przekładzie siedzi tutaj, dzięki
/// czemu ani widok, ani mostek nie muszą znać drugiej strony.
/// </summary>
internal sealed class AssistantEngine : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Dictionary<string, string> ServerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["clockify"] = "Clockify",
        ["hubspot"] = "HubSpot",
        ["azure-devops"] = "Azure DevOps",
        ["optimes-monitor"] = "Optimes Monitor",
        ["sqlserver"] = "SQL Server",
    };

    private readonly BridgeClient _bridge = new();
    private readonly Dictionary<string, ToolCall> _tools = [];
    private readonly StringBuilder _text = new();

    /// <summary>
    /// Jeden katalog na stałe. Asystent odpowiada „o rzeczy", nie o kod, więc
    /// pracuje we własnym, pustym katalogu: nie wciąga ustawień ani reguł żadnego
    /// projektu i nie ma jak dotknąć repozytorium przy zwykłym pytaniu.
    /// </summary>
    private readonly string _workingDirectory = AssistantPaths.DefaultWorkingDirectory;

    /// <summary>
    /// Lista na start, zanim mostek poda prawdziwą z <c>supportedModels()</c>.
    /// Mostek rusza dopiero z pierwszym pytaniem, a wybór ma działać już przed nim.
    /// </summary>
    private static readonly ModelOption[] FallbackModels =
    [
        new("opus[1m]", "Opus · 1M", "Najmocniejszy, z kontekstem 1M tokenów"),
        new("opus", "Opus", "Najmocniejszy"),
        new("sonnet", "Sonnet", "Szybszy i tańszy na co dzień"),
        new("haiku", "Haiku", "Najszybszy, do prostych pytań"),
    ];

    private string? _sessionId;
    private bool _started;
    private AssistantStatus? _status;
    private int _pendingConsents;

    /// <summary>Wybór z nagłówka; <c>null</c> = model z settings.json.</summary>
    private string? _model;

    /// <summary>Identyfikator, którym sesja naprawdę jedzie - z wiadomości <c>ready</c>.</summary>
    private string? _activeModel;

    private ModelOption[] _models = FallbackModels;

    private readonly AssistantSuggestions _suggestions;

    public AssistantEngine(string? model = null)
    {
        _model = string.IsNullOrWhiteSpace(model) ? null : model;
        _bridge.Line += OnBridgeLine;
        _bridge.Log += message => Log?.Invoke(message);
        _bridge.Exited += OnBridgeExited;

        _suggestions = new AssistantSuggestions(_workingDirectory);
        _suggestions.Log += message => Log?.Invoke(message);
        _suggestions.Changed += items => Send(new { type = "suggestions", items });
        _suggestions.RefreshIfStale();
    }

    /// <summary>Gotowa wiadomość dla panelu. Wołane z wątku puli - odbiorca marshaluje sam.</summary>
    public event Action<string>? ToUi;

    public event Action<string>? Log;

    /// <summary>
    /// Zmiana stanu sesji asystenta - dla pastylki, nie dla panelu. <c>null</c>
    /// znaczy, że sesji nie ma: mostek jeszcze nie ruszył albo już się skończył.
    /// Wołane z wątku puli - odbiorca marshaluje sam.
    /// </summary>
    public event Action<AssistantStatus?>? StatusChanged;

    /// <summary>
    /// Tura skończyła się odpowiedzią - treść dla dymka. Nie leci przy turze
    /// przerwanej ani zakończonej błędem: nie ma czego pokazywać.
    /// Wołane z wątku puli - odbiorca marshaluje sam.
    /// </summary>
    public event Action<string>? AnswerReady;

    /// <summary>Użytkownik wybrał inny model - do zapamiętania w konfiguracji.</summary>
    public event Action<string?>? ModelChanged;

    public string WorkingDirectory => _workingDirectory;

    private sealed record ToolCall(string Name, string Input, DateTime StartedAt);

    private sealed record ModelOption(string Value, string Name, string Description);

    // -------------------------------------------------------------- panel -> silnik

    /// <summary>Wiadomość z panelu (surowy JSON z <c>postMessage</c>).</summary>
    public void HandleUiMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = Text(root, "type");

        switch (type)
        {
            case "ready":
                Send(new { type = "context", context = Context() });
                break;

            case "prompt":
                EnsureStarted();
                _tools.Clear();
                _text.Clear();
                SetStatus(SessionState.Working);
                Send(new { type = "turnStart" });
                _bridge.Send(new { t = "prompt", text = Text(root, "text") });
                break;

            case "interrupt":
                _bridge.Send(new { t = "interrupt" });
                break;

            case "consent":
            {
                var decision = Text(root, "decision");
                _bridge.Send(new
                {
                    t = "permission",
                    id = Text(root, "id"),
                    allow = decision != "deny",
                    always = decision == "always",
                });

                // Claude Code nie ma zdarzenia "decyzja zapadła" - my je mamy, bo to
                // my pytaliśmy. Ostatnia odpowiedziana karta oddaje turę silnikowi.
                if (Interlocked.Decrement(ref _pendingConsents) <= 0)
                {
                    Interlocked.Exchange(ref _pendingConsents, 0);
                    SetStatus(SessionState.Working);
                }
                break;
            }

            case "newConversation":
                Restart();
                Send(new { type = "reset" });
                // Poprzednia rozmowa właśnie dopisała się do historii - pusty ekran
                // dostaje na razie stare podpowiedzi, nowe dojadą same.
                _suggestions.RefreshIfStale();
                break;

            case "setModel":
            {
                var model = Text(root, "model");
                _model = model.Length > 0 ? model : null;

                // Działający mostek przełącza model w tej samej sesji; niedziałający
                // dostanie go w argumentach przy następnym starcie. Pełny identyfikator
                // poprzedniego modelu przestaje być prawdą w obu przypadkach.
                _activeModel = null;
                if (_started && _bridge.Running) _bridge.Send(new { t = "model", model = _model });

                ModelChanged?.Invoke(_model);
                SendModels();
                break;
            }
        }
    }

    private void EnsureStarted()
    {
        if (_started && _bridge.Running) return;
        _bridge.Start(_workingDirectory, _sessionId, _model);
        _started = true;
    }

    /// <summary>Nowa rozmowa to nowy proces - silnik wiąże sesję z procesem mostka.</summary>
    private void Restart()
    {
        _bridge.Stop();
        _sessionId = null;
        _started = false;
        _tools.Clear();
        _text.Clear();
        ClearStatus();
    }

    // -------------------------------------------------------------- silnik -> panel

    private void OnBridgeLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            Translate(doc.RootElement);
        }
        catch (JsonException)
        {
            Log?.Invoke("mostek wyslal linie, ktora nie jest JSON-em: " + Trim(line, 160));
        }
        catch (Exception ex)
        {
            // Linie z mostka przychodzą na wątku puli, więc wyjątek stąd nie ma
            // kogo złapać - zabiera ze sobą cały proces, razem z pastylką. Awaria
            // na jednej wiadomości ma kosztować tę jedną wiadomość i nic więcej.
            Log?.Invoke("nie udalo sie przetlumaczyc wiadomosci mostka: " + ex);
        }
    }

    private void Translate(JsonElement m)
    {
        switch (Text(m, "t"))
        {
            case "ready":
                _sessionId = Text(m, "session");
                _activeModel = Fallback(Text(m, "model"), _activeModel ?? "");
                Log?.Invoke($"sesja asystenta {_sessionId} · model {_activeModel}");
                SendModels();
                break;

            case "models":
            {
                if (!m.TryGetProperty("models", out var list) || list.ValueKind != JsonValueKind.Array) break;

                // "default" to ten sam wybór, co nasz "z ustawień" - drugi raz go nie pokazujemy.
                var models = list.EnumerateArray()
                    .Select(x => new ModelOption(Text(x, "value"), Fallback(Text(x, "name"), Text(x, "value")), Text(x, "description")))
                    .Where(x => x.Value.Length > 0 && !x.Value.Equals("default", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (models.Length > 0) _models = models;
                SendModels();
                break;
            }

            case "model":
                Log?.Invoke($"model asystenta: {Fallback(Text(m, "model"), "z ustawien")}");
                break;

            case "delta":
                Send(new { type = "delta", text = Text(m, "text") });
                break;

            case "text":
                if (_text.Length > 0) _text.Append("\n\n");
                _text.Append(Text(m, "text"));
                Send(new { type = "text", text = _text.ToString() });
                break;

            case "tool":
            {
                var id = Text(m, "id");
                var name = Text(m, "name");
                _tools[id] = new ToolCall(name, Pretty(m, "input"), DateTime.UtcNow);
                Send(new { type = "toolStart", id, label = ToolLabel(name) + "…" });
                break;
            }

            case "tool_result":
            {
                var id = Text(m, "id");
                var known = _tools.TryGetValue(id, out var call);
                var label = known ? ToolLabel(call!.Name) : id;

                if (m.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.True)
                {
                    Send(new { type = "toolError", id, label, reason = Trim(Text(m, "text"), 400) });
                }
                else
                {
                    Send(new
                    {
                        type = "toolDone",
                        id,
                        label,
                        metric = known ? Duration(DateTime.UtcNow - call!.StartedAt) : "",
                        input = known ? call!.Input : "",
                        result = Text(m, "text"),
                    });
                }
                break;
            }

            case "permission":
            {
                var title = Fallback(Text(m, "title"), ToolLabel(Text(m, "tool")));
                Interlocked.Increment(ref _pendingConsents);
                SetStatus(SessionState.Attention, "czeka na zgodę: " + title);
                Send(new
                {
                    type = "consent",
                    consent = new
                    {
                        id = Text(m, "id"),
                        title,
                        effect = Effect(m),
                        facts = Facts(m),
                    },
                });
                break;
            }

            case "done":
            {
                Interlocked.Exchange(ref _pendingConsents, 0);
                SetStatus(SessionState.Done);
                Send(new { type = "done" });

                var failed = m.TryGetProperty("error", out var why) && why.ValueKind is not JsonValueKind.Null;
                var answer = _text.ToString();
                if (!failed && answer.Length > 0) AnswerReady?.Invoke(answer);
                break;
            }

            case "error":
                Interlocked.Exchange(ref _pendingConsents, 0);
                SetStatus(SessionState.Error, Trim(Text(m, "message"), 120));
                Send(new
                {
                    type = "engineError",
                    error = new
                    {
                        title = "Błąd silnika",
                        text = Text(m, "message"),
                        diagnostic = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    },
                });
                break;
        }
    }

    private void OnBridgeExited(int code)
    {
        _started = false;
        Interlocked.Exchange(ref _pendingConsents, 0);

        if (code == 0)
        {
            ClearStatus();
            return;
        }

        SetStatus(SessionState.Error, "mostek przestał odpowiadać");
        Send(new
        {
            type = "engineError",
            error = new
            {
                title = "Mostek przestał odpowiadać",
                text = "Proces silnika zakończył się. Treść pytania jest zachowana — spróbuj ponowić turę.",
                diagnostic = $"exit={code} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            },
        });
    }

    // -------------------------------------------------------------- stan dla pastylki
    //
    // Stan sesji asystenta bierze się stąd, a nie z pliku pisanego hookiem, bo
    // tylko tutaj widać całość: hook wie o prośbie o zgodę, ale zdarzenia "zgoda
    // udzielona" Claude Code nie ma, a decyzja zapada w panelu. Z hooka wpis
    // zostawałby więc w stanie "czeka na Ciebie" długo po tym, jak tura poszła
    // dalej. Tu przejścia są komplety, bo to ta sama strona, która pyta i słyszy
    // odpowiedź.

    private void SetStatus(SessionState state, string note = "")
    {
        var current = _status;
        if (current is not null && current.State == state && current.Note == note) return;

        _status = new AssistantStatus(state, note, DateTime.Now);
        StatusChanged?.Invoke(_status);
    }

    private void ClearStatus()
    {
        if (_status is null) return;
        _status = null;
        StatusChanged?.Invoke(null);
    }

    // ------------------------------------------------------------------ dane panelu

    /// <summary>Pełny stan startowy - panel prosi o niego wiadomością <c>ready</c>.</summary>
    public string InitMessage(SessionSnapshot snapshot, string anchor) => JsonSerializer.Serialize(new
    {
        type = "init",
        anchor,
        title = "Nowa rozmowa",
        context = Context(),
        models = Models(),
        suggestions = _suggestions.Items,
        sessions = Sessions(snapshot),
        limits = Limits(snapshot),
        account = Account(snapshot),
    }, Json);

    public string SnapshotMessage(SessionSnapshot snapshot) => JsonSerializer.Serialize(new
    {
        type = "sessions",
        sessions = Sessions(snapshot),
    }, Json);

    public string LimitsMessage(SessionSnapshot snapshot) => JsonSerializer.Serialize(new
    {
        type = "limits",
        limits = Limits(snapshot),
    }, Json);

    public string AccountMessage(SessionSnapshot snapshot) => JsonSerializer.Serialize(new
    {
        type = "account",
        account = Account(snapshot),
    }, Json);

    public static string ShowMessage(bool visible) =>
        JsonSerializer.Serialize(new { type = "show", visible }, Json);

    private object Context() => new
    {
        name = new DirectoryInfo(_workingDirectory).Name,
        path = _workingDirectory,
    };

    /// <summary>
    /// Wybór modelu dla nagłówka. Wybrany spoza listy (np. wpisany ręcznie do
    /// konfiguracji) i tak na niej ląduje - inaczej nie byłoby widać, czym jedzie sesja.
    /// </summary>
    private object Models()
    {
        var options = _models;
        if (_model is not null && !options.Any(o => o.Value.Equals(_model, StringComparison.OrdinalIgnoreCase)))
        {
            options = [.. options, new ModelOption(_model, _model, "Z konfiguracji nakładki")];
        }

        return new
        {
            current = _model,
            active = _activeModel ?? "",
            options = options.Select(o => new { value = o.Value, name = o.Name, description = o.Description }).ToArray(),
        };
    }

    private void SendModels() => Send(new { type = "models", models = Models() });

    private static object[] Sessions(SessionSnapshot snapshot)
    {
        var now = DateTime.Now;
        return [.. snapshot.Sessions.Select(s => new
        {
            id = s.SessionId,
            name = s.DisplayName,
            state = StateName(s.State),
            age = Age(now - s.Timestamp),
            path = s.Cwd ?? "",
        })];
    }

    private static object? Limits(SessionSnapshot snapshot)
    {
        var usage = snapshot.Usage;
        if (!usage.Ok || !usage.HasAnyWindow) return null;

        return new
        {
            window5h = Window(usage.FiveHour, TimeSpan.FromHours(5)),
            window7d = Window(usage.SevenDay, TimeSpan.FromDays(7)),
        };
    }

    private static object? Window(Core.Usage.UsageWindow? window, TimeSpan span)
    {
        if (window is null) return null;

        double? elapsed = null;
        if (window.ResetsAt is { } resets)
        {
            var left = resets - DateTime.Now;
            elapsed = Math.Clamp((1 - left.TotalMilliseconds / span.TotalMilliseconds) * 100, 0, 100);
        }

        return new { usedPct = window.Used, elapsedPct = elapsed, reset = ResetLabel(window.ResetsAt) };
    }

    private static object Account(SessionSnapshot snapshot) => new
    {
        email = snapshot.Account?.Email ?? "",
        refreshedAgo = snapshot.Usage.FetchedAt is { } at
            ? $"odświeżono {Age(DateTime.Now - at)} temu"
            : "",
    };

    // ------------------------------------------------------------------ formatowanie

    private static readonly string[] Days = ["ndz", "pon", "wt", "śr", "czw", "pt", "sob"];

    private static string ResetLabel(DateTime? resets)
    {
        if (resets is not { } at) return "";
        var sameDay = at.Date == DateTime.Now.Date;
        return sameDay
            ? $"reset {at:HH:mm}"
            : $"reset {Days[(int)at.DayOfWeek]} {at:HH:mm}";
    }

    private static string StateName(SessionState state) => state switch
    {
        SessionState.Working => "pracuje",
        SessionState.Done => "gotowe",
        SessionState.Attention => "czeka",
        SessionState.Error => "blad",
        _ => "bezczynny",
    };

    private static string Age(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalSeconds < 60) return $"{(int)age.TotalSeconds}s";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m";
        return $"{(int)age.TotalHours}h";
    }

    /// <summary>
    /// Przecinek dziesiętny bez sięgania po kulturę. Cały solucja jest budowana
    /// z <c>InvariantGlobalization</c>, gdzie <c>pl-PL</c> po prostu nie istnieje -
    /// <c>CultureInfo.GetCultureInfo("pl-PL")</c> rzuca tam wyjątkiem.
    /// </summary>
    private static readonly NumberFormatInfo Decimals = new() { NumberDecimalSeparator = "," };

    private static string Duration(TimeSpan taken) =>
        taken.TotalSeconds.ToString("0.0", Decimals) + " s";

    private static string ToolLabel(string name)
    {
        if (!name.StartsWith("mcp__", StringComparison.Ordinal)) return name;

        var parts = name.Split("__");
        if (parts.Length < 3) return name;

        var server = ServerNames.TryGetValue(parts[1], out var pretty) ? pretty : parts[1];
        return $"{server} · {string.Join("__", parts.Skip(2))}";
    }

    private string Effect(JsonElement m)
    {
        var server = Text(m, "server");
        return server.Length > 0
            ? $"Operacja pójdzie przez serwer `{server}` w katalogu `{_workingDirectory}`."
            : $"Narzędzie `{Text(m, "tool")}` zadziała w katalogu `{_workingDirectory}`.";
    }

    private object[] Facts(JsonElement m)
    {
        var facts = new List<object>
        {
            new { label = "Narzędzie", value = Text(m, "tool") },
            new { label = "Katalog", value = _workingDirectory },
        };

        var server = Text(m, "server");
        if (server.Length > 0)
        {
            var source = Text(m, "source");
            facts.Add(new { label = "Serwer", value = source.Length > 0 ? $"{server} ({source})" : server });
        }

        var reason = Text(m, "reason");
        if (reason.Length > 0) facts.Add(new { label = "Powód", value = reason });

        return [.. facts];
    }

    // ------------------------------------------------------------------ drobiazgi

    private void Send(object message) => ToUi?.Invoke(JsonSerializer.Serialize(message, Json));

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Pretty(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
            ? JsonSerializer.Serialize(v, new JsonSerializerOptions(Json) { WriteIndented = true })
            : "";

    private static string Fallback(string value, string fallback) => value.Length > 0 ? value : fallback;

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    public void Dispose() => _bridge.Dispose();
}
