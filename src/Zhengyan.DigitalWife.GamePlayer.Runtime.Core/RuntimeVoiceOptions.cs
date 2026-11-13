namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed class RuntimeVoiceOptions
{
    public int? SpeakerId { get; init; }
    public float? Speed { get; init; }
    public float? Volume { get; init; }
    public Action? OnCompleted { get; init; }
}
