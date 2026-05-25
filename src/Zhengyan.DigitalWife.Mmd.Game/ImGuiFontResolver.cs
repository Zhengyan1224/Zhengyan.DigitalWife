using System.Runtime.InteropServices;

namespace Zhengyan.DigitalWife.Mmd.Game;

public static class ImGuiFontResolver
{
    public static bool TryGetCjkFontPath(out string fontPath)
    {
        foreach (string candidate in EnumerateCandidates())
        {
            if (File.Exists(candidate))
            {
                fontPath = candidate;
                return true;
            }
        }

        fontPath = string.Empty;
        return false;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        string? bundled = BundledAssetPathResolver.TryResolveFile("Resources", "Fonts", "NotoSansCJKsc-Regular.otf");
        if (!string.IsNullOrWhiteSpace(bundled))
        {
            yield return bundled;
        }

        IEnumerable<string> platformCandidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? GetWindowsFontCandidates()
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? GetMacFontCandidates()
                : GetLinuxFontCandidates();

        foreach (string candidate in platformCandidates)
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> GetWindowsFontCandidates()
    {
        string fontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        return
        [
            Path.Combine(fontsDir, "msyh.ttc"),
            Path.Combine(fontsDir, "msyhbd.ttc"),
            Path.Combine(fontsDir, "simsun.ttc"),
            Path.Combine(fontsDir, "arialuni.ttf"),
            Path.Combine(fontsDir, "meiryo.ttc"),
            Path.Combine(fontsDir, "YuGothM.ttc"),
            Path.Combine(fontsDir, "msgothic.ttc")
        ];
    }

    private static IEnumerable<string> GetLinuxFontCandidates()
    {
        return
        [
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJKsc-Regular.otf",
            "/usr/share/fonts/opentype/noto/NotoSansCJKjp-Regular.otf",
            "/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
        ];
    }

    private static IEnumerable<string> GetMacFontCandidates()
    {
        return
        [
            "/System/Library/Fonts/PingFang.ttc",
            "/System/Library/Fonts/Hiragino Sans GB.ttc",
            "/System/Library/Fonts/Supplemental/Arial Unicode.ttf"
        ];
    }
}
