namespace Zhengyan.DigitalWife.Mmd.Game;

public readonly struct GameTime(TimeSpan totalGameTime, TimeSpan elapsedGameTime, long frameCount)
{
    public TimeSpan TotalGameTime { get; } = totalGameTime;

    public TimeSpan ElapsedGameTime { get; } = elapsedGameTime;

    public long FrameCount { get; } = frameCount;

    public double TotalSeconds => TotalGameTime.TotalSeconds;

    public double ElapsedSeconds => ElapsedGameTime.TotalSeconds;
}

