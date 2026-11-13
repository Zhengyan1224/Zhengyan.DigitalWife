using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeAsrScriptEvent(string RequestId, string EventName, string Text, bool IsFinal, string Error,
    string CallbackName, double OffsetSeconds = 0, string WakeWord = "", string RecognizedText = "");
public sealed record RuntimeAsrCommand(string Operation, string RequestId = "", string EntityId = "", string PartialCallback = "",
    string CompletedCallback = "", string ErrorCallback = "", IReadOnlyList<string>? WakeWords = null)
{
    public float? ChunkDurationSeconds {get;init;}
    public float? ExtensionDurationSeconds {get;init;}
    public float? TrailingSilencePaddingSeconds {get;init;}
}

public sealed partial class RuntimeAsr : IDisposable
{
    private GameProjectAsrSettings _settings = new();
    private readonly HashSet<string> _recordings = new(StringComparer.OrdinalIgnoreCase);
    public event Action<RuntimeAsrCommand>? Command;
    public bool Enabled => _settings.Enabled;
    public string Provider => _settings.Provider;
    public bool MicrophoneInputAvailable { get; set; } = true;
    public string MicrophoneUnavailableReason { get; set; } = "";
    public int? InputDeviceIndex => _settings.InputDeviceIndex;
    public float PartialResultIntervalSeconds => _settings.PartialResultIntervalSeconds;
    public bool IsRecording => _recordings.Count > 0;
    public bool IsWakeWordMonitoring { get; private set; }
    public void Configure(GameProjectAsrSettings settings) => _settings = settings;
    public void Preload() {if(Enabled&&_settings.PreloadOnSceneLoad)Command?.Invoke(new("preload"));}
    public string StartStreamingRecognition(RuntimeEntity callbackTarget, string? requestId = null, string? onPartialCallback = "asr_partial",
        string? onCompletedCallback = "asr_completed", string? onErrorCallback = "asr_error")
    {
        if (!Enabled) throw new InvalidOperationException("ASR is disabled in the game project.");
        StopWakeWordMonitoring(); StopAllStreamingRecognitions();
        string id = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        _recordings.Add(id);
        Command?.Invoke(new("start", id, callbackTarget.Id, onPartialCallback ?? "", onCompletedCallback ?? "", onErrorCallback ?? ""));
        return id;
    }
    public void StopStreamingRecognition(string? requestId=null) { if(requestId is null){StopAllStreamingRecognitions();return;}if (_recordings.Remove(requestId)) Command?.Invoke(new("stop", requestId)); }
    public void StopAllStreamingRecognitions() { foreach (string id in _recordings.ToArray()) StopStreamingRecognition(id); }
    public string StartWakeWordMonitoring(RuntimeEntity callbackTarget, IEnumerable<string> wakeWords, string? requestId = null,
        float? chunkDurationSeconds = null, float? extensionDurationSeconds = null, float? trailingSilencePaddingSeconds = null,
        string? onDetectedCallback = "wake_detected", string? onErrorCallback = "wake_error")
    {
        if (!Enabled) throw new InvalidOperationException("ASR is disabled in the game project.");
        StopAllStreamingRecognitions(); StopWakeWordMonitoring(); IsWakeWordMonitoring = true;
        string id = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        Command?.Invoke(new("wake", id, callbackTarget.Id, "", onDetectedCallback ?? "", onErrorCallback ?? "", wakeWords.Where(w => !string.IsNullOrWhiteSpace(w)).ToArray())
        {ChunkDurationSeconds=chunkDurationSeconds,ExtensionDurationSeconds=extensionDurationSeconds,TrailingSilencePaddingSeconds=trailingSilencePaddingSeconds});
        return id;
    }
    public string StartWakeWordMonitoring(RuntimeEntity callbackTarget,IEnumerable<string> wakeWords,string? requestId,
        float chunkDurationSeconds,float extensionDurationSeconds,float trailingSilencePaddingSeconds,string? onDetectedCallback,string? onErrorCallback)
        =>StartWakeWordMonitoring(callbackTarget,wakeWords,requestId,(float?)chunkDurationSeconds,extensionDurationSeconds,trailingSilencePaddingSeconds,onDetectedCallback,onErrorCallback);
    public void StopWakeWordMonitoring() { if (!IsWakeWordMonitoring) return; IsWakeWordMonitoring = false; Command?.Invoke(new("stop_wake")); }
    public void Report(RuntimeAsrScriptEvent value) { if (value.IsFinal || value.EventName == "error") _recordings.Remove(value.RequestId); }
    public void Dispose() { StopAllStreamingRecognitions(); StopWakeWordMonitoring(); Command = null; }
}
