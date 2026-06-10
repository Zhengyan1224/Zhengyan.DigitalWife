namespace Zhengyan.DigitalWife.GamePlayer;

public sealed record RuntimeRealtimeVoiceScriptEvent(
    string RequestId,
    string EventName,
    string Text,
    string Delta,
    string AccumulatedText,
    bool IsFinal,
    string Error,
    string CallbackName,
    string WakeWord,
    string RecognizedText);
