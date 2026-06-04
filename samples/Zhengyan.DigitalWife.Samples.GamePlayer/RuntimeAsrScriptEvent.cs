namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed record RuntimeAsrScriptEvent(
    string RequestId,
    string EventName,
    string Text,
    bool IsFinal,
    string Error,
    string CallbackName,
    double OffsetSeconds);
