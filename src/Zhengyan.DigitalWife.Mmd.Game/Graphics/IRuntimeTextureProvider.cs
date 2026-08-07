namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public readonly record struct RuntimeTextureHandle(
    GraphicsBackend Backend,
    uint LegacyTextureId,
    object? NativeResource = null);

public interface IRuntimeTextureProvider
{
    bool TryGetTexture(string textureReference, out uint textureId);

    /// <summary>
    /// Returns a backend-neutral handle for migrated consumers. The legacy method
    /// remains available while OpenGL-only components are being converted.
    /// </summary>
    bool TryGetTextureHandle(string textureReference, out RuntimeTextureHandle handle)
    {
        if (TryGetTexture(textureReference, out uint textureId))
        {
            handle = new RuntimeTextureHandle(GraphicsBackend.OpenGL, textureId);
            return textureId != 0;
        }

        handle = default;
        return false;
    }
}
