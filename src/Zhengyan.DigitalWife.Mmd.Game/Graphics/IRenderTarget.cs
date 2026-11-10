using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>
/// Backend-neutral off-screen render target. Backend-specific handles stay below
/// the scene/component boundary.
/// </summary>
public interface IRenderTarget : IDisposable
{
    string Name { get; }
    int Width { get; }
    int Height { get; }
    int SampleCount => 1;
    GraphicsBackend Backend { get; }
    uint LegacyColorTextureId { get; }

    object? NativeColorResource { get; }
    object? NativeDepthResource { get; }
    void EnsureSize(int width, int height);
    void EnsureSize(int width, int height, int samples);
    void BeginPass(Vector4 clearColor);
    void ResumePass();
    void EndPass();
    void ForceOpaqueAlpha();
}
