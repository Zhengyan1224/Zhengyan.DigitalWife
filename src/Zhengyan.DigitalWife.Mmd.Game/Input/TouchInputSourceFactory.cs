using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game.Input;

internal static class TouchInputSourceFactory
{
    public static ITouchInputSource Create(IWindow window)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsPointerTouchInputSource.Create(window);
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacTouchInputSource.Create(window);
        }

        if (OperatingSystem.IsLinux())
        {
            string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? string.Empty;
            return sessionType.Equals("wayland", StringComparison.OrdinalIgnoreCase)
                ? NullTouchInputSource.Instance
                : X11TouchInputSource.Create(window);
        }

        return NullTouchInputSource.Instance;
    }
}
