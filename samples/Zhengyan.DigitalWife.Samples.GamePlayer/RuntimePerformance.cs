using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimePerformance
{
    private const double SmoothingFactor = 0.12;
    private bool _hasSample;
    private double _smoothedDeltaSeconds;

    public double Fps { get; private set; }

    public double RawFps { get; private set; }

    public double DeltaSeconds { get; private set; }

    public double TotalSeconds { get; private set; }

    public long FrameCount { get; private set; }

    internal void Update(GameTime gameTime)
    {
        DeltaSeconds = Math.Max(0.0, gameTime.ElapsedSeconds);
        TotalSeconds = gameTime.TotalSeconds;
        FrameCount = gameTime.FrameCount;

        if (DeltaSeconds <= double.Epsilon)
        {
            return;
        }

        RawFps = 1.0 / DeltaSeconds;
        _smoothedDeltaSeconds = _hasSample
            ? (_smoothedDeltaSeconds * (1.0 - SmoothingFactor)) + (DeltaSeconds * SmoothingFactor)
            : DeltaSeconds;
        _hasSample = true;
        Fps = _smoothedDeltaSeconds <= double.Epsilon ? 0.0 : 1.0 / _smoothedDeltaSeconds;
    }
}
