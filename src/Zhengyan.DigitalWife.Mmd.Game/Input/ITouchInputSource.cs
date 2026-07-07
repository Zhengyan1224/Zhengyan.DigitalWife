namespace Zhengyan.DigitalWife.Mmd.Game.Input;

internal interface ITouchInputSource : IDisposable
{
    bool IsAvailable { get; }

    IReadOnlyList<TouchInputEvent> ConsumeEvents();
}
