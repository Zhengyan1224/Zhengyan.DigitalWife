using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class OpenGlRenderer : IRenderer
{
    private GL? _gl;
    private readonly IRenderBackendServices _services;

    public OpenGlRenderer()
    {
        _services = new OpenGlRenderBackendServices(this);
    }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public string Name => "OpenGL ES 3.0";

    public int RequestedAntiAliasingSamples { get; private set; } = 1;

    public int AntiAliasingSamples { get; private set; } = 1;

    public IRenderBackendServices Services => _services;

    public Vector2D<int> BackBufferSize { get; private set; }

    internal GL Gl => _gl ?? throw new InvalidOperationException("The OpenGL renderer has not been initialized.");

    public void Initialize(IWindow window, Vector2D<int> backBufferSize, int requestedSamples)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_gl is not null)
        {
            throw new InvalidOperationException("The OpenGL renderer is already initialized.");
        }

        RequestedAntiAliasingSamples = Zhengyan.DigitalWife.Mmd.Game.Graphics.AntiAliasingSamples.NormalizeRequested(requestedSamples);
        _gl = window.CreateOpenGLES();
        int actualSamples = 1;
        try
        {
            actualSamples = Math.Max(1, Gl.GetInteger(GLEnum.Samples));
        }
        catch
        {
            // Some GLES implementations do not expose the default framebuffer sample count.
        }

        AntiAliasingSamples = Zhengyan.DigitalWife.Mmd.Game.Graphics.AntiAliasingSamples.FallbackToSupported(
            RequestedAntiAliasingSamples, actualSamples);
        Resize(backBufferSize);
    }

    public void Resize(Vector2D<int> backBufferSize)
    {
        BackBufferSize = backBufferSize;
        Gl.Viewport(backBufferSize);
    }

    public void Clear(Vector4 color)
    {
        GL gl = Gl;
        gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        gl.Viewport(BackBufferSize);
        gl.Disable(GLEnum.ScissorTest);
        gl.Disable(GLEnum.StencilTest);
        gl.ColorMask(true, true, true, true);
        gl.DepthMask(true);
        gl.StencilMask(0xFF);
        gl.ClearColor(color.X, color.Y, color.Z, color.W);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    public void ClearViewport(int x, int y, int width, int height, Vector4 color)
    {
        GL gl = Gl;
        gl.Enable(GLEnum.ScissorTest);
        gl.Scissor(x, y, (uint)Math.Max(width, 1), (uint)Math.Max(height, 1));
        gl.ColorMask(true, true, true, true);
        gl.DepthMask(true);
        gl.StencilMask(0xFF);
        gl.ClearColor(color.X, color.Y, color.Z, color.W);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    public IRenderTarget CreateRenderTarget(string name)
    {
        return new RenderTexture(Gl, name);
    }

    public ITexture2D CreateTexture2D()
    {
        return new Texture2D(Gl);
    }

    public IScreenSpriteRenderer CreateScreenSpriteRenderer()
    {
        return new ScreenSpriteRenderer(Gl);
    }

    public IGpuBuffer CreateBuffer(GpuBufferDescription description)
    {
        return new OpenGlGpuBuffer(Gl, description);
    }

    public IGpuSampler CreateSampler(GpuSamplerDescription description)
    {
        return new OpenGlGpuSampler(Gl, description);
    }

    public void RestoreBackBuffer()
    {
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.Viewport(BackBufferSize);
    }

    public void SetViewport(int x, int y, int width, int height)
        => Gl.Viewport(x, y, (uint)Math.Max(width, 1), (uint)Math.Max(height, 1));

    public void SetScissor(int x, int y, int width, int height, bool enabled)
    {
        if (!enabled)
        {
            Gl.Disable(GLEnum.ScissorTest);
            return;
        }

        Gl.Enable(GLEnum.ScissorTest);
        Gl.Scissor(x, y, (uint)Math.Max(width, 1), (uint)Math.Max(height, 1));
    }

    public unsafe bool TryReadBackBufferRgba(Span<byte> destination)
    {
        int required = checked(Math.Max(BackBufferSize.X, 1) * Math.Max(BackBufferSize.Y, 1) * 4);
        if (destination.Length < required)
        {
            return false;
        }

        fixed (byte* pixels = destination)
        {
            Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
            Gl.PixelStore(GLEnum.PackAlignment, 1);
            Gl.ReadPixels(0, 0, (uint)Math.Max(BackBufferSize.X, 1), (uint)Math.Max(BackBufferSize.Y, 1),
                GLEnum.Rgba, GLEnum.UnsignedByte, pixels);
        }

        return true;
    }

    public void Present()
    {
        // Silk.NET presents the OpenGL surface at the end of the window Render callback.
    }

    public void WaitForIdle() => Gl.Finish();

    public void Dispose()
    {
        _gl?.Dispose();
        _gl = null;
    }
}
