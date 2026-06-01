namespace Zhengyan.DigitalWife.Audio.OpenAL;

public sealed class OpenAlRuntimeOptions
{
    public int EstimatedOutputLatencyMilliseconds { get; init; } = 60;

    public int MaxQueuedStreamingBuffers { get; init; } = 8;

    public int PollIntervalMilliseconds { get; init; } = 10;
}
