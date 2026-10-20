using Zhengyan.DigitalWife.GamePlayer;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed class AndroidScriptRealtimeVoice : IAndroidScriptRealtimeVoice
{
    private readonly RuntimeRealtimeVoice _runtime;
    internal AndroidScriptRealtimeVoice(RuntimeRealtimeVoice runtime) => _runtime = runtime;
    public bool Enabled => _runtime.Enabled;
    public string BaseUrl => _runtime.BaseUrl;
    public string Model => _runtime.Model;
    public string Voice => _runtime.Voice;
    public bool WakeWordEnabled => _runtime.WakeWordEnabled;
    public IReadOnlyList<string> WakeWords => _runtime.WakeWords;
    public bool MicrophoneInputAvailable => _runtime.MicrophoneInputAvailable;
    public string MicrophoneUnavailableReason => _runtime.MicrophoneUnavailableReason;
    public bool IsWakeWordMonitoring => _runtime.IsWakeWordMonitoring;
    public int? InputDeviceIndex => _runtime.InputDeviceIndex;

    public string StartWakeWordMonitoring(AndroidScriptEntity entity, string onDetectedCallback = "wake_word_detected", string onErrorCallback = "wake_word_error")
    {
        _runtime.StartWakeWordMonitoring(entity, onDetectedCallback, onErrorCallback);
        return string.Empty;
    }
    public void StopWakeWordMonitoring() => _runtime.StopWakeWordMonitoring();

    // Retain the published Android signatures while exposing request IDs through
    // additional overloads matching the desktop API.
    public string StartTranscription(AndroidScriptEntity entity, float timeoutSeconds,
        string onCompletedCallback = "realtime_voice_transcription_completed", string onTimeoutCallback = "realtime_voice_timeout", string onErrorCallback = "realtime_voice_error")
        => _runtime.StartTranscription(entity, timeoutSeconds: timeoutSeconds, onCompletedCallback: onCompletedCallback, onTimeoutCallback: onTimeoutCallback, onErrorCallback: onErrorCallback);
    public string StartTranscription(AndroidScriptEntity entity, string? requestId = null, float? timeoutSeconds = null,
        string? onCompletedCallback = "realtime_voice_transcription_completed", string? onTimeoutCallback = "realtime_voice_timeout", string? onErrorCallback = "realtime_voice_error")
        => _runtime.StartTranscription(entity, requestId, timeoutSeconds, onCompletedCallback, onTimeoutCallback, onErrorCallback);
    public string StartResponse(AndroidScriptEntity entity, string text, string onDeltaCallback = "realtime_voice_delta",
        string onCompletedCallback = "realtime_voice_completed", string onErrorCallback = "realtime_voice_error")
        => _runtime.StartResponse(entity, text, onDeltaCallback: onDeltaCallback, onCompletedCallback: onCompletedCallback, onErrorCallback: onErrorCallback);
    public string StartResponse(AndroidScriptEntity entity, string userText, string? requestId,
        string? onDeltaCallback = "realtime_voice_delta", string? onCompletedCallback = "realtime_voice_completed", string? onErrorCallback = "realtime_voice_error")
        => _runtime.StartResponse(entity, userText, requestId, onDeltaCallback, onCompletedCallback, onErrorCallback);
    public string StartSpeakText(AndroidScriptEntity entity, string text, float speed,
        string onCompletedCallback = "realtime_voice_speech_completed", string onErrorCallback = "realtime_voice_error")
        => _runtime.StartSpeakText(entity, text, speed, onCompletedCallback: onCompletedCallback, onErrorCallback: onErrorCallback);
    public string StartSpeakText(AndroidScriptEntity entity, string text, float? speed = null, string? requestId = null,
        string? onCompletedCallback = "realtime_voice_speech_completed", string? onErrorCallback = "realtime_voice_error")
        => _runtime.StartSpeakText(entity, text, speed, requestId, onCompletedCallback, onErrorCallback);
    public string StartVoiceTurn(AndroidScriptEntity entity, string? requestId = null, float? timeoutSeconds = 30,
        string? onTranscriptionCompletedCallback = "realtime_voice_transcription_completed", string? onDeltaCallback = "realtime_voice_delta",
        string? onCompletedCallback = "realtime_voice_completed", string? onTimeoutCallback = "realtime_voice_timeout", string? onErrorCallback = "realtime_voice_error")
        => _runtime.StartVoiceTurn(entity, requestId, timeoutSeconds, onTranscriptionCompletedCallback, onDeltaCallback, onCompletedCallback, onTimeoutCallback, onErrorCallback);
    public void CancelRequest(string requestId) => _runtime.CancelRequest(requestId);
    public void CancelAllRequests() => _runtime.CancelAllRequests();
    public Task ResetConversationAsync() => _runtime.ResetConversationAsync();
    public Task ResetConversationAsync(CancellationToken cancellationToken) => _runtime.ResetConversationAsync(cancellationToken);
}
