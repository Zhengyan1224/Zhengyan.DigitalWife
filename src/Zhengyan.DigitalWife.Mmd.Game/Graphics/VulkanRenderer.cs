using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Extensions.Veldrid;
using Veldrid;
using VeldridDevice = Veldrid.GraphicsDevice;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>
/// Vulkan device, swapchain, and command-list owner. Backend-specific pass renderers receive
/// this object and own their Veldrid resources.
/// </summary>
public sealed class VulkanRenderer : IRenderer
{
    private const int ReadbackSlotCount = 3;

    private VeldridDevice? _device;
    private CommandList? _commandList;
    private readonly List<ReadbackSlot> _readbackSlots = [];
    private ReadbackSlot? _pendingReadbackSlot;
    private int _readbackWidth;
    private int _readbackHeight;
    private PixelFormat _readbackFormat;
    private VeldridUtilityPassRenderer? _utilityPasses;
    private Texture? _multisampleColor;
    private Texture? _multisampleDepth;
    private Framebuffer? _multisampleFramebuffer;
    private TextureSampleCount _sampleCount = TextureSampleCount.Count1;
    private bool _mainColorResolved;
    private bool _frameOpen;
    private readonly IRenderBackendServices _services;

    public VulkanRenderer()
    {
        _services = new VulkanRenderBackendServices(this);
    }

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public string Name => _device is null ? "Vulkan" : $"Vulkan ({_device.DeviceName})";

    public int RequestedAntiAliasingSamples { get; private set; } = 1;

    public int AntiAliasingSamples => (int)_sampleCount;

    public IRenderBackendServices Services => _services;

    public Vector2D<int> BackBufferSize { get; private set; }

    internal VeldridDevice Device => _device
        ?? throw new InvalidOperationException("The Vulkan renderer has not been initialized.");

    internal ResourceFactory ResourceFactory => Device.ResourceFactory;

    internal CommandList CommandList => _commandList
        ?? throw new InvalidOperationException("The Vulkan command list has not been initialized.");

    internal bool IsFrameOpen => _frameOpen;

    internal FrontFace RasterizerFrontFace
        => Device.IsClipSpaceYInverted ? FrontFace.Clockwise : FrontFace.CounterClockwise;

    internal bool RequiresProjectedTextureYFlip
        => Device.IsUvOriginTopLeft != Device.IsClipSpaceYInverted;

    internal bool UsesZeroToOneDepthRange
        => Device.IsDepthRangeZeroToOne;

    public Veldrid.GraphicsDevice NativeDevice => Device;

    public CommandList NativeCommandList => CommandList;

    public OutputDescription NativeOutputDescription => CurrentOutputDescription;

    internal OutputDescription CurrentOutputDescription { get; private set; }

    private Framebuffer MainFramebuffer => _multisampleFramebuffer ?? Device.SwapchainFramebuffer;

    public IRenderTarget CreateRenderTarget(string name)
    {
        if (_device is null)
        {
            throw new InvalidOperationException("The Vulkan renderer has not been initialized.");
        }

        return new VeldridRenderTarget(this, name);
    }

    public ITexture2D CreateTexture2D()
    {
        if (_device is null)
        {
            throw new InvalidOperationException("The Vulkan renderer has not been initialized.");
        }

        return new VeldridTexture2D(this);
    }

    public IScreenSpriteRenderer CreateScreenSpriteRenderer()
    {
        if (_device is null)
        {
            throw new InvalidOperationException("The Vulkan renderer has not been initialized.");
        }

        return new VeldridScreenSpriteRenderer(this);
    }

    public IGpuBuffer CreateBuffer(GpuBufferDescription description)
    {
        if (_device is null)
        {
            throw new InvalidOperationException("The Vulkan renderer has not been initialized.");
        }

        return new VeldridGpuBuffer(this, description);
    }

    public IGpuSampler CreateSampler(GpuSamplerDescription description)
    {
        if (_device is null)
        {
            throw new InvalidOperationException("The Vulkan renderer has not been initialized.");
        }

        return new VeldridGpuSampler(this, description);
    }

    public void RestoreBackBuffer()
    {
        if (_frameOpen)
        {
            _commandList?.SetFramebuffer(MainFramebuffer);
            CurrentOutputDescription = MainFramebuffer.OutputDescription;
            _commandList?.SetFullViewports();
            _commandList?.SetFullScissorRects();
        }
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        if (_frameOpen)
            _commandList!.SetViewport(0, new Viewport(x, y, Math.Max(width, 1), Math.Max(height, 1), 0, 1));
    }

    public void SetScissor(int x, int y, int width, int height, bool enabled)
    {
        if (!_frameOpen) return;
        if (enabled) _commandList!.SetScissorRect(0, (uint)Math.Max(x, 0), (uint)Math.Max(y, 0), (uint)Math.Max(width, 1), (uint)Math.Max(height, 1));
        else _commandList!.SetFullScissorRects();
    }

    public unsafe bool TryReadBackBufferRgba(Span<byte> destination)
    {
        int width = Math.Max(BackBufferSize.X, 1);
        int height = Math.Max(BackBufferSize.Y, 1);
        int required = checked(width * height * 4);
        if (!_frameOpen || destination.Length < required)
        {
            return false;
        }

        Texture source = Device.SwapchainFramebuffer.ColorTargets[0].Target;
        ResolveMainColor();
        EnsureReadbackSlots(width, height, source.Format);

        bool copiedResult = false;
        foreach (ReadbackSlot slot in _readbackSlots)
        {
            if (!slot.InFlight || !slot.Fence.Signaled)
            {
                continue;
            }

            CopyMappedReadback(slot.Texture, destination, width, height, source.Format);
            slot.InFlight = false;
            copiedResult = true;
            break;
        }

        if (_pendingReadbackSlot is null)
        {
            ReadbackSlot? available = _readbackSlots.FirstOrDefault(static slot => !slot.InFlight);
            if (available is not null)
            {
                _commandList!.CopyTexture(source, available.Texture);
                _pendingReadbackSlot = available;
            }
        }

        return copiedResult;
    }

    private void EnsureReadbackSlots(int width, int height, PixelFormat format)
    {
        if (_readbackSlots.Count == ReadbackSlotCount
            && _readbackWidth == width
            && _readbackHeight == height
            && _readbackFormat == format)
        {
            return;
        }

        if (_readbackSlots.Count != 0)
        {
            Device.WaitForIdle();
            foreach (ReadbackSlot slot in _readbackSlots) slot.Dispose();
            _readbackSlots.Clear();
        }

        _pendingReadbackSlot = null;
        for (int i = 0; i < ReadbackSlotCount; i++)
        {
            Texture texture = ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                (uint)width, (uint)height, 1, 1, format, TextureUsage.Staging));
            _readbackSlots.Add(new ReadbackSlot(texture, ResourceFactory.CreateFence(false)));
        }

        _readbackWidth = width;
        _readbackHeight = height;
        _readbackFormat = format;
    }

    private unsafe void CopyMappedReadback(
        Texture texture,
        Span<byte> destination,
        int width,
        int height,
        PixelFormat format)
    {
        MappedResource mapped = Device.Map(texture, MapMode.Read);
        try
        {
            byte* sourceBytes = (byte*)mapped.Data.ToPointer();
            uint rowPitch = mapped.RowPitch;
            bool bgra = format is PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
            bool sourceTopLeft = Device.IsUvOriginTopLeft;
            for (int destinationY = 0; destinationY < height; destinationY++)
            {
                int sourceY = sourceTopLeft ? height - 1 - destinationY : destinationY;
                byte* row = sourceBytes + (sourceY * rowPitch);
                int destinationOffset = destinationY * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int sourceOffset = x * 4;
                    int targetOffset = destinationOffset + sourceOffset;
                    destination[targetOffset] = row[sourceOffset + (bgra ? 2 : 0)];
                    destination[targetOffset + 1] = row[sourceOffset + 1];
                    destination[targetOffset + 2] = row[sourceOffset + (bgra ? 0 : 2)];
                    destination[targetOffset + 3] = row[sourceOffset + 3];
                }
            }
        }
        finally
        {
            Device.Unmap(texture);
        }
    }

    internal void BeginRenderTarget(VeldridRenderTarget target, Vector4 clearColor)
    {
        if (!_frameOpen)
        {
            _commandList!.Begin();
            _frameOpen = true;
            _mainColorResolved = false;
        }

        _commandList!.SetFramebuffer(target.Framebuffer);
        CurrentOutputDescription = target.Framebuffer.OutputDescription;
        _commandList.SetFullViewports();
        _commandList.SetFullScissorRects();
        _commandList.ClearColorTarget(0, new RgbaFloat(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));
        _commandList.ClearDepthStencil(1f);
    }

    internal void ResumeRenderTarget(VeldridRenderTarget target)
    {
        if (!_frameOpen)
        {
            _commandList!.Begin();
            _frameOpen = true;
            _mainColorResolved = false;
        }

        _commandList!.SetFramebuffer(target.Framebuffer);
        CurrentOutputDescription = target.Framebuffer.OutputDescription;
        _commandList.SetFullViewports();
        _commandList.SetFullScissorRects();
    }

    internal void EndRenderTarget(VeldridRenderTarget target)
    {
        _commandList?.SetFramebuffer(MainFramebuffer);
        CurrentOutputDescription = MainFramebuffer.OutputDescription;
        _commandList?.SetFullViewports();
        _commandList?.SetFullScissorRects();
    }

    internal void BeginShadowMap(VeldridShadowMapTarget target)
    {
        if (!_frameOpen)
        {
            _commandList!.Begin();
            _frameOpen = true;
            _mainColorResolved = false;
        }

        _commandList!.SetFramebuffer(target.Framebuffer);
        CurrentOutputDescription = target.Framebuffer.OutputDescription;
        _commandList.SetFullViewports();
        _commandList.SetFullScissorRects();
        _commandList.ClearDepthStencil(1.0f);
    }

    internal void EndShadowMap(VeldridShadowMapTarget target)
    {
        _ = target;
        _commandList?.SetFramebuffer(MainFramebuffer);
        CurrentOutputDescription = MainFramebuffer.OutputDescription;
        _commandList?.SetFullViewports();
        _commandList?.SetFullScissorRects();
    }

    public static bool IsSupported(out string reason)
    {
        try
        {
            if (!VeldridDevice.IsBackendSupported(Veldrid.GraphicsBackend.Vulkan))
            {
                reason = "Veldrid did not find a Vulkan loader or a compatible physical device.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public void Initialize(IWindow window, Vector2D<int> backBufferSize, int requestedSamples)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_device is not null)
        {
            throw new InvalidOperationException("The Vulkan renderer is already initialized.");
        }

        RequestedAntiAliasingSamples = Zhengyan.DigitalWife.Mmd.Game.Graphics.AntiAliasingSamples.NormalizeRequested(requestedSamples);
        GraphicsDeviceOptions options = CreateDeviceOptions();
        VeldridDevice device = VeldridWindow.CreateGraphicsDevice(
            window,
            options,
            Veldrid.GraphicsBackend.Vulkan);
        CompleteInitialization(device, backBufferSize);
    }

    /// <summary>
    /// Initializes the Vulkan renderer for a platform-owned swapchain source, such as an Android Surface.
    /// </summary>
    public void Initialize(
        SwapchainSource swapchainSource,
        Vector2D<int> backBufferSize,
        int requestedSamples,
        bool syncToVerticalBlank = true)
    {
        if (_device is not null)
        {
            throw new InvalidOperationException("The Vulkan renderer is already initialized.");
        }

        RequestedAntiAliasingSamples = Zhengyan.DigitalWife.Mmd.Game.Graphics.AntiAliasingSamples.NormalizeRequested(requestedSamples);
        GraphicsDeviceOptions options = CreateDeviceOptions(syncToVerticalBlank);
        SwapchainDescription swapchain = new(
            swapchainSource,
            (uint)Math.Max(backBufferSize.X, 1),
            (uint)Math.Max(backBufferSize.Y, 1),
            options.SwapchainDepthFormat,
            syncToVerticalBlank);
        VeldridDevice device = VeldridDevice.CreateVulkan(options, swapchain);
        CompleteInitialization(device, backBufferSize);
    }

    private static GraphicsDeviceOptions CreateDeviceOptions(bool syncToVerticalBlank = true)
    {
        GraphicsDeviceOptions options = new(
            debug: false,
            swapchainDepthFormat: PixelFormat.D24_UNorm_S8_UInt,
            syncToVerticalBlank: syncToVerticalBlank);
        options.PreferDepthRangeZeroToOne = true;
        options.PreferStandardClipSpaceYDirection = true;
        return options;
    }

    private void CompleteInitialization(VeldridDevice device, Vector2D<int> backBufferSize)
    {
        try
        {
            _device = device;
            string deviceInfo =
                $"[Vulkan] Device='{_device.DeviceName}', " +
                $"DepthZeroToOne={_device.IsDepthRangeZeroToOne}, " +
                $"ClipSpaceYInverted={_device.IsClipSpaceYInverted}, " +
                $"UvOriginTopLeft={_device.IsUvOriginTopLeft}";
            Console.WriteLine(deviceInfo);
            _commandList = _device.ResourceFactory.CreateCommandList();
            Resize(backBufferSize);
        }
        catch
        {
            _commandList?.Dispose();
            _commandList = null;
            _device = null;
            device.Dispose();
            throw;
        }
    }

    public void Resize(Vector2D<int> backBufferSize)
    {
        BackBufferSize = backBufferSize;
        if (_device is null)
        {
            return;
        }

        if (_frameOpen)
        {
            throw new InvalidOperationException("Cannot resize a Vulkan swapchain while a frame is recording.");
        }

        if (_multisampleFramebuffer is not null)
        {
            _device.WaitForIdle();
        }

        _device.MainSwapchain.Resize(
            (uint)Math.Max(1, backBufferSize.X),
            (uint)Math.Max(1, backBufferSize.Y));
        RecreateMultisampleFramebuffer();
    }

    public void Clear(Vector4 color)
    {
        VeldridDevice device = _device ?? throw new InvalidOperationException("The Vulkan renderer has not been initialized.");
        CommandList commands = _commandList ?? throw new InvalidOperationException("The Vulkan command list has not been initialized.");
        if (_frameOpen)
        {
            throw new InvalidOperationException("A Vulkan frame is already recording.");
        }

        commands.Begin();
        commands.SetFramebuffer(MainFramebuffer);
        CurrentOutputDescription = MainFramebuffer.OutputDescription;
        _mainColorResolved = false;
        commands.ClearColorTarget(0, new RgbaFloat(color.X, color.Y, color.Z, color.W));
        commands.ClearDepthStencil(1.0f);
        _frameOpen = true;
    }

    public void ClearViewport(int x, int y, int width, int height, Vector4 color)
    {
        _utilityPasses ??= new VeldridUtilityPassRenderer(this);
        _utilityPasses.ClearViewport(x, y, width, height, color);
    }

    internal void ForceOpaqueAlpha(VeldridRenderTarget target)
    {
        _utilityPasses ??= new VeldridUtilityPassRenderer(this);
        _utilityPasses.ForceOpaqueAlpha(target);
    }

    public void Present()
    {
        VeldridDevice device = _device ?? throw new InvalidOperationException("The Vulkan renderer has not been initialized.");
        CommandList commands = _commandList ?? throw new InvalidOperationException("The Vulkan command list has not been initialized.");
        if (!_frameOpen)
        {
            return;
        }

        ResolveMainColor();
        commands.End();
        if (_pendingReadbackSlot is not null)
        {
            device.ResetFence(_pendingReadbackSlot.Fence);
            device.SubmitCommands(commands, _pendingReadbackSlot.Fence);
            _pendingReadbackSlot.InFlight = true;
            _pendingReadbackSlot = null;
        }
        else
        {
            device.SubmitCommands(commands);
        }
        device.SwapBuffers();
        _frameOpen = false;
    }

    public void WaitForIdle()
    {
        if (_device is null)
        {
            return;
        }

        if (_frameOpen)
        {
            throw new InvalidOperationException("Cannot wait for Vulkan idle while a frame is being recorded.");
        }

        _device.WaitForIdle();
    }

    public void Dispose()
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            if (_frameOpen)
            {
                _commandList?.End();
                _frameOpen = false;
            }

            _device.WaitForIdle();
        }
        finally
        {
            _commandList?.Dispose();
            _utilityPasses?.Dispose();
            _utilityPasses = null;
            _multisampleFramebuffer?.Dispose();
            _multisampleDepth?.Dispose();
            _multisampleColor?.Dispose();
            _multisampleFramebuffer = null;
            _multisampleDepth = null;
            _multisampleColor = null;
            foreach (ReadbackSlot slot in _readbackSlots) slot.Dispose();
            _readbackSlots.Clear();
            _device.Dispose();
            _commandList = null;
            _pendingReadbackSlot = null;
            _device = null;
        }
    }

    internal void SetShadowMapRegion(int x, int y, int width, int height)
    {
        if (!_frameOpen || _commandList is null)
        {
            return;
        }

        _commandList.SetViewport(0, new Viewport(
            Math.Max(x, 0),
            Math.Max(y, 0),
            Math.Max(width, 1),
            Math.Max(height, 1),
            0.0f,
            1.0f));
        _commandList.SetScissorRect(
            0,
            (uint)Math.Max(x, 0),
            (uint)Math.Max(y, 0),
            (uint)Math.Max(width, 1),
            (uint)Math.Max(height, 1));
    }

    private void RecreateMultisampleFramebuffer()
    {
        _multisampleFramebuffer?.Dispose();
        _multisampleDepth?.Dispose();
        _multisampleColor?.Dispose();
        _multisampleFramebuffer = null;
        _multisampleDepth = null;
        _multisampleColor = null;

        PixelFormat colorFormat = Device.SwapchainFramebuffer.ColorTargets[0].Target.Format;
        int colorLimit = (int)Device.GetSampleCountLimit(colorFormat, depthFormat: false);
        int depthLimit = (int)Device.GetSampleCountLimit(PixelFormat.D24_UNorm_S8_UInt, depthFormat: true);
        int maximumSupported = Math.Min(colorLimit, depthLimit);
        int actualSamples = Zhengyan.DigitalWife.Mmd.Game.Graphics.AntiAliasingSamples.FallbackToSupported(
            RequestedAntiAliasingSamples, maximumSupported);
        _sampleCount = (TextureSampleCount)actualSamples;

        if (actualSamples <= 1)
        {
            CurrentOutputDescription = Device.SwapchainFramebuffer.OutputDescription;
            return;
        }

        uint width = (uint)Math.Max(BackBufferSize.X, 1);
        uint height = (uint)Math.Max(BackBufferSize.Y, 1);
        _multisampleColor = ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, colorFormat, TextureUsage.RenderTarget, _sampleCount));
        _multisampleDepth = ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.D24_UNorm_S8_UInt,
            TextureUsage.DepthStencil, _sampleCount));
        _multisampleFramebuffer = ResourceFactory.CreateFramebuffer(
            new FramebufferDescription(_multisampleDepth, _multisampleColor));
        CurrentOutputDescription = _multisampleFramebuffer.OutputDescription;
        Console.WriteLine(
            $"[Vulkan] MSAA requested={RequestedAntiAliasingSamples}x, actual={actualSamples}x " +
            $"(color limit={colorLimit}x, depth limit={depthLimit}x)");
    }

    private void ResolveMainColor()
    {
        if (!_frameOpen || _multisampleColor is null || _mainColorResolved)
        {
            return;
        }

        _commandList!.ResolveTexture(
            _multisampleColor,
            Device.SwapchainFramebuffer.ColorTargets[0].Target);
        _mainColorResolved = true;
    }

    private sealed class ReadbackSlot(Texture texture, Fence fence) : IDisposable
    {
        public Texture Texture { get; } = texture;
        public Fence Fence { get; } = fence;
        public bool InFlight { get; set; }

        public void Dispose()
        {
            Fence.Dispose();
            Texture.Dispose();
        }
    }
}
