namespace Zhengyan.DigitalWife.Mmd.Game.Input;

internal sealed class NullTouchInputSource : ITouchInputSource
{
    public static readonly NullTouchInputSource Instance = new();

    private NullTouchInputSource()
    {
    }

    public bool IsAvailable => false;

    public IReadOnlyList<TouchInputEvent> ConsumeEvents() => [];

    public void Dispose()
    {
    }
}
