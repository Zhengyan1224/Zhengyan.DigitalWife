namespace Zhengyan.DigitalWife.Mmd.Game.Input;

internal readonly record struct TouchInputEvent(
    int Id,
    float X,
    float Y,
    TouchPhase Phase,
    TouchInputKind Kind,
    float Pressure);
