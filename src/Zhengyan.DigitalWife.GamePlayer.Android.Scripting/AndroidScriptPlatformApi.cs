using Zhengyan.DigitalWife.Mmd.Game.Input;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

// These platform contracts are also the editor's compilation surface. Native
// implementations live in the Android application, never in published scripts.
public abstract class AndroidScriptInputApi
{
    public virtual bool IsTouchAvailable => false;
    public virtual float TouchSlopPixels => 12.0f;
    public bool HasTouch => TouchCount > 0;
    public int TouchCount => Touches.Count;
    public int ActiveTouchCount => Touches.Count(touch => touch.IsActive);
    public bool IsTouchDown => ActiveTouchCount > 0;
    public virtual bool IsTouchStarted => TouchTransitions.Any(touch => touch.Phase == TouchPhase.Started);
    public virtual bool IsTouchEnded => TouchTransitions.Any(touch => touch.IsEnded);
    public virtual IReadOnlyList<TouchPoint> Touches => Array.Empty<TouchPoint>();
    public virtual IReadOnlyList<TouchPoint> TouchTransitions => Touches;
    public TouchPoint? PrimaryTouch => Touches.Count == 0 ? null
        : Touches.OrderBy(touch => touch.IsActive ? 0 : 1).ThenBy(touch => touch.Id).First();
    public bool TryGetTouch(int id, out TouchPoint touch)
    {
        foreach (TouchPoint point in Touches)
        {
            if (point.Id != id) continue;
            touch = point;
            return true;
        }
        touch = default;
        return false;
    }

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
    public virtual bool IsCursorVisible=>CursorVisible;
    public virtual bool CursorLocked {get=>IsCursorLocked;set=>SetCursorLocked(value);}
    public virtual bool IsCursorLocked=>false;
    public virtual bool IsRawMouseInput=>false;
    public virtual string CursorMode=>IsCursorLocked?"locked":CursorVisible?"normal":"hidden";
    public bool IsAltDown=>IsKeyDown("AltLeft")||IsKeyDown("AltRight");
    public bool IsControlDown=>IsKeyDown("ControlLeft")||IsKeyDown("ControlRight");
    public bool IsShiftDown=>IsKeyDown("ShiftLeft")||IsKeyDown("ShiftRight");
    public virtual int GamepadIndex=>HasGamepad?0:-1;
    public void HideCursor()=>SetCursorVisible(false);
    public void ShowCursor()=>SetCursorVisible(true);
    public void SetCursorVisible(bool visible)=>TrySetCursorVisible(visible);
    public virtual bool TrySetCursorVisible(bool visible){CursorVisible=visible;return true;}
    public void SetCursorLocked(bool locked,bool rawInput=false)=>TrySetCursorLocked(locked,rawInput);
    public virtual bool TrySetCursorLocked(bool locked,bool rawInput=false)=>!locked;
    public void LockCursor(bool rawInput=false)=>SetCursorLocked(true,rawInput);
    public void UnlockCursor()=>SetCursorLocked(false);
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
    public abstract string ClipboardText { get; set; }
    public bool TryGetClipboardText(out string text){text=ClipboardText;return text.Length>0;}
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
    bool IsRecording => IsListening && !IsWakeWordMonitoring;
    string Provider => "sherpa";
    int? InputDeviceIndex => null;
    float PartialResultIntervalSeconds => .2f;
    bool MicrophoneInputAvailable => IsAvailable;
    string MicrophoneUnavailableReason => IsAvailable ? string.Empty : "No usable microphone input is available.";
    void Preload() { }
    void StopAllStreamingRecognitions() => Stop();
    string LastText { get; }
    event Action<string>? PartialResult;
    event Action<string>? Result;
    event Action<int>? Error;
    string StartStreamingRecognition(AndroidScriptEntity callbackTarget, string? requestId = null, string? onPartialCallback = "asr_partial", string? onCompletedCallback = "asr_completed", string? onErrorCallback = "asr_error", string eventType = "asr");
    string StartWakeWordMonitoring(AndroidScriptEntity callbackTarget, IEnumerable<string> wakeWords, string? requestId = null, float? chunkDurationSeconds = null, float? extensionDurationSeconds = null, float? trailingSilencePaddingSeconds = null, string? onDetectedCallback = "asr_wake_word_detected", string? onErrorCallback = "asr_wake_word_error");
    bool IsWakeWordMonitoring { get; }
    void StopWakeWordMonitoring();
    void StopStreamingRecognition(string? requestId = null);
    bool Start(string language = "");
    void Stop();
    void Cancel();
    void Dispose() => Cancel();
}

public interface IAndroidScriptRealtimeVoice
{
    bool Enabled { get; }
    string BaseUrl { get; }
    string Model { get; }
    string Voice { get; }
    bool WakeWordEnabled { get; }
    IReadOnlyList<string> WakeWords { get; }
    bool MicrophoneInputAvailable { get; }
    string MicrophoneUnavailableReason { get; }
    bool IsWakeWordMonitoring { get; }
    int? InputDeviceIndex { get; }
    string StartWakeWordMonitoring(AndroidScriptEntity callbackTarget, string onDetectedCallback = "wake_word_detected", string onErrorCallback = "wake_word_error");
    void StopWakeWordMonitoring();
    string StartTranscription(AndroidScriptEntity callbackTarget, float timeoutSeconds, string onCompletedCallback = "realtime_voice_transcription_completed", string onTimeoutCallback = "realtime_voice_timeout", string onErrorCallback = "realtime_voice_error");
    string StartTranscription(AndroidScriptEntity callbackTarget, string? requestId = null, float? timeoutSeconds = null, string? onCompletedCallback = "realtime_voice_transcription_completed", string? onTimeoutCallback = "realtime_voice_timeout", string? onErrorCallback = "realtime_voice_error");
    void CancelRequest(string requestId);
    void CancelAllRequests();
    string StartResponse(AndroidScriptEntity callbackTarget, string userText, string onDeltaCallback = "realtime_voice_delta", string onCompletedCallback = "realtime_voice_completed", string onErrorCallback = "realtime_voice_error");
    string StartResponse(AndroidScriptEntity callbackTarget, string userText, string? requestId, string? onDeltaCallback = "realtime_voice_delta", string? onCompletedCallback = "realtime_voice_completed", string? onErrorCallback = "realtime_voice_error");
    string StartSpeakText(AndroidScriptEntity callbackTarget, string text, float speed, string onCompletedCallback = "realtime_voice_speech_completed", string onErrorCallback = "realtime_voice_error");
    string StartSpeakText(AndroidScriptEntity callbackTarget, string text, float? speed = null, string? requestId = null, string? onCompletedCallback = "realtime_voice_speech_completed", string? onErrorCallback = "realtime_voice_error");
    string StartVoiceTurn(AndroidScriptEntity callbackTarget, string? requestId = null, float? timeoutSeconds = 30, string? onTranscriptionCompletedCallback = "realtime_voice_transcription_completed", string? onDeltaCallback = "realtime_voice_delta", string? onCompletedCallback = "realtime_voice_completed", string? onTimeoutCallback = "realtime_voice_timeout", string? onErrorCallback = "realtime_voice_error");
    Task ResetConversationAsync();
    Task ResetConversationAsync(CancellationToken cancellationToken);
    void Dispose() { StopWakeWordMonitoring(); CancelAllRequests(); }
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
