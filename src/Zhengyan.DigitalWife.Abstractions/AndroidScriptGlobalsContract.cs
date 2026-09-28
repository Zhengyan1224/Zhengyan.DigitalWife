namespace Zhengyan.DigitalWife.GameProjects;

/// <summary>Legacy globals contract retained for previously published Android scripts.</summary>
// New exports use the typed AndroidScriptGlobals in GamePlayer.Android.Scripting.
// Its implementation still fills this base contract for older DLLs.
public class AndroidScriptGlobalsContract
{
    public dynamic? Scene { get; protected set; }
    public dynamic? Entity { get; protected set; }
    public dynamic? Input { get; protected set; }
    public dynamic? Audio { get; protected set; }
    public dynamic? Network { get; protected set; }
    public dynamic? Save { get; protected set; }
    public dynamic? Llm { get; protected set; }
    public dynamic? Tts { get; protected set; }
    public dynamic? Asr { get; protected set; }
    public dynamic? Realtime { get; protected set; }
    public dynamic? Event { get; protected set; }
    public dynamic? Services { get; protected set; }
    public float DeltaSeconds { get; protected set; }
    public bool IsStart { get; protected set; }
    public bool IsUpdate { get; protected set; }
    public bool IsEvent => Event is not null;
    public bool IsGuiEvent { get; protected set; }
    public bool IsSpriteEvent { get; protected set; }
    public bool IsTrayMenuEvent { get; protected set; }
    public bool IsLoadingEvent { get; protected set; }
    public bool IsSpeechEvent { get; protected set; }
    public bool IsLlmEvent { get; protected set; }
    public bool IsAsrEvent { get; protected set; }
    public bool IsRealtimeVoiceEvent { get; protected set; }
    public dynamic? LlmEvent { get; protected set; }
    public string LlmRequestId { get; protected set; } = string.Empty;
    public string LlmEventName { get; protected set; } = string.Empty;
    public string LlmDelta { get; protected set; } = string.Empty;
    public string LlmText { get; protected set; } = string.Empty;
    public bool LlmIsFinal { get; protected set; }
    public string LlmError { get; protected set; } = string.Empty;
    public string LlmCallbackName { get; protected set; } = string.Empty;
    public dynamic? LlmToolCall { get; protected set; }
    public string LlmToolCallId { get; protected set; } = string.Empty;
    public string LlmToolName { get; protected set; } = string.Empty;
    public string LlmToolArgumentsJson { get; protected set; } = string.Empty;
    public string LlmToolResult { get; protected set; } = string.Empty;
    public string LoadingEventName { get; protected set; } = string.Empty;
    public float LoadingProgress { get; protected set; }
    public string LoadingMessage { get; protected set; } = string.Empty;
    public string AsrRequestId { get; protected set; } = string.Empty;
    public string AsrEventName { get; protected set; } = string.Empty;
    public string AsrText { get; protected set; } = string.Empty;
    public bool AsrIsFinal { get; protected set; }
    public string AsrError { get; protected set; } = string.Empty;
    public string AsrCallbackName { get; protected set; } = string.Empty;
    public string RealtimeVoiceRequestId { get; protected set; } = string.Empty;
    public string RealtimeVoiceEventName { get; protected set; } = string.Empty;
    public string RealtimeVoiceText { get; protected set; } = string.Empty;
    public string RealtimeVoiceDelta { get; protected set; } = string.Empty;
    public string RealtimeVoiceAccumulatedText { get; protected set; } = string.Empty;
    public bool RealtimeVoiceIsFinal { get; protected set; }
    public string RealtimeVoiceError { get; protected set; } = string.Empty;
    public string RealtimeVoiceCallbackName { get; protected set; } = string.Empty;
    public string SpriteId { get; protected set; } = string.Empty;
    public string SpriteName { get; protected set; } = string.Empty;
    public string SpriteEventName { get; protected set; } = string.Empty;
    public string TrayMenuItemId { get; protected set; } = string.Empty;
    public string TrayMenuItemText { get; protected set; } = string.Empty;
    public string TrayMenuEventName { get; protected set; } = string.Empty;
    public string GuiControlId { get; protected set; } = string.Empty;
    public string GuiControlName { get; protected set; } = string.Empty;
    public string GuiEventName { get; protected set; } = string.Empty;
    public string SpeechCallbackName { get; protected set; } = string.Empty;
}
