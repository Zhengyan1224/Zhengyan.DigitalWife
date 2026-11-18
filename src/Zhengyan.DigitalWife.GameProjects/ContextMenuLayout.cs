namespace Zhengyan.DigitalWife.GameProjects;

/// <summary>Menu dimensions in render pixels, using the desktop popup's layout rules.</summary>
public readonly record struct ContextMenuLayout(float ContentWidth, float ItemHeight, float PaddingX, float PaddingY,
    float FontSize, float Rounding, float BorderThickness)
{
    public float Width => ContentWidth + 2 * (PaddingX + BorderThickness);
    public float Height(int itemCount) => ItemHeight * itemCount + 2 * (PaddingY + BorderThickness);

    public static ContextMenuLayout Resolve(ContextMenuSettings menu, float width, float height, GameWindowSettings window)
    {
        float scale = LayoutResolver.IsRelative(menu.LayoutMode)
            ? MathF.Min(Math.Max(width, 1) / Math.Max(window.Width, 1), Math.Max(height, 1) / Math.Max(window.Height, 1)) : 1;
        float contentWidth = LayoutResolver.Resolve(menu.LayoutMode, 0, 0, menu.Width, menu.Width,
            width, height, window.Width, window.Height).Width;
        return new(Math.Max(contentWidth, 48), Math.Max(menu.ItemHeight * scale, 12),
            Math.Max(menu.PaddingX * scale, 0), Math.Max(menu.PaddingY * scale, 0),
            LayoutResolver.ResolveFontSize(menu.LayoutMode, menu.Style.FontSize, width, height, window.Width, window.Height),
            Math.Max(menu.Style.Rounding * scale, 0), Math.Max(menu.Style.BorderThickness * scale, 0));
    }
}
