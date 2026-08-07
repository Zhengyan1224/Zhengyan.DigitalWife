using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public interface IRenderer : IDisposable
{
    GraphicsBackend Backend { get; }

    string Name { get; }

    IRenderBackendServices Services { get; }

    Vector2D<int> BackBufferSize { get; }

    void Initialize(IWindow window, Vector2D<int> backBufferSize);

    void Resize(Vector2D<int> backBufferSize);

    void Clear(Vector4 color);

    void ClearViewport(int x, int y, int width, int height, Vector4 color);

    IRenderTarget CreateRenderTarget(string name);

    ITexture2D CreateTexture2D();

    IScreenSpriteRenderer CreateScreenSpriteRenderer();

    IGpuBuffer CreateBuffer(GpuBufferDescription description);

    IGpuSampler CreateSampler(GpuSamplerDescription description);

    void RestoreBackBuffer();

    void SetViewport(int x, int y, int width, int height);

    void SetScissor(int x, int y, int width, int height, bool enabled);

    bool TryReadBackBufferRgba(Span<byte> destination);

    void Present();
}

public interface IRendererFactory
{
    GraphicsBackend Backend { get; }

    bool IsSupported(out string reason);

    IRenderer Create();
}

public sealed record RendererSelection(
    GraphicsBackend RequestedBackend,
    GraphicsBackend ResolvedBackend,
    string? FallbackReason = null);
