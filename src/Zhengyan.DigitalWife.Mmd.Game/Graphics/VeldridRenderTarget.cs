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
    private Texture? _multisampleColor;
    private Texture? _resolvedDepth;
    private TextureView? _multisampleDepthView;
    private Framebuffer? _depthResolveFramebuffer;
    private VeldridDepthResolvePass? _depthResolvePass;
    public bool ResolveDepth { get; set; }
    private bool _allocatedResolveDepth;
    private int _requestedSamples = 1;
    private bool _disposed;

    public VeldridRenderTarget(VulkanRenderer renderer, string name)
    {
        _renderer = renderer;
        Name = name;
    }

    public string Name { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int SampleCount { get; private set; } = 1;
    public GraphicsBackend Backend => GraphicsBackend.Vulkan;
    public uint LegacyColorTextureId => 0;
    public object? NativeColorResource => _colorView;
    public object? NativeDepthResource => _depthView;
    internal Framebuffer Framebuffer => _framebuffer ?? throw new InvalidOperationException("Render target has not been sized.");

    public void EnsureSize(int width, int height)
        => EnsureSize(width, height, _requestedSamples);

    public void EnsureSize(int width, int height, int samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        samples = AntiAliasingSamples.NormalizeRequested(samples);
        if (Width == width && Height == height && _framebuffer is not null && samples == _requestedSamples && ResolveDepth == _allocatedResolveDepth)
        {
            return;
        }

        DisposeResources();
        Width = width;
        Height = height;
        _requestedSamples = samples;
        _allocatedResolveDepth = ResolveDepth;
        SampleCount = ResolveSampleCount(samples);
        ResourceFactory factory = _renderer.ResourceFactory;
        _colorTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.RenderTarget | TextureUsage.Sampled));
        _colorView = factory.CreateTextureView(_colorTexture);
        if (SampleCount > 1)
            _multisampleColor = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)width, (uint)height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.RenderTarget, AntiAliasingSamples.ToVeldrid(SampleCount)));
        // Keep off-screen targets on the same widely-supported packed depth
        // format as the swapchain. D32S8 is optional on a number of Android
        // Vulkan devices and would make RenderTexture/reflection allocation
        // fail even though the main surface is usable.
        _depthTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, PixelFormat.D24_UNorm_S8_UInt,
            SampleCount > 1 && !ResolveDepth ? TextureUsage.DepthStencil : TextureUsage.DepthStencil | TextureUsage.Sampled,
            AntiAliasingSamples.ToVeldrid(SampleCount)));
        if (SampleCount == 1) _depthView = factory.CreateTextureView(_depthTexture);
        else if(ResolveDepth)
        {
            _resolvedDepth=factory.CreateTexture(TextureDescription.Texture2D((uint)width,(uint)height,1,1,PixelFormat.D24_UNorm_S8_UInt,
                TextureUsage.DepthStencil|TextureUsage.Sampled));
            _depthView=factory.CreateTextureView(_resolvedDepth);
            _multisampleDepthView=factory.CreateTextureView(_depthTexture);
            _depthResolveFramebuffer=factory.CreateFramebuffer(new FramebufferDescription(_resolvedDepth,Array.Empty<Texture>()));
            _depthResolvePass=new(_renderer,_multisampleDepthView,_depthResolveFramebuffer);
        }
        _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(_depthTexture, _multisampleColor ?? _colorTexture));
    }

    private int ResolveSampleCount(int requested)
    {
        bool colorSupported = _renderer.Device.GetPixelFormatSupport(PixelFormat.R8_G8_B8_A8_UNorm,
            TextureType.Texture2D, TextureUsage.RenderTarget, out PixelFormatProperties color);
        bool depthSupported = _renderer.Device.GetPixelFormatSupport(PixelFormat.D24_UNorm_S8_UInt,
            TextureType.Texture2D, ResolveDepth ? TextureUsage.DepthStencil | TextureUsage.Sampled : TextureUsage.DepthStencil, out PixelFormatProperties depth);
        foreach (int candidate in AntiAliasingSamples.Choices.Reverse())
            if (candidate <= requested && colorSupported && depthSupported
                && color.IsSampleCountSupported(AntiAliasingSamples.ToVeldrid(candidate))
                && depth.IsSampleCountSupported(AntiAliasingSamples.ToVeldrid(candidate))) return candidate;
        return 1;
    }

    internal void ResolveColor(CommandList commands)
    {
        if (_multisampleColor is not null) commands.ResolveTexture(_multisampleColor, _colorTexture!);
        _depthResolvePass?.Draw(commands);
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
        if(_depthResolvePass is not null) _renderer.RetireResource(_depthResolvePass);
        if(_depthResolveFramebuffer is not null) _renderer.RetireResource(_depthResolveFramebuffer);
        if(_multisampleDepthView is not null) _renderer.RetireResource(_multisampleDepthView);
        if(_resolvedDepth is not null) _renderer.RetireResource(_resolvedDepth);
        _depthResolvePass=null; _depthResolveFramebuffer=null; _multisampleDepthView=null; _resolvedDepth=null;
        if (_framebuffer is not null) _renderer.RetireResource(_framebuffer);
        _framebuffer = null;
        if (_multisampleColor is not null) _renderer.RetireResource(_multisampleColor);
        _multisampleColor = null;
        if (_colorView is not null) _renderer.RetireResource(_colorView);
        _colorView = null;
        if (_colorTexture is not null) _renderer.RetireResource(_colorTexture);
        _colorTexture = null;
        if (_depthView is not null) _renderer.RetireResource(_depthView);
        _depthView = null;
        if (_depthTexture is not null) _renderer.RetireResource(_depthTexture);
        _depthTexture = null;
    }
}
