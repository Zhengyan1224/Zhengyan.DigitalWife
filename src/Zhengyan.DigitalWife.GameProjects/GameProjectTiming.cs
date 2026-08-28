namespace Zhengyan.DigitalWife.GameProjects;

public static class GameProjectTiming
{
    public const string TimeSynchronized = "time_synchronized";
    public const string FrameRateDependent = "frame_rate_dependent";
    public const double MaximumFrameRateDependentElapsedSeconds = 1.0 / 30.0;
    public const double MaximumPhysicsElapsedSeconds = 1.0 / 15.0;

    public static string NormalizeMode(string? timingMode)
    {
        string normalized = (timingMode ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        return normalized is FrameRateDependent or "framerate_dependent" or "frame"
            ? FrameRateDependent
            : TimeSynchronized;
    }

    public static double ResolveAnimationElapsedSeconds(string? timingMode, double rawElapsedSeconds)
    {
        double elapsedSeconds = Math.Max(0.0, rawElapsedSeconds);
        return NormalizeMode(timingMode) == FrameRateDependent
            ? Math.Min(elapsedSeconds, MaximumFrameRateDependentElapsedSeconds)
            : elapsedSeconds;
    }
}
