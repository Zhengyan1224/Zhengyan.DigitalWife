using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed record AndroidHttpResponse(int StatusCode, bool IsSuccessStatusCode, string ReasonPhrase, string Body, IReadOnlyDictionary<string, string[]> Headers);

public sealed class AndroidScriptNetwork
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    public async Task<AndroidHttpResponse> SendAsync(string method, string url, string? body = null, string? contentType = "application/json", int timeoutSeconds = 15, IReadOnlyDictionary<string, string>? headers = null)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300)));
        using HttpRequestMessage request = new(new HttpMethod(string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant()), url);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, contentType ?? "application/octet-stream");
        if (headers is not null) foreach (var pair in headers) request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        using HttpResponseMessage response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        Dictionary<string, string[]> responseHeaders = response.Headers.Concat(response.Content.Headers).ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        return new AndroidHttpResponse((int)response.StatusCode, response.IsSuccessStatusCode, response.ReasonPhrase ?? string.Empty, text, responseHeaders);
    }
    public Task<AndroidHttpResponse> GetAsync(string url, int timeoutSeconds = 15, IReadOnlyDictionary<string, string>? headers = null) => SendAsync("GET", url, null, null, timeoutSeconds, headers);
    public Task<AndroidHttpResponse> PostTextAsync(string url, string text, string contentType = "text/plain; charset=utf-8", int timeoutSeconds = 15, IReadOnlyDictionary<string, string>? headers = null) => SendAsync("POST", url, text, contentType, timeoutSeconds, headers);
    public Task<AndroidHttpResponse> PostJsonAsync<T>(string url, T value, int timeoutSeconds = 15, IReadOnlyDictionary<string, string>? headers = null) => SendAsync("POST", url, JsonSerializer.Serialize(value), "application/json; charset=utf-8", timeoutSeconds, headers);
}

public sealed class AndroidScriptSaveStore
{
    private readonly string _root;
    internal AndroidScriptSaveStore(string root) { _root = Path.GetFullPath(root); Directory.CreateDirectory(_root); }
    public string SaveDirectory => _root;
    public bool Exists(string fileName) => File.Exists(Resolve(fileName));
    public void WriteText(string fileName, string text) { string path = Resolve(fileName); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text ?? string.Empty); }
    public string ReadText(string fileName, string fallback = "") => File.Exists(Resolve(fileName)) ? File.ReadAllText(Resolve(fileName)) : fallback;
    public void WriteJson<T>(string fileName, T value) => WriteText(fileName, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    public T? ReadJson<T>(string fileName, T? fallback = default) => !File.Exists(Resolve(fileName)) ? fallback : JsonSerializer.Deserialize<T>(File.ReadAllText(Resolve(fileName))) ?? fallback;
    public bool Delete(string fileName) { string path = Resolve(fileName); if (!File.Exists(path)) return false; File.Delete(path); return true; }
    private string Resolve(string fileName) { if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Save file name cannot be empty.", nameof(fileName)); string path = Path.GetFullPath(Path.Combine(_root, fileName.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar))); if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Save path is outside the save directory."); return path; }
}

public sealed class AndroidScriptBubbleManager
{
    public static AndroidScriptBubbleManager Shared { get; } = new();
    private readonly Dictionary<string, RuntimeDialogueBubble> _bubbles = new(StringComparer.OrdinalIgnoreCase);
    public void Clear() => _bubbles.Clear();
    public RuntimeDialogueBubble GetOrCreate(string name)
    {
        if (!_bubbles.TryGetValue(name, out RuntimeDialogueBubble? bubble))
            _bubbles[name] = bubble = new RuntimeDialogueBubble(name);
        return bubble;
    }
    public IEnumerable<RuntimeDialogueBubble> VisibleBubbles => _bubbles.Values.Where(b => b.Visible && (!string.IsNullOrWhiteSpace(b.Text) || !string.IsNullOrWhiteSpace(b.HeaderText) || !string.IsNullOrWhiteSpace(b.FooterText)));
}
