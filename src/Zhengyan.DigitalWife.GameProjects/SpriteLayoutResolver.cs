namespace Zhengyan.DigitalWife.GameProjects;

public static class SpriteLayoutResolver
{
    public static LayoutRect Resolve(
        SpriteSettings sprite,
        float actualWidth,
        float actualHeight,
        float referenceWidth,
        float referenceHeight)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        return LayoutResolver.Resolve(
            sprite.LayoutMode,
            sprite.X,
            sprite.Y,
            sprite.Width,
            sprite.Height,
            actualWidth,
            actualHeight,
            referenceWidth,
            referenceHeight);
    }

    public static bool ContainsPoint(
        SpriteSettings sprite,
        float pointX,
        float pointY,
        float actualWidth,
        float actualHeight,
        float referenceWidth,
        float referenceHeight)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        if (!sprite.Visible || sprite.Width <= 0.0f || sprite.Height <= 0.0f || string.IsNullOrWhiteSpace(sprite.Path))
        {
            return false;
        }

        LayoutRect rect = Resolve(sprite, actualWidth, actualHeight, referenceWidth, referenceHeight);
        float centerX = rect.X + (rect.Width * 0.5f);
        float centerY = rect.Y + (rect.Height * 0.5f);
        float halfWidth = rect.Width * 0.5f;
        float halfHeight = rect.Height * 0.5f;

        float radians = -sprite.RotationDegrees * (MathF.PI / 180.0f);
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float offsetX = pointX - centerX;
        float offsetY = pointY - centerY;
        float localX = (offsetX * cos) - (offsetY * sin);
        float localY = (offsetX * sin) + (offsetY * cos);

        return MathF.Abs(localX) <= halfWidth && MathF.Abs(localY) <= halfHeight;
    }
}
