using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidDesktopSpritePolicy
{
    public const int FramesPerSecond = 30;

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

    public static bool ShouldRender(long previous, long current, int limit)
        => limit <= 0 || previous == 0 || current < previous || current - previous >= 1_000_000_000d / limit - 500_000;
}
