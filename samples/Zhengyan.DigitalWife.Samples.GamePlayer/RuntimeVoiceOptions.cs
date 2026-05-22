namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeVoiceOptions
{
    public int? SpeakerId { get; init; }

    public float? Speed { get; init; }

    public float? Volume { get; init; }

    public Action? OnCompleted { get; init; }
}
