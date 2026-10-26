namespace Zhengyan.DigitalWife.Scripting;

/// <summary>Live system clock shared by desktop and Android scripts.</summary>
public sealed class ScriptTime
{
    /// <summary>Current device time in the local time zone.</summary>
    public DateTime Now => DateTime.Now;

    /// <summary>Current device time in UTC.</summary>
    public DateTime UtcNow => DateTime.UtcNow;

    /// <summary>Whole seconds since 1970-01-01 00:00:00 UTC.</summary>
    public long UnixTimeSeconds => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Whole milliseconds since 1970-01-01 00:00:00 UTC.</summary>
    public long UnixTimeMilliseconds => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
