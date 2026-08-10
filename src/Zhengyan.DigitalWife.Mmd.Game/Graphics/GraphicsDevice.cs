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

    public string RendererName => _renderer.Name;

    public int RequestedAntiAliasingSamples => _renderer.RequestedAntiAliasingSamples;

    public int AntiAliasingSamples => _renderer.AntiAliasingSamples;

    /// <summary>
    /// Legacy OpenGL compatibility bridge. New scene code must use
    /// <see cref="Renderer"/> and <see cref="IRenderBackendServices"/> instead.
    /// This remains public temporarily for the OpenGL-only loading and material
    /// compatibility paths and is deliberately unavailable on Vulkan.
    /// </summary>
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

    public void ClearViewport(int x, int y, int width, int height, Vector4 color)
        => _renderer.ClearViewport(x, y, width, height, color);

    public bool TryReadBackBufferRgba(Span<byte> destination)
        => _renderer.TryReadBackBufferRgba(destination);

    public void WaitForIdle() => _renderer.WaitForIdle();

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

