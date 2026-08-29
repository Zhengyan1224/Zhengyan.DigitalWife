using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Speech.Tts;
using Android.Speech;
using Android.OS;
using Java.Util;
using System.Net.WebSockets;
using System.Threading.Channels;

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
    public new void Dispose() => _engine.Dispose();
}

public sealed class AndroidScriptRealtime : IDisposable
{
    private static readonly Lazy<AndroidScriptRealtime> LazyShared = new(() => new AndroidScriptRealtime());
    private ClientWebSocket? _socket;
    private readonly AndroidPcmAudioStream _audio;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _voiceCts;
    private Task? _voiceTask;
    private Task? _voiceSendTask;
    private Channel<byte[]>? _outboundAudio;
    private TaskCompletionSource<bool>? _sessionReady;
    private string _voiceTranscript = string.Empty;
    private string _inputTranscript = string.Empty;
    private AndroidScriptRealtime() { _audio = new AndroidPcmAudioStream(); }
    public static AndroidScriptRealtime Shared => LazyShared.Value;
    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public bool IsVoiceLoopRunning => _voiceTask is { IsCompleted: false };
    public string VoiceTranscript => _voiceTranscript;
    public string InputTranscript => _inputTranscript;
    public event Action<string>? TranscriptDelta;
    public event Action<string>? TranscriptCompleted;
    public event Action<string>? InputTranscriptDelta;
    public event Action<string>? InputTranscriptCompleted;
    public event Action<Exception>? VoiceError;
    public AndroidPcmAudioStream Audio => _audio;
    public bool StartMicrophone() => _audio.StartCapture();
    public void StopMicrophone() => _audio.StopCapture();
    public bool StartSpeaker() => _audio.StartPlayback();
    public void StopSpeaker() => _audio.StopPlayback();
    public void QueuePcm16(ReadOnlySpan<byte> pcm16) => _audio.QueuePlayback(pcm16);
    public event Action<ReadOnlyMemory<byte>>? PcmCaptured
    {
        add => _audio.PcmCaptured += value;
        remove => _audio.PcmCaptured -= value;
    }
    public async Task SendPcm16Async(ReadOnlyMemory<byte> pcm16, CancellationToken cancellationToken = default)
    {
        if (pcm16.Length == 0) return;
        string payload = JsonSerializer.Serialize(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(pcm16.Span) });
        await SendTextAsync(payload, cancellationToken).ConfigureAwait(false);
    }
    public Task CommitInputAudioAsync(CancellationToken cancellationToken = default) =>
        SendTextAsync("{\"type\":\"input_audio_buffer.commit\"}", cancellationToken);
    public Task CreateResponseAsync(CancellationToken cancellationToken = default) =>
        SendTextAsync(JsonSerializer.Serialize(new { type = "response.create", response = new { output_modalities = new[] { "audio" } } }), cancellationToken);
    public Task CancelResponseAsync(CancellationToken cancellationToken = default) =>
        SendTextAsync("{\"type\":\"response.cancel\"}", cancellationToken);
    public async Task ConnectAsync(string url, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        if (IsVoiceLoopRunning) await StopVoiceLoopAsync(cancellationToken).ConfigureAwait(false);
        DisposeSocket(); _socket = new ClientWebSocket();
        if (headers is not null) foreach (var pair in headers) _socket.Options.SetRequestHeader(pair.Key, pair.Value);
        await _socket.ConnectAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
    }
    public async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Realtime socket is not connected.");
        byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _socket!.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false); }
        finally { _sendLock.Release(); }
    }
    public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;
        using MemoryStream buffer = new(); byte[] chunk = new byte[8192]; WebSocketReceiveResult result;
        do { result = await _socket!.ReceiveAsync(chunk, cancellationToken).ConfigureAwait(false); if (result.MessageType == WebSocketMessageType.Close) return null; buffer.Write(chunk, 0, result.Count); } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    { await StopVoiceLoopAsync(cancellationToken).ConfigureAwait(false); if (IsConnected) await _socket!.CloseAsync(WebSocketCloseStatus.NormalClosure, "close", cancellationToken).ConfigureAwait(false); DisposeSocket(); }
    public async Task StartVoiceLoopAsync(string url, string apiKey, string model, string voice = "alloy", string instructions = "", int inputSampleRate = 24000, int outputSampleRate = 24000, CancellationToken cancellationToken = default)
    {
        try
        {
            await StartVoiceLoopCoreAsync(url, apiKey, model, voice, instructions, inputSampleRate, outputSampleRate, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopVoiceLoopAsync().ConfigureAwait(false);
            DisposeSocket();
            throw;
        }
    }
    private async Task StartVoiceLoopCoreAsync(string url, string apiKey, string model, string voice, string instructions, int inputSampleRate, int outputSampleRate, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        inputSampleRate = Math.Clamp(inputSampleRate, 8000, 48000);
        outputSampleRate = Math.Clamp(outputSampleRate, 8000, 48000);
        if (inputSampleRate != outputSampleRate)
            throw new ArgumentException("Android realtime PCM input and output must use the same sample rate.");
        await StopVoiceLoopAsync(cancellationToken).ConfigureAwait(false);
        await ConnectAsync(url, string.IsNullOrWhiteSpace(apiKey) ? null : new Dictionary<string, string> { ["Authorization"] = "Bearer " + apiKey, ["OpenAI-Beta"] = "realtime=v1" }, cancellationToken).ConfigureAwait(false);
        _audio.Reconfigure(inputSampleRate, 1);
        _sessionReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _voiceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _voiceTranscript = string.Empty;
        _inputTranscript = string.Empty;
        _outboundAudio = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _audio.PcmCaptured += OnCapturedPcm;
        _voiceTask = Task.Run(() => ReceiveVoiceLoopAsync(_voiceCts.Token), CancellationToken.None);
        _voiceSendTask = Task.Run(() => SendVoiceAudioLoopAsync(_voiceCts.Token), CancellationToken.None);
        await SendTextAsync(JsonSerializer.Serialize(new
        {
            type = "session.update",
            session = new
            {
                model,
                instructions,
                output_modalities = new[] { "audio" },
                audio = new
                {
                    input = new
                    {
                        format = new { type = "audio/pcm", rate = inputSampleRate },
                        transcription = new { model = "gpt-4o-mini-transcribe" },
                        turn_detection = (object?)new { type = "server_vad", create_response = true }
                    },
                    output = new
                    {
                        format = new { type = "audio/pcm", rate = outputSampleRate },
                        voice
                    }
                }
            }
        }), cancellationToken).ConfigureAwait(false);
        await _sessionReady.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        if (!_audio.StartPlayback() || !_audio.StartCapture())
            throw new InvalidOperationException("Android microphone or speaker could not be started.");
    }
    public async Task StopVoiceLoopAsync(CancellationToken cancellationToken = default)
    {
        _voiceCts?.Cancel();
        _outboundAudio?.Writer.TryComplete();
        if (_voiceTask is not null) { try { await _voiceTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false); } catch { } }
        if (_voiceSendTask is not null) { try { await _voiceSendTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false); } catch { } }
        _voiceTask = null; _voiceSendTask = null; _voiceCts?.Dispose(); _voiceCts = null; _outboundAudio = null;
        _audio.PcmCaptured -= OnCapturedPcm; _audio.StopAll();
    }
    private void OnCapturedPcm(ReadOnlyMemory<byte> pcm)
    {
        if (!IsConnected || !IsVoiceLoopRunning || pcm.Length == 0) return;
        _outboundAudio?.Writer.TryWrite(pcm.ToArray());
    }
    private async Task SendVoiceAudioLoopAsync(CancellationToken token)
    {
        ChannelReader<byte[]>? reader = _outboundAudio?.Reader;
        if (reader is null) return;
        try
        {
            await foreach (byte[] pcm in reader.ReadAllAsync(token).ConfigureAwait(false))
                await SendPcm16Async(pcm, token).ConfigureAwait(false);
        }
        catch (global::System.OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { VoiceError?.Invoke(ex); }
    }
    private async Task ReceiveVoiceLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && IsConnected)
            {
                string? message = await ReceiveTextAsync(token).ConfigureAwait(false); if (message is null) break;
                using JsonDocument json = JsonDocument.Parse(message); JsonElement root = json.RootElement; string type = root.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty;
                if (type is "session.created" or "session.updated")
                {
                    _sessionReady?.TrySetResult(true);
                }
                else if (type is "response.audio.delta" or "response.output_audio.delta")
                {
                    if (root.TryGetProperty("delta", out JsonElement delta) && delta.GetString() is { } encoded)
                    {
                        try { _audio.QueuePlayback(Convert.FromBase64String(encoded)); }
                        catch (FormatException ex) { VoiceError?.Invoke(ex); }
                    }
                }
                else if (type is "response.audio_transcript.delta" or "response.output_audio_transcript.delta" or "response.output_text.delta")
                {
                    string delta = root.TryGetProperty("delta", out JsonElement d) ? d.GetString() ?? string.Empty : string.Empty;
                    if (delta.Length > 0) { _voiceTranscript += delta; TranscriptDelta?.Invoke(delta); }
                }
                else if (type == "response.created")
                {
                    _voiceTranscript = string.Empty;
                }
                else if (type == "input_audio_buffer.speech_started")
                {
                    _inputTranscript = string.Empty;
                }
                else if (type == "conversation.item.input_audio_transcription.delta")
                {
                    string delta = root.TryGetProperty("delta", out JsonElement d) ? d.GetString() ?? string.Empty : string.Empty;
                    if (delta.Length > 0) { _inputTranscript += delta; InputTranscriptDelta?.Invoke(delta); }
                }
                else if (type == "conversation.item.input_audio_transcription.completed")
                {
                    string transcript = root.TryGetProperty("transcript", out JsonElement t) ? t.GetString() ?? _inputTranscript : _inputTranscript;
                    _inputTranscript = transcript;
                    InputTranscriptCompleted?.Invoke(transcript.Trim());
                }
                else if (type == "response.done") { TranscriptCompleted?.Invoke(_voiceTranscript.Trim()); }
                else if (type == "error")
                {
                    _sessionReady?.TrySetException(new InvalidOperationException(root.ToString()));
                    VoiceError?.Invoke(new InvalidOperationException(root.ToString()));
                }
            }
            _sessionReady?.TrySetException(new InvalidOperationException("Realtime connection closed before the session was ready."));
        }
        catch (global::System.OperationCanceledException) { }
        catch (Exception ex) { VoiceError?.Invoke(ex); }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _audio.PcmCaptured -= OnCapturedPcm;
                _audio.StopAll();
                _outboundAudio?.Writer.TryComplete();
            }
        }
    }
    public void Dispose()
    {
        try { StopVoiceLoopAsync().GetAwaiter().GetResult(); } catch { }
        DisposeSocket();
        _sendLock.Dispose();
    }
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
        SpeechRecognizer? recognizer = _recognizer;
        if (recognizer is null) return false;
        recognizer.SetRecognitionListener(this);
        Intent intent = new(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
        intent.PutExtra(RecognizerIntent.ExtraLanguage, string.IsNullOrWhiteSpace(language) ? Java.Util.Locale.Default.ToString() : language);
        new Handler(Looper.MainLooper!).Post(() => { recognizer.StartListening(intent); _listening = true; });
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
    public new void Dispose() { _recognizer?.Destroy(); _recognizer = null; }
    private static string Extract(Bundle? bundle) => bundle?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault() ?? string.Empty;
}
