namespace Zhengyan.DigitalWife.GameProjects;

public readonly record struct LayoutRect(float X, float Y, float Width, float Height);

public static class LayoutResolver
{
    public static bool IsRelative(string? layoutMode)
    {
        string normalized = (layoutMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "relative" or "scaled" or "scale";
    }

    public static string NormalizeLayoutMode(string? layoutMode)
    {
        return IsRelative(layoutMode) ? "relative" : "absolute";
    }

    public static LayoutRect Resolve(
        string? layoutMode,
        float x,
        float y,
        float width,
        float height,
        float actualWidth,
        float actualHeight,
        float referenceWidth,
        float referenceHeight,
        bool scaleFont = false)
    {
        _ = scaleFont;
        if (!IsRelative(layoutMode))
        {
            return new LayoutRect(x, y, Math.Max(width, 1.0f), Math.Max(height, 1.0f));
        }

        float safeReferenceWidth = Math.Max(referenceWidth, 1.0f);
        float safeReferenceHeight = Math.Max(referenceHeight, 1.0f);
        float scaleX = Math.Max(actualWidth, 1.0f) / safeReferenceWidth;
        float scaleY = Math.Max(actualHeight, 1.0f) / safeReferenceHeight;
        return new LayoutRect(
            x * scaleX,
            y * scaleY,
            Math.Max(width * scaleX, 1.0f),
            Math.Max(height * scaleY, 1.0f));
    }

    public static float ResolveFontSize(
        string? layoutMode,
        float fontSize,
        float actualWidth,
        float actualHeight,
        float referenceWidth,
        float referenceHeight)
    {
        float safeFontSize = Math.Clamp(fontSize <= 0.0f ? 18.0f : fontSize, 8.0f, 96.0f);
        if (!IsRelative(layoutMode))
        {
            return safeFontSize;
        }

        float safeReferenceWidth = Math.Max(referenceWidth, 1.0f);
        float safeReferenceHeight = Math.Max(referenceHeight, 1.0f);
        float scaleX = Math.Max(actualWidth, 1.0f) / safeReferenceWidth;
        float scaleY = Math.Max(actualHeight, 1.0f) / safeReferenceHeight;
        return Math.Clamp(safeFontSize * MathF.Min(scaleX, scaleY), 6.0f, 192.0f);
    }
}
