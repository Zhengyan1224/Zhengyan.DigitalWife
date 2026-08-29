using System.Numerics;
using Veldrid;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>Vulkan off-screen target backed by Veldrid textures and a framebuffer.</summary>
public sealed class VeldridRenderTarget : IRenderTarget
{
    private readonly VulkanRenderer _renderer;
    private Texture? _colorTexture;
    private TextureView? _colorView;
    private Texture? _depthTexture;
    private TextureView? _depthView;
    private Framebuffer? _framebuffer;
    private bool _disposed;

    public VeldridRenderTarget(VulkanRenderer renderer, string name)
    {
        _renderer = renderer;
        Name = name;
    }

    public string Name { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public GraphicsBackend Backend => GraphicsBackend.Vulkan;
    public uint LegacyColorTextureId => 0;
    public object? NativeColorResource => _colorView;
    public object? NativeDepthResource => _depthView;
    internal Framebuffer Framebuffer => _framebuffer ?? throw new InvalidOperationException("Render target has not been sized.");

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
        _colorTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.RenderTarget | TextureUsage.Sampled));
        _colorView = factory.CreateTextureView(_colorTexture);
        // Keep off-screen targets on the same widely-supported packed depth
        // format as the swapchain. D32S8 is optional on a number of Android
        // Vulkan devices and would make RenderTexture/reflection allocation
        // fail even though the main surface is usable.
        _depthTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, PixelFormat.D24_UNorm_S8_UInt,
            TextureUsage.DepthStencil | TextureUsage.Sampled));
        _depthView = factory.CreateTextureView(_depthTexture);
        _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(_depthTexture, _colorTexture));
    }

    public void BeginPass(Vector4 clearColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSize(Width, Height);
        _renderer.BeginRenderTarget(this, clearColor);
    }

    public void ResumePass()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSize(Width, Height);
        _renderer.ResumeRenderTarget(this);
    }

    public void EndPass()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _renderer.EndRenderTarget(this);
    }

    public void ForceOpaqueAlpha()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _renderer.ForceOpaqueAlpha(this);
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
        _colorView?.Dispose();
        _colorView = null;
        _colorTexture?.Dispose();
        _colorTexture = null;
        _depthView?.Dispose();
        _depthView = null;
        _depthTexture?.Dispose();
        _depthTexture = null;
    }
}
