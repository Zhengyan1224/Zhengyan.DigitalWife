using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GameProjects;

internal static class AndroidDesktopSpriteTests
{
    public static void GeometryAndPacing()
    {
        foreach (var screen in new[] { (1080, 2400, 3f), (2400, 1080, 3f), (320, 480, 1f), (1, 1, float.NaN) })
        {
            var size = AndroidDesktopSpritePolicy.Size(500, 720, screen.Item1, screen.Item2, screen.Item3);
            Check(size.Width >= 1 && size.Height >= 1 && size.Width <= screen.Item1 && size.Height <= screen.Item2,
                "The sprite exceeded the screen after rotation or on a small display.");
            var point = AndroidDesktopSpritePolicy.Position(int.MaxValue, int.MinValue, size.Width, size.Height, screen.Item1, screen.Item2);
            Check(point.X + size.Width <= screen.Item1 && point.Y == 0, "Dragging could strand the close/drag controls off screen.");
        }
        var portrait = AndroidDesktopSpritePolicy.Size(500, 720, 1080, 2400, 3);
        Check(Math.Abs(portrait.Width / (float)portrait.Height - 500f / 720) < .003f, "Sprite scaling distorted the project aspect ratio.");
        Vector4 original = new(.2f, .4f, .6f, 1);
        Check(AndroidDesktopSpritePolicy.ClearColor(true, original) == Vector4.Zero, "Sprite clear pixels must be transparent black.");
        Check(AndroidDesktopSpritePolicy.ClearColor(false, original) == original, "Ordinary Android games lost their background.");
        int frames = 0;
        long last = 0;
        for (int i = 1; i <= 120; i++)
        {
            long time = i * 1_000_000_000L / 120;
            if (!AndroidDesktopSpritePolicy.ShouldRender(last, time, 30)) continue;
            last = time; frames++;
        }
        Check(frames == 30, $"A 120 Hz display rendered {frames} sprite frames instead of 30.");
        Check(AndroidDesktopSpritePolicy.ShouldRender(100, 50, 30), "The sprite failed to recover from a reset frame clock.");
        Check(AndroidDesktopSpritePolicy.ShouldRender(100, 101, 0), "Ordinary uncapped rendering was throttled.");
    }

    public static void Compatibility()
    {
        string path = Path.Combine(Path.GetTempPath(), "dw-sprite-compat-" + Guid.NewGuid().ToString("N"));
        try
        {
            GameProject project = new();
            project.Window.DesktopSpriteMode = true;
            project.Window.DesktopSpriteClickThrough = true;
            project.Window.DesktopSpriteTrayEnabled = true;
            GameProjectStore.Save(path, project);
            var report = AndroidProjectCompatibility.Analyze(path, project);
            Check(report.Issues.Any(i => i.Code == "ANDROID_DESKTOP_SPRITE_PERMISSION"), "The export omitted the overlay permission requirement.");
            Check(report.Issues.Any(i => i.Code == "ANDROID_DESKTOP_SPRITE_DESKTOP_FEATURES"), "The export promised unavailable pixel click-through/tray menus.");
            Check(!report.Issues.Any(i => i.Code == "ANDROID_DESKTOP_SPRITE_IGNORED"), "The export still claimed Android ignores sprite mode.");
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
