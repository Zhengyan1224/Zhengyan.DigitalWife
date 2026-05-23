namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public interface IRuntimeTextureProvider
{
    bool TryGetTexture(string textureReference, out uint textureId);
}
