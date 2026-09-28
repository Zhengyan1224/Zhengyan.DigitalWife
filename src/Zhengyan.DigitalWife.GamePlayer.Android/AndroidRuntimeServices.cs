using System.Net.Http.Headers;
using System.Numerics;
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
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed class AndroidScriptRealtime : IAndroidScriptRealtime, IDisposable
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

public sealed class AndroidScriptRealtimeVoice : IAndroidScriptRealtimeVoice
{
    private readonly Action<AndroidRuntimeEvent>? _dispatch;
    private readonly AndroidScriptAsr _asr;
    internal AndroidScriptRealtimeVoice(Action<AndroidRuntimeEvent>? dispatch = null) { _dispatch = dispatch; _asr = AndroidScriptAsr.Shared; }
    public bool WakeWordEnabled => _asr.IsAvailable;
    public string StartWakeWordMonitoring(AndroidScriptEntity entity, string onDetectedCallback = "", string onErrorCallback = "") => _asr.StartStreamingRecognition(entity, onCompletedCallback: onDetectedCallback, onErrorCallback: onErrorCallback, eventType: "realtime_voice");
    public void StopWakeWordMonitoring() => _asr.Stop();
    public string StartTranscription(AndroidScriptEntity entity, float timeoutSeconds = 30, string onCompletedCallback = "", string onTimeoutCallback = "", string onErrorCallback = "") => _asr.StartStreamingRecognition(entity, onCompletedCallback: onCompletedCallback, onErrorCallback: onErrorCallback, eventType: "realtime_voice");
    public void CancelRequest(string requestId) => _asr.StopStreamingRecognition(requestId);
    public string StartResponse(AndroidScriptEntity entity, string text, string onDeltaCallback = "", string onCompletedCallback = "", string onErrorCallback = "") => StartSpeakText(entity, text, 1.0f, onCompletedCallback, onErrorCallback);
    public string StartSpeakText(AndroidScriptEntity entity, string text, float speed = 1, string onCompletedCallback = "", string onErrorCallback = "")
    {
        string id = Guid.NewGuid().ToString("N");
        AndroidScriptTts.Shared.Speak(text);
        if (!string.IsNullOrWhiteSpace(onCompletedCallback)) _dispatch?.Invoke(new AndroidRuntimeEvent("realtime_voice", id, onCompletedCallback, Vector2.Zero, text, entity.Id));
        return id;
    }
    public Task ResetConversationAsync() { _asr.Cancel(); AndroidScriptTts.Shared.Stop(); return Task.CompletedTask; }
}
