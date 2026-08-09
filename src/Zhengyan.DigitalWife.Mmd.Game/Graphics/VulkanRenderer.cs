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
    private bool _frameOpen;
    private readonly IRenderBackendServices _services;

    public VulkanRenderer()
    {
        _services = new VulkanRenderBackendServices(this);
    }

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public string Name => _device is null ? "Vulkan" : $"Vulkan ({_device.DeviceName})";

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
            _commandList?.SetFramebuffer(Device.SwapchainFramebuffer);
            CurrentOutputDescription = Device.SwapchainFramebuffer.OutputDescription;
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
                _commandList!.SetFramebuffer(Device.SwapchainFramebuffer);
                _commandList.CopyTexture(source, available.Texture);
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
        }

        _commandList!.SetFramebuffer(target.Framebuffer);
        CurrentOutputDescription = target.Framebuffer.OutputDescription;
        _commandList.SetFullViewports();
        _commandList.SetFullScissorRects();
    }

    internal void EndRenderTarget(VeldridRenderTarget target)
    {
        _commandList?.SetFramebuffer(Device.SwapchainFramebuffer);
        CurrentOutputDescription = Device.SwapchainFramebuffer.OutputDescription;
        _commandList?.SetFullViewports();
        _commandList?.SetFullScissorRects();
    }

    internal void BeginShadowMap(VeldridShadowMapTarget target)
    {
        if (!_frameOpen)
        {
            _commandList!.Begin();
            _frameOpen = true;
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
        _commandList?.SetFramebuffer(Device.SwapchainFramebuffer);
        CurrentOutputDescription = Device.SwapchainFramebuffer.OutputDescription;
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

    public void Initialize(IWindow window, Vector2D<int> backBufferSize)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_device is not null)
        {
            throw new InvalidOperationException("The Vulkan renderer is already initialized.");
        }

        GraphicsDeviceOptions options = new(
            debug: false,
            swapchainDepthFormat: PixelFormat.D24_UNorm_S8_UInt,
            syncToVerticalBlank: true);
        options.PreferDepthRangeZeroToOne = true;
        options.PreferStandardClipSpaceYDirection = true;
        _device = VeldridWindow.CreateGraphicsDevice(
            window,
            options,
            Veldrid.GraphicsBackend.Vulkan);
        string deviceInfo =
            $"[Vulkan] Device='{_device.DeviceName}', " +
            $"DepthZeroToOne={_device.IsDepthRangeZeroToOne}, " +
            $"ClipSpaceYInverted={_device.IsClipSpaceYInverted}, " +
            $"UvOriginTopLeft={_device.IsUvOriginTopLeft}";
        Console.WriteLine(deviceInfo);
        _commandList = _device.ResourceFactory.CreateCommandList();
        CurrentOutputDescription = _device.SwapchainFramebuffer.OutputDescription;
        Resize(backBufferSize);
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

        _device.MainSwapchain.Resize(
            (uint)Math.Max(1, backBufferSize.X),
            (uint)Math.Max(1, backBufferSize.Y));
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
        commands.SetFramebuffer(device.SwapchainFramebuffer);
        CurrentOutputDescription = device.SwapchainFramebuffer.OutputDescription;
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
            foreach (ReadbackSlot slot in _readbackSlots) slot.Dispose();
            _readbackSlots.Clear();
            _device.Dispose();
            _commandList = null;
            _pendingReadbackSlot = null;
            _device = null;
        }
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
