using System.Collections.Concurrent;

namespace Android.Util;

// Capture the production Android logger's platform boundary without a device.
public static class Log
{
    internal static ConcurrentQueue<(string Level, string Tag, string Message)> Entries { get; } = new();
    public static int Info(string tag, string message) => Write("info", tag, message);
    public static int Warn(string tag, string message) => Write("warning", tag, message);
    public static int Error(string tag, string message) => Write("error", tag, message);

    private static int Write(string level, string tag, string message)
    {
        Entries.Enqueue((level, tag, message));
        return message.Length;
    }
}
