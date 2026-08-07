namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>Backend-neutral sampled 2D texture contract.</summary>
public interface ITexture2D : IDisposable
{
    GraphicsBackend Backend { get; }
    int Width { get; }
    int Height { get; }
    bool HasAlpha { get; }
    TextureAlphaMode AlphaMode { get; }
    uint LegacyTextureId { get; }
    object? NativeResource { get; }
    void LoadFromFile(string filePath);
    void Upload(byte[] bytes, uint width, uint height, TextureAlphaMode alphaMode = TextureAlphaMode.Blend);
    void Fill(byte red, byte green, byte blue, byte alpha = 255);
}
