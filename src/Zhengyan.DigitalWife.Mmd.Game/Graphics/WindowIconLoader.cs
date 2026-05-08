using Silk.NET.Core;
using Silk.NET.Windowing;
using StbImageSharp;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public static class WindowIconLoader
{
    public static bool TrySetWindowIconFromFile(IWindow window, string? iconPath)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(iconPath);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        using FileStream stream = File.OpenRead(fullPath);
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        RawImage rawImage = new(image.Width, image.Height, image.Data);
        try
        {
            window.SetWindowIcon(new[] { rawImage });
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
