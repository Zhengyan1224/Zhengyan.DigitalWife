using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeRealtimeVoiceScriptEvent(string RequestId, string EventName, string Text, string Delta,
    string AccumulatedText, bool IsFinal, string Error, string CallbackName, string WakeWord = "", string RecognizedText = "");
public sealed record RuntimeRealtimeVoiceCommand(string Operation, string EntityId = "", string RequestId = "", string Text = "",
    float TimeoutSeconds = 30, float Speed = 1, string DeltaCallback = "", string CompletedCallback = "", string ErrorCallback = "",
    string TranscriptionCallback = "", string TimeoutCallback = "");

public sealed partial class RuntimeRealtimeVoice : IDisposable
{
    private GameProjectRealtimeVoiceSettings _settings = new();
    public event Action<RuntimeRealtimeVoiceCommand>? Command;
    public bool Enabled => _settings.Enabled;
    public string BaseUrl => _settings.BaseUrl;
    public string Model => _settings.Model;
    public string Voice => _settings.Voice;
    public int? InputDeviceIndex=>_settings.InputDeviceIndex;
    public bool WakeWordEnabled => Enabled && _settings.WakeWord.Enabled;
    public IReadOnlyList<string> WakeWords => _settings.WakeWord.Keywords;
    public bool MicrophoneInputAvailable { get; set; } = true;
    public string MicrophoneUnavailableReason { get; set; } = "";
    public bool IsWakeWordMonitoring { get; private set; }
    public void Configure(GameProjectRealtimeVoiceSettings settings) => _settings = settings;
    private string Id(string? id) => string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    private string Send(RuntimeRealtimeVoiceCommand command)
    {
        if (!Enabled) throw new InvalidOperationException("Realtime Voice is disabled in the game project.");
        Command?.Invoke(command);return command.RequestId;
    }
    public string StartWakeWordMonitoring(RuntimeEntity callbackTarget, string? requestId,
        string? onDetectedCallback, string? onErrorCallback)
    {
        IsWakeWordMonitoring = true;
        return Send(new("wake",callbackTarget.Id,Id(requestId),CompletedCallback:onDetectedCallback??"",ErrorCallback:onErrorCallback??""));
    }
    public void StartWakeWordMonitoring(RuntimeEntity callbackTarget,string onDetectedCallback="wake_word_detected",string onErrorCallback="wake_word_error")
        =>StartWakeWordMonitoring(callbackTarget,null,onDetectedCallback,onErrorCallback);
    public string StartWakeWordMonitoringRequest(RuntimeEntity callbackTarget,string? requestId=null,string? onDetectedCallback="realtime_voice_wake_detected",string? onErrorCallback="realtime_voice_error")
        =>StartWakeWordMonitoring(callbackTarget,requestId,onDetectedCallback,onErrorCallback);
    public void StopWakeWordMonitoring() { if(!IsWakeWordMonitoring)return;IsWakeWordMonitoring=false;Command?.Invoke(new("stop_wake")); }
    public string StartTranscription(RuntimeEntity callbackTarget, string? requestId = null, float? timeoutSeconds = null,
        string? onCompletedCallback = "realtime_voice_transcription_completed", string? onTimeoutCallback = "realtime_voice_timeout", string? onErrorCallback = "realtime_voice_error")
    {
        StopWakeWordMonitoring();return Send(new("transcribe",callbackTarget.Id,Id(requestId),TimeoutSeconds:timeoutSeconds??30,
            CompletedCallback:onCompletedCallback??"",ErrorCallback:onErrorCallback??"",TimeoutCallback:onTimeoutCallback??""));
    }
    public string StartResponse(RuntimeEntity callbackTarget,string userText,string? requestId=null,string? onDeltaCallback="realtime_voice_delta",
        string? onCompletedCallback="realtime_voice_completed",string? onErrorCallback="realtime_voice_error")
    {
        StopWakeWordMonitoring();return Send(new("response",callbackTarget.Id,Id(requestId),userText,DeltaCallback:onDeltaCallback??"",CompletedCallback:onCompletedCallback??"",ErrorCallback:onErrorCallback??""));
    }
    public string StartSpeakText(RuntimeEntity callbackTarget,string text,float? speed=null,string? requestId=null,
        string? onCompletedCallback="realtime_voice_speech_completed",string? onErrorCallback="realtime_voice_error")
    {
        StopWakeWordMonitoring();return Send(new("speak",callbackTarget.Id,Id(requestId),text,Speed:speed??_settings.PromptSpeed,CompletedCallback:onCompletedCallback??"",ErrorCallback:onErrorCallback??""));
    }
    public string StartVoiceTurn(RuntimeEntity callbackTarget,string? requestId=null,float? timeoutSeconds=30,
        string? onTranscriptionCompletedCallback="realtime_voice_transcription_completed",string? onDeltaCallback="realtime_voice_delta",
        string? onCompletedCallback="realtime_voice_completed",string? onTimeoutCallback="realtime_voice_timeout",string? onErrorCallback="realtime_voice_error")
    {
        StopWakeWordMonitoring();return Send(new("turn",callbackTarget.Id,Id(requestId),TimeoutSeconds:timeoutSeconds??30,DeltaCallback:onDeltaCallback??"",
            CompletedCallback:onCompletedCallback??"",ErrorCallback:onErrorCallback??"",TranscriptionCallback:onTranscriptionCompletedCallback??"",TimeoutCallback:onTimeoutCallback??""));
    }
    public void CancelRequest(string requestId) => Command?.Invoke(new("cancel",RequestId:requestId));
    public void CancelAllRequests() { StopWakeWordMonitoring();Command?.Invoke(new("cancel_all")); }
    public Task ResetConversationAsync(CancellationToken cancellationToken=default) { cancellationToken.ThrowIfCancellationRequested();Command?.Invoke(new("reset"));return Task.CompletedTask; }
    public void Dispose() { CancelAllRequests();Command=null; }
}
