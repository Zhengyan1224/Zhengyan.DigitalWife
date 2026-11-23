using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidDesktopSpritePolicy
{
    public const int FramesPerSecond = 30;
    public const int MinimumScalePercent = 10;

    public static Vector4 ClearColor(bool spriteMode, Vector4 sceneColor)
        => spriteMode ? Vector4.Zero : sceneColor;

    public static (int Width, int Height) Size(int width, int height, int screenWidth, int screenHeight, float density)
    {
        density = float.IsFinite(density) && density > 0 ? density : 1;
        float maxWidth = Math.Max(1, Math.Min(Math.Max(1, screenWidth) * .6f, 360 * density));
        float maxHeight = Math.Max(1, Math.Min(Math.Max(1, screenHeight) * .65f, 480 * density));
        float aspect = Math.Clamp(Math.Max(1, width) / (float)Math.Max(1, height), .25f, 4);
        float actualWidth = Math.Min(maxWidth, maxHeight * aspect);
        return (Math.Max(1, (int)actualWidth), Math.Max(1, (int)(actualWidth / aspect)));
    }

    public static (int X, int Y) Position(int x, int y, int width, int height, int screenWidth, int screenHeight)
        => (Math.Clamp(x, 0, Math.Max(0, screenWidth - width)), Math.Clamp(y, 0, Math.Max(0, screenHeight - height)));

    public static int MaximumScalePercent(int width, int height, int screenWidth, int screenHeight, float density)
    {
        var baseline = Size(width, height, screenWidth, screenHeight, density);
        double scale = Math.Min(Math.Max(1, screenWidth) / (double)baseline.Width,
            Math.Max(1, screenHeight) / (double)baseline.Height);
        // Round the slider endpoint up; ScaledSize fits that endpoint exactly to the screen.
        return Math.Max(MinimumScalePercent, (int)Math.Min(int.MaxValue, Math.Ceiling(scale * 100)));
    }

    public static (int Width, int Height) ScaledSize(int width, int height, int screenWidth, int screenHeight, float density, int percent)
    {
        var baseline = Size(width, height, screenWidth, screenHeight, density);
        int availableWidth = Math.Max(1, screenWidth), availableHeight = Math.Max(1, screenHeight);
        double fit = Math.Min(availableWidth / (double)baseline.Width, availableHeight / (double)baseline.Height);
        double scale = Math.Min(Math.Max(MinimumScalePercent, percent) / 100d, fit);
        return (Math.Clamp((int)Math.Min(availableWidth, baseline.Width * scale), 1, availableWidth),
            Math.Clamp((int)Math.Min(availableHeight, baseline.Height * scale), 1, availableHeight));
    }

    public static (int Width, int Height) OverlaySize(int contentWidth, int contentHeight, int screenWidth, int screenHeight,
        float density, bool toolbarCollapsed)
    {
        density = float.IsFinite(density) && density > 0 ? density : 1;
        int availableWidth = Math.Max(1, screenWidth), availableHeight = Math.Max(1, screenHeight);
        int controlsWidth = Math.Clamp((int)Math.Min(availableWidth, (toolbarCollapsed ? 36d : 180d) * density), 1, availableWidth);
        int controlsHeight = Math.Clamp((int)Math.Min(availableHeight, 32d * density), 1, availableHeight);
        // Very small sprites need space above the content so the controls do not hide the whole character.
        int heightWithControls = contentHeight < controlsHeight * 2L
            ? (int)Math.Min(availableHeight, (long)Math.Max(1, contentHeight) + controlsHeight) : contentHeight;
        return (Math.Clamp(Math.Max(contentWidth, controlsWidth), 1, availableWidth),
            Math.Clamp(Math.Max(heightWithControls, controlsHeight), 1, availableHeight));
    }

    public static bool ShouldRender(long previous, long current, int limit)
        => limit <= 0 || previous == 0 || current < previous || current - previous >= 1_000_000_000d / limit - 500_000;
}
