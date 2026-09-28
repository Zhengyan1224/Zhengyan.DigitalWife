namespace Zhengyan.DigitalWife.GamePlayer.Android;

// These platform contracts are also the editor's compilation surface. Native
// implementations live in the Android application, never in published scripts.
public abstract class AndroidScriptInputApi
{
    public abstract bool IsKeyDown(string key);
    public abstract bool IsKeyPressed(string key);
    public abstract bool IsKeyReleased(string key);
    public abstract float MouseX { get; }
    public abstract float MouseY { get; }
    public abstract float MouseDeltaX { get; }
    public abstract float MouseDeltaY { get; }
    public abstract float ScrollX { get; }
    public abstract float ScrollY { get; }
    public abstract bool CursorVisible { get; set; }
    public abstract bool IsMouseButtonDown(string button);
    public abstract bool IsMouseButtonPressed(string button);
    public abstract bool IsMouseButtonReleased(string button);
    public abstract bool HasGamepad { get; }
    public abstract string GamepadName { get; }
    public abstract float LeftStickX { get; }
    public abstract float LeftStickY { get; }
    public abstract float RightStickX { get; }
    public abstract float RightStickY { get; }
    public abstract float LeftTrigger { get; }
    public abstract float RightTrigger { get; }
    public abstract bool IsGamepadButtonDown(string button);
    public abstract string ClipboardText { get; }
    public abstract bool HasClipboardText { get; }
    public abstract bool TrySetClipboardText(string text);
    public abstract void SetClipboardText(string text);
}

public interface IAndroidScriptTts
{
    bool IsReady { get; }
    bool Speak(string text, string? language = null, bool flush = true);
    Task SpeakAsync(string text, float speed = 1.0f, float volume = 1.0f, CancellationToken cancellationToken = default);
    void Stop();
}

public interface IAndroidScriptAsr
{
    bool Enabled { get; }
    bool IsAvailable { get; }
    bool IsListening { get; }
    string LastText { get; }
    event Action<string>? PartialResult;
    event Action<string>? Result;
    event Action<int>? Error;
    string StartStreamingRecognition(AndroidScriptEntity entity, string? requestId = null, string? onPartialCallback = "asr_partial", string? onCompletedCallback = "asr_completed", string? onErrorCallback = "asr_error", string eventType = "asr");
    string StartWakeWordMonitoring(AndroidScriptEntity entity, IEnumerable<string> wakeWords, string? requestId = null, float? chunkDurationSeconds = null, float? extensionDurationSeconds = null, float? trailingSilencePaddingSeconds = null, string? onDetectedCallback = "asr_wake_word_detected", string? onErrorCallback = "asr_wake_word_error");
    bool IsWakeWordMonitoring { get; }
    void StopWakeWordMonitoring();
    void StopStreamingRecognition(string requestId);
    bool Start(string language = "");
    void Stop();
    void Cancel();
}

public interface IAndroidScriptRealtimeVoice
{
    bool WakeWordEnabled { get; }
    string StartWakeWordMonitoring(AndroidScriptEntity entity, string onDetectedCallback = "", string onErrorCallback = "");
    void StopWakeWordMonitoring();
    string StartTranscription(AndroidScriptEntity entity, float timeoutSeconds = 30, string onCompletedCallback = "", string onTimeoutCallback = "", string onErrorCallback = "");
    void CancelRequest(string requestId);
    string StartResponse(AndroidScriptEntity entity, string text, string onDeltaCallback = "", string onCompletedCallback = "", string onErrorCallback = "");
    string StartSpeakText(AndroidScriptEntity entity, string text, float speed = 1, string onCompletedCallback = "", string onErrorCallback = "");
    Task ResetConversationAsync();
}

public interface IAndroidScriptRealtime
{
    bool IsConnected { get; }
    bool IsVoiceLoopRunning { get; }
    string VoiceTranscript { get; }
    string InputTranscript { get; }
    event Action<string>? TranscriptDelta;
    event Action<string>? TranscriptCompleted;
    event Action<string>? InputTranscriptDelta;
    event Action<string>? InputTranscriptCompleted;
    event Action<Exception>? VoiceError;
    event Action<ReadOnlyMemory<byte>>? PcmCaptured;
    bool StartMicrophone();
    void StopMicrophone();
    bool StartSpeaker();
    void StopSpeaker();
    void QueuePcm16(ReadOnlySpan<byte> pcm16);
    Task SendPcm16Async(ReadOnlyMemory<byte> pcm16, CancellationToken cancellationToken = default);
    Task CommitInputAudioAsync(CancellationToken cancellationToken = default);
    Task CreateResponseAsync(CancellationToken cancellationToken = default);
    Task CancelResponseAsync(CancellationToken cancellationToken = default);
    Task ConnectAsync(string url, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default);
    Task SendTextAsync(string text, CancellationToken cancellationToken = default);
    Task<string?> ReceiveTextAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task StartVoiceLoopAsync(string url, string apiKey, string model, string voice = "alloy", string instructions = "", int inputSampleRate = 24000, int outputSampleRate = 24000, CancellationToken cancellationToken = default);
    Task StopVoiceLoopAsync(CancellationToken cancellationToken = default);
}
