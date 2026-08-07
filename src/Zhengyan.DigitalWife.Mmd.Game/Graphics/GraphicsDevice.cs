using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class GraphicsDevice
{
    private readonly IRenderer _renderer;

    public GraphicsDevice(IRenderer renderer, Vector4 clearColor)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        ClearColor = clearColor;
    }

    public IRenderer Renderer => _renderer;

    public GraphicsBackend Backend => _renderer.Backend;

    // Compatibility bridge while individual rendering components move to backend-neutral resources.
    public GL Gl => _renderer is OpenGlRenderer openGl
        ? openGl.Gl
        : throw new NotSupportedException($"{_renderer.Backend} does not expose an OpenGL API.");

    public Vector4 ClearColor { get; set; }

    public Vector2D<int> BackBufferSize => _renderer.BackBufferSize;

    public void Resize(Vector2D<int> backBufferSize)
    {
        _renderer.Resize(backBufferSize);
    }

    public IRenderTarget CreateRenderTarget(string name)
    {
        return _renderer.CreateRenderTarget(name);
    }

    public ITexture2D CreateTexture2D()
    {
        return _renderer.CreateTexture2D();
    }

    public IScreenSpriteRenderer CreateScreenSpriteRenderer()
    {
        return _renderer.CreateScreenSpriteRenderer();
    }

    public IGpuBuffer CreateBuffer(GpuBufferDescription description)
    {
        return _renderer.CreateBuffer(description);
    }

    public IGpuSampler CreateSampler(GpuSamplerDescription description)
    {
        return _renderer.CreateSampler(description);
    }

    public void RestoreBackBuffer()
    {
        _renderer.RestoreBackBuffer();
    }

    public void SetViewport(int x, int y, int width, int height) => _renderer.SetViewport(x, y, width, height);

    public void SetScissor(int x, int y, int width, int height, bool enabled = true)
        => _renderer.SetScissor(x, y, width, height, enabled);

    public bool TryReadBackBufferRgba(Span<byte> destination)
        => _renderer.TryReadBackBufferRgba(destination);

    public void Clear(ClearBufferMask mask = ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit)
    {
        ClearBufferMask defaultMask = ClearBufferMask.ColorBufferBit
            | ClearBufferMask.DepthBufferBit
            | ClearBufferMask.StencilBufferBit;
        if (mask == defaultMask)
        {
            _renderer.Clear(ClearColor);
            return;
        }

        if (_renderer is not OpenGlRenderer openGl)
        {
            throw new NotSupportedException("Custom clear masks are only available through the OpenGL compatibility path.");
        }

        openGl.Gl.Clear(mask);
    }
}

