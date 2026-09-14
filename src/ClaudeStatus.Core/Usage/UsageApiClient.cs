using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeStatus.Core.Usage;

/// <summary>Odpowiedź inna niż 2xx; treść błędu celowo nie jest przechowywana.</summary>
public sealed class UsageApiException : Exception
{
    public UsageApiException(HttpStatusCode status) : base($"HTTP {(int)status} {status}")
    {
        Status = status;
    }

    public HttpStatusCode Status { get; }

    /// <summary>401/403 - token nieważny; nie ma sensu ponawiać, dopóki plik poświadczeń się nie zmieni.</summary>
    public bool IsAuthFailure => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

/// <summary>
/// Ten sam endpoint, z którego korzysta /usage w Claude Code i panel w VS Code.
/// Odpowiedź ma dokładnie ten kształt, który Claude Code zapisuje w
/// cachedUsageUtilization.utilization - stąd wspólny parser.
/// Token trafia wyłącznie tutaj, w nagłówku Authorization.
/// </summary>
public sealed class UsageApiClient : IDisposable
{
    private static readonly Uri Endpoint = new("https://api.anthropic.com/api/oauth/usage");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;

    public UsageApiClient()
    {
        _http = new HttpClient { Timeout = Timeout };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeStatusOverlay/2.0");
        // OAuth w API Anthropic wymaga tej flagi beta - Claude Code wysyła ją przy każdym żądaniu
        _http.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
    }

    public async Task<UsageData?> FetchAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new UsageApiException(response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return UsageData.FromWindows(doc.RootElement, DateTime.Now);
    }

    public void Dispose() => _http.Dispose();
}
