namespace Zhengyan.DigitalWife.Mmd.Game.Input;

public enum TouchPhase
{
    Started,
    Moved,
    Stationary,
    Ended,
    Cancelled
}

public enum TouchInputKind
{
    Unknown,
    Touch,
    Pen
}

public readonly record struct TouchPoint(
    int Id,
    float X,
    float Y,
    float DeltaX,
    float DeltaY,
    TouchPhase Phase,
    TouchInputKind Kind,
    float Pressure)
{
    public bool IsActive => Phase is TouchPhase.Started or TouchPhase.Moved or TouchPhase.Stationary;

    public bool IsEnded => Phase is TouchPhase.Ended or TouchPhase.Cancelled;
}
