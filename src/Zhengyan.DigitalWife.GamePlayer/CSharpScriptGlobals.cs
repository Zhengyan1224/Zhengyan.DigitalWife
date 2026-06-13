namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class CSharpScriptGlobals
{
    public required RuntimeEntity Entity { get; init; }

    public required RuntimeScene Scene { get; init; }

    public required RuntimeInput Input { get; init; }

    public required RuntimeAudio Audio { get; init; }

    public double DeltaSeconds { get; init; }

    public bool IsStart { get; init; }

    public bool IsUpdate { get; init; }

    public bool IsGuiEvent { get; init; }

    public bool IsSpriteEvent { get; init; }

    public bool IsTrayMenuEvent { get; init; }

    public bool IsLoadingEvent { get; init; }

    public bool IsSpeechEvent { get; init; }

    public bool IsLlmEvent { get; init; }

    public bool IsAsrEvent { get; init; }

    public bool IsRealtimeVoiceEvent { get; init; }

    public RuntimeLlmScriptEvent? LlmEvent { get; init; }

    public RuntimeAsrScriptEvent? AsrEvent { get; init; }

    public RuntimeRealtimeVoiceScriptEvent? RealtimeVoiceEvent { get; init; }

    public string LlmRequestId => LlmEvent?.RequestId ?? string.Empty;

    public string LlmEventName => LlmEvent?.EventName ?? string.Empty;

    public string LlmDelta => LlmEvent?.Delta ?? string.Empty;

    public string LlmText => LlmEvent?.AccumulatedText ?? string.Empty;

    public bool LlmIsFinal => LlmEvent?.IsFinal ?? false;

    public string LlmError => LlmEvent?.Error ?? string.Empty;

    public string LlmCallbackName => LlmEvent?.CallbackName ?? string.Empty;

    public RuntimeLlmToolCall? LlmToolCall => LlmEvent?.ToolCall;

    public string LlmToolCallId => LlmEvent?.ToolCall?.Id ?? string.Empty;

    public string LlmToolName => LlmEvent?.ToolCall?.Name ?? string.Empty;

    public string LlmToolArgumentsJson => LlmEvent?.ToolCall?.ArgumentsJson ?? string.Empty;

    public string LlmToolResult => LlmEvent?.ToolResult ?? string.Empty;

    public string AsrRequestId => AsrEvent?.RequestId ?? string.Empty;

    public string AsrEventName => AsrEvent?.EventName ?? string.Empty;

    public string AsrText => AsrEvent?.Text ?? string.Empty;

    public bool AsrIsFinal => AsrEvent?.IsFinal ?? false;

    public string AsrError => AsrEvent?.Error ?? string.Empty;

    public string AsrCallbackName => AsrEvent?.CallbackName ?? string.Empty;

    public double AsrOffsetSeconds => AsrEvent?.OffsetSeconds ?? 0.0;

    public string AsrWakeWord => AsrEvent?.WakeWord ?? string.Empty;

    public string AsrRecognizedText => AsrEvent?.RecognizedText ?? string.Empty;

    public string RealtimeVoiceRequestId => RealtimeVoiceEvent?.RequestId ?? string.Empty;

    public string RealtimeVoiceEventName => RealtimeVoiceEvent?.EventName ?? string.Empty;

    public string RealtimeVoiceText => RealtimeVoiceEvent?.Text ?? string.Empty;

    public string RealtimeVoiceDelta => RealtimeVoiceEvent?.Delta ?? string.Empty;

    public string RealtimeVoiceAccumulatedText => RealtimeVoiceEvent?.AccumulatedText ?? string.Empty;

    public bool RealtimeVoiceIsFinal => RealtimeVoiceEvent?.IsFinal ?? false;

    public string RealtimeVoiceError => RealtimeVoiceEvent?.Error ?? string.Empty;

    public string RealtimeVoiceCallbackName => RealtimeVoiceEvent?.CallbackName ?? string.Empty;

    public string RealtimeVoiceWakeWord => RealtimeVoiceEvent?.WakeWord ?? string.Empty;

    public string RealtimeVoiceRecognizedText => RealtimeVoiceEvent?.RecognizedText ?? string.Empty;

    public string SpeechCallbackName { get; init; } = string.Empty;

    public string LoadingEventName { get; init; } = string.Empty;

    public float LoadingProgress { get; init; }

    public string LoadingMessage { get; init; } = string.Empty;

    public string GuiControlId { get; init; } = string.Empty;

    public string GuiControlName { get; init; } = string.Empty;

    public string GuiEventName { get; init; } = string.Empty;

    public string SpriteId { get; init; } = string.Empty;

    public string SpriteName { get; init; } = string.Empty;

    public string SpriteEventName { get; init; } = string.Empty;

    public string TrayMenuItemId { get; init; } = string.Empty;

    public string TrayMenuItemText { get; init; } = string.Empty;

    public string TrayMenuEventName { get; init; } = string.Empty;
}
