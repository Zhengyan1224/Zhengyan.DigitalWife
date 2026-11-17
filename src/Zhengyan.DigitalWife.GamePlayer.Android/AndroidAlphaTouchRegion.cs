namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal readonly record struct AndroidTouchRect(int Left, int Top, int Right, int Bottom);

internal static class AndroidAlphaTouchRegion
{
    // Renderers return bottom-up RGBA; the native GUI bitmap is top-down ARGB.
    public static List<AndroidTouchRect> Build(ReadOnlySpan<byte> rgba, ReadOnlySpan<int> gui,
        int width, int height, int cellSize = 4, byte threshold = 8)
    {
        if (width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return [];
        cellSize = Math.Clamp(cellSize, 1, 16);
        bool hasGui = gui.Length >= width * height;
        List<AndroidTouchRect> result = [];
        Dictionary<(int Left, int Right), int> previous = [];
        for (int y = 0; y < height; y += cellSize)
        {
            Dictionary<(int Left, int Right), int> current = [];
            int run = -1;
            for (int x = 0; x <= width; x += cellSize)
            {
                bool opaque = false;
                if (x < width)
                    for (int py = y; py < Math.Min(y + cellSize, height) && !opaque; py++)
                        for (int px = x; px < Math.Min(x + cellSize, width); px++)
                            if (rgba[((height - 1 - py) * width + px) * 4 + 3] >= threshold
                                || hasGui && (uint)gui[py * width + px] >> 24 >= threshold)
                            { opaque = true; break; }
                if (opaque && run < 0) run = x;
                if (run >= 0 && (!opaque || x + cellSize >= width))
                {
                    int right = opaque ? width : x;
                    var key = (run, right);
                    int bottom = Math.Min(y + cellSize, height);
                    if (previous.TryGetValue(key, out int index) && result[index].Bottom == y)
                        result[index] = result[index] with { Bottom = bottom };
                    else { index = result.Count; result.Add(new(run, y, right, bottom)); }
                    current[key] = index;
                    run = -1;
                }
            }
            previous = current;
        }
        return result;
    }
}
