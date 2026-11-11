using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeHttpResponse(int StatusCode, bool IsSuccessStatusCode, string ReasonPhrase, string Body, IReadOnlyDictionary<string, string[]> Headers)
{
    public string GetHeader(string name) => Headers.TryGetValue(name, out var value) ? string.Join(", ", value) : "";
}

public sealed class RuntimeNetwork : IDisposable
{
    public HttpClient Client { get; }
    public RuntimeNetwork(HttpClient? client = null) => Client = client ?? new() { Timeout = Timeout.InfiniteTimeSpan };
    public Action<HttpRequestMessage>? ConfigureStreamingRequest { get; set; }
    public Task<RuntimeHttpResponse> HttpGetAsync(string url, int timeoutSeconds = 15, IReadOnlyDictionary<string, string>? headers = null)
        => HttpSendAsync("GET", url, null, null, timeoutSeconds, headers);
    public Task<RuntimeHttpResponse> HttpPostTextAsync(string url, string text, string contentType = "text/plain; charset=utf-8", int timeoutSeconds = 15, IReadOnlyDictionary<string, string>? headers = null)
        => HttpSendAsync("POST", url, text, contentType, timeoutSeconds, headers);
    public Task<RuntimeHttpResponse> HttpPostJsonAsync<T>(string url, T value, int timeoutSeconds = 15, IReadOnlyDictionary<string, string>? headers = null)
        => HttpSendAsync("POST", url, JsonSerializer.Serialize(value), "application/json", timeoutSeconds, headers);
    public async Task<RuntimeHttpResponse> HttpSendAsync(string method, string url, string? body = null, string? contentType = null,
        int timeoutSeconds = 15, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        using HttpRequestMessage request = new(new(method), url);
        if (body is not null) { request.Content = new StringContent(body, Encoding.UTF8); request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? "text/plain; charset=utf-8"); }
        if (headers is not null) foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        using var response = await Client.SendAsync(request, timeout.Token);
        return new((int)response.StatusCode, response.IsSuccessStatusCode, response.ReasonPhrase ?? "", await response.Content.ReadAsStringAsync(timeout.Token),
            response.Headers.Concat(response.Content.Headers).ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
    }
    public void Dispose() => Client.Dispose();
}
