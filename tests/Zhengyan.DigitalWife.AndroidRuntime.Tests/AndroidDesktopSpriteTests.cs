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

    public static void SizeSlider()
    {
        var baseline = AndroidDesktopSpritePolicy.ScaledSize(720, 1280, 1080, 2400, 3, 100);
        Check(baseline == (648, 1152), "The default DemoGame08 content size changed.");
        var minimum = AndroidDesktopSpritePolicy.ScaledSize(720, 1280, 1080, 2400, 3, 10);
        Check(minimum == (64, 115), "The slider no longer reaches 10% of the original content size.");
        Check(AndroidDesktopSpritePolicy.ScaledSize(720, 1280, 1080, 2400, 3, -10) == minimum,
            "An invalid percentage bypassed the 10% minimum.");
        Check(AndroidDesktopSpritePolicy.ScaledSize(720, 1280, 1080, 2400, 3, 150) == (972, 1728),
            "The slider was still constrained by the default 60%/dp limits.");
        int maximumPercent = AndroidDesktopSpritePolicy.MaximumScalePercent(720, 1280, 1080, 2400, 3);
        var maximum = AndroidDesktopSpritePolicy.ScaledSize(720, 1280, 1080, 2400, 3, maximumPercent);
        Check(maximum == (1080, 1920), "The maximum failed to fill the available screen width at the project aspect ratio.");
        Check(AndroidDesktopSpritePolicy.ScaledSize(720, 1280, 1080, 2400, 3, int.MaxValue) == maximum,
            "An oversized saved percentage escaped the screen bounds.");
        var expanded = AndroidDesktopSpritePolicy.OverlaySize(minimum.Width, minimum.Height, 1080, 2400, 3, false);
        var collapsed = AndroidDesktopSpritePolicy.OverlaySize(minimum.Width, minimum.Height, 1080, 2400, 3, true);
        Check(expanded.Width >= 180 * 3 && collapsed.Width >= 36 * 3,
            "At 10%, shrinking the character made the size/restore buttons unreachable.");
        Check(expanded.Height - minimum.Height >= 32 * 3 && collapsed.Height - minimum.Height >= 32 * 3,
            "The toolbar covered the entire tiny character.");
        Check(AndroidDesktopSpritePolicy.OverlaySize(baseline.Width, baseline.Height, 1080, 2400, 3, false) == baseline,
            "Normal-sized content acquired extra space or changed its original window size.");
        foreach (var screen in new[] { (1080, 2400, 3f), (2400, 1080, 3f), (320, 480, 1f), (2560, 1600, 2f), (1, 1, float.NaN) })
        {
            int limit = AndroidDesktopSpritePolicy.MaximumScalePercent(720, 1280, screen.Item1, screen.Item2, screen.Item3);
            foreach (int percent in new[] { 10, 100, 150, limit, int.MaxValue })
            {
                var content = AndroidDesktopSpritePolicy.ScaledSize(720, 1280, screen.Item1, screen.Item2, screen.Item3, percent);
                Check(content.Width >= 1 && content.Height >= 1 && content.Width <= screen.Item1 && content.Height <= screen.Item2,
                    "Scaling on rotation, a tablet or a tiny display exceeded the screen.");
                if (content.Width > 1 && content.Height > 1)
                    Check(Math.Abs(content.Width - content.Height * (720d / 1280)) < 2,
                        "Scaling distorted the project aspect ratio beyond integer pixel rounding.");
                foreach (bool hidden in new[] { false, true })
                {
                    var overlay = AndroidDesktopSpritePolicy.OverlaySize(content.Width, content.Height, screen.Item1, screen.Item2, screen.Item3, hidden);
                    Check(overlay.Width >= content.Width && overlay.Height >= content.Height && overlay.Width <= screen.Item1 && overlay.Height <= screen.Item2,
                        "The toolbar overlay clipped content or exceeded the screen.");
                }
            }
        }
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
