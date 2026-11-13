namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed class RuntimePerformance
{
    private double _smoothed;
    public double Fps { get; private set; }
    public double RawFps { get; private set; }
    public double DeltaSeconds { get; private set; }
    public double TotalSeconds { get; private set; }
    public long FrameCount { get; private set; }
    internal void Report(double interval, double total)
    {
        DeltaSeconds = Math.Max(0, interval); TotalSeconds = total; FrameCount++;
        if (interval <= double.Epsilon) return;
        RawFps = 1 / interval;
        _smoothed = _smoothed <= 0 ? interval : _smoothed * .88 + interval * .12;
        Fps = 1 / _smoothed;
    }
}
