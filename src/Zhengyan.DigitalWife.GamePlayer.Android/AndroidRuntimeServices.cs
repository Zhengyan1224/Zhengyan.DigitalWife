using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Android.App;
using Android.Speech.Tts;
using Android.Speech;
using Android.OS;
using Java.Util;
using System.Net.WebSockets;

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

public sealed class AndroidScriptLlm
{
    private readonly AndroidScriptNetwork _network;
    internal AndroidScriptLlm(AndroidScriptNetwork network) => _network = network;
    public async Task<string> ChatAsync(string baseUrl, string model, string prompt, string apiKey = "", float? temperature = null, int timeoutSeconds = 60)
    {
        string url = baseUrl.TrimEnd('/') + "/v1/chat/completions";
        var payload = new { model, messages = new[] { new { role = "user", content = prompt } }, temperature, stream = false };
        Dictionary<string, string> headers = string.IsNullOrWhiteSpace(apiKey) ? [] : new(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = "Bearer " + apiKey };
        AndroidHttpResponse response = await _network.PostJsonAsync(url, payload, timeoutSeconds, headers);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"LLM request failed ({response.StatusCode}): {response.Body}");
        using JsonDocument json = JsonDocument.Parse(response.Body);
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}

public sealed class AndroidScriptTts : Java.Lang.Object, TextToSpeech.IOnInitListener, IDisposable
{
    private static readonly Lazy<AndroidScriptTts> LazyShared = new(() => new AndroidScriptTts());
    private readonly TextToSpeech _engine;
    private bool _ready;
    private AndroidScriptTts() { _engine = new TextToSpeech(Application.Context, this); }
    public static AndroidScriptTts Shared => LazyShared.Value;
    public bool IsReady => _ready;
    public void OnInit(OperationResult status) => _ready = status == OperationResult.Success;
    public bool Speak(string text, string? language = null, bool flush = true)
    {
        if (!_ready || string.IsNullOrWhiteSpace(text)) return false;
        if (!string.IsNullOrWhiteSpace(language)) _engine.SetLanguage(Locale.ForLanguageTag(language));
        OperationResult result = _engine.Speak(text, flush ? QueueMode.Flush : QueueMode.Add, null, Guid.NewGuid().ToString("N"));
        return result == OperationResult.Success;
    }
    public void Stop() { if (_ready) _engine.Stop(); }
    public void Dispose() => _engine.Dispose();
}

public sealed class AndroidScriptRealtime : IDisposable
{
    private static readonly Lazy<AndroidScriptRealtime> LazyShared = new(() => new AndroidScriptRealtime());
    private ClientWebSocket? _socket;
    public static AndroidScriptRealtime Shared => LazyShared.Value;
    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public async Task ConnectAsync(string url, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        DisposeSocket(); _socket = new ClientWebSocket();
        if (headers is not null) foreach (var pair in headers) _socket.Options.SetRequestHeader(pair.Key, pair.Value);
        await _socket.ConnectAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
    }
    public async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Realtime socket is not connected.");
        byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        await _socket!.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }
    public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;
        using MemoryStream buffer = new(); byte[] chunk = new byte[8192]; WebSocketReceiveResult result;
        do { result = await _socket!.ReceiveAsync(chunk, cancellationToken).ConfigureAwait(false); if (result.MessageType == WebSocketMessageType.Close) return null; buffer.Write(chunk, 0, result.Count); } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    { if (IsConnected) await _socket!.CloseAsync(WebSocketCloseStatus.NormalClosure, "close", cancellationToken).ConfigureAwait(false); DisposeSocket(); }
    public void Dispose() => DisposeSocket();
    private void DisposeSocket() { _socket?.Dispose(); _socket = null; }
}

public sealed class AndroidScriptAsr : Java.Lang.Object, IRecognitionListener, IDisposable
{
    private static readonly Lazy<AndroidScriptAsr> LazyShared = new(() => new AndroidScriptAsr());
    private SpeechRecognizer? _recognizer;
    private bool _listening;
    private AndroidScriptAsr() { }
    public static AndroidScriptAsr Shared => LazyShared.Value;
    public bool IsAvailable => SpeechRecognizer.IsRecognitionAvailable(Application.Context);
    public bool IsListening => _listening;
    public string LastText { get; private set; } = string.Empty;
    public event Action<string>? PartialResult;
    public event Action<string>? Result;
    public event Action<int>? Error;
    public bool Start(string language = "")
    {
        if (!IsAvailable || _listening) return false;
        _recognizer ??= SpeechRecognizer.CreateSpeechRecognizer(Application.Context);
        _recognizer.SetRecognitionListener(this);
        Intent intent = new(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
        intent.PutExtra(RecognizerIntent.ExtraLanguage, string.IsNullOrWhiteSpace(language) ? Java.Util.Locale.Default.ToString() : language);
        new Handler(Looper.MainLooper!).Post(() => { _recognizer?.StartListening(intent); _listening = true; });
        return true;
    }
    public void Stop() { new Handler(Looper.MainLooper!).Post(() => { _recognizer?.StopListening(); _listening = false; }); }
    public void Cancel() { new Handler(Looper.MainLooper!).Post(() => { _recognizer?.Cancel(); _listening = false; }); }
    public void OnResults(Bundle? results) { _listening = false; LastText = Extract(results); Result?.Invoke(LastText); }
    public void OnPartialResults(Bundle? partialResults) { string text = Extract(partialResults); if (text.Length > 0) { LastText = text; PartialResult?.Invoke(text); } }
    public void OnError(SpeechRecognizerError error) { _listening = false; Error?.Invoke((int)error); }
    public void OnBeginningOfSpeech() { }
    public void OnBufferReceived(byte[]? buffer) { }
    public void OnEndOfSpeech() { }
    public void OnEvent(int eventType, Bundle? @params) { }
    public void OnReadyForSpeech(Bundle? @params) { }
    public void OnRmsChanged(float rmsdB) { }
    public void Dispose() { _recognizer?.Destroy(); _recognizer = null; }
    private static string Extract(Bundle? bundle) => bundle?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault() ?? string.Empty;
}
