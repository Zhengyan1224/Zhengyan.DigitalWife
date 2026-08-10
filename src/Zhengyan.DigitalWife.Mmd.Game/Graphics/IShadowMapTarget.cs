using Silk.NET.OpenGLES;
using Veldrid;
using VeldridFramebuffer = Veldrid.Framebuffer;
using VeldridPixelFormat = Veldrid.PixelFormat;
using VeldridSampler = Veldrid.Sampler;
using VeldridSamplerDescription = Veldrid.SamplerDescription;
using VeldridTexture = Veldrid.Texture;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public interface IShadowMapTarget : IDisposable
{
    int Width { get; }
    int Height { get; }
    RuntimeTextureHandle Texture { get; }
    object? NativeSampler { get; }

    void EnsureSize(int width, int height);
    void BeginPass();
    void EndPass();
}

internal sealed class OpenGlShadowMapTarget : IShadowMapTarget
{
    private readonly DepthRenderTexture _texture;
    private bool _disposed;

    public OpenGlShadowMapTarget(GL gl, string name)
    {
        _texture = new DepthRenderTexture(gl, name);
    }

    public int Width => _texture.Width;
    public int Height => _texture.Height;
    public RuntimeTextureHandle Texture => new(GraphicsBackend.OpenGL, _texture.DepthTextureId);
    public object? NativeSampler => null;

    public void EnsureSize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _texture.EnsureSize(width, height);
    }

    public void BeginPass()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _texture.Bind();
        GL gl = _texture.Gl;
        gl.Disable(GLEnum.ScissorTest);
        gl.Disable(GLEnum.StencilTest);
        gl.ColorMask(false, false, false, false);
        gl.DepthMask(true);
        gl.Enable(GLEnum.DepthTest);
        gl.Clear(ClearBufferMask.DepthBufferBit);
    }

    public void EndPass()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _texture.Gl.ColorMask(true, true, true, true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _texture.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class VeldridShadowMapTarget : IShadowMapTarget
{
    private readonly VulkanRenderer _renderer;
    private VeldridTexture? _depthTexture;
    private TextureView? _depthView;
    private VeldridFramebuffer? _framebuffer;
    private VeldridSampler? _sampler;
    private bool _disposed;

    public VeldridShadowMapTarget(VulkanRenderer renderer, string name)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Name = name;
    }

    public string Name { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public RuntimeTextureHandle Texture => new(GraphicsBackend.Vulkan, 0, _depthView);
    public object? NativeSampler => _sampler;
    internal VeldridFramebuffer Framebuffer => _framebuffer
        ?? throw new InvalidOperationException("Shadow map target has not been sized.");

    public void EnsureSize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (Width == width && Height == height && _framebuffer is not null)
        {
            return;
        }

        DisposeResources();
        Width = width;
        Height = height;
        ResourceFactory factory = _renderer.ResourceFactory;
        _depthTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width,
            (uint)height,
            1,
            1,
            VeldridPixelFormat.D24_UNorm_S8_UInt,
            TextureUsage.DepthStencil | TextureUsage.Sampled));
        _depthView = factory.CreateTextureView(_depthTexture);
        _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(
            _depthTexture,
            Array.Empty<VeldridTexture>()));
        _sampler = factory.CreateSampler(new VeldridSamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinPoint_MagPoint_MipPoint,
            null,
            0,
            0,
            uint.MaxValue,
            0,
            SamplerBorderColor.OpaqueWhite));
    }

    public void BeginPass()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSize(Width, Height);
        _renderer.BeginShadowMap(this);
    }

    public void EndPass()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _renderer.EndShadowMap(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeResources();
        GC.SuppressFinalize(this);
    }

    private void DisposeResources()
    {
        _framebuffer?.Dispose();
        _framebuffer = null;
        _depthView?.Dispose();
        _depthView = null;
        _depthTexture?.Dispose();
        _depthTexture = null;
        _sampler?.Dispose();
        _sampler = null;
    }
}
