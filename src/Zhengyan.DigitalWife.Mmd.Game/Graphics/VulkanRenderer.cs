using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Extensions.Veldrid;
using Veldrid;
using VeldridDevice = Veldrid.GraphicsDevice;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>
/// Vulkan device and swapchain owner. Scene resources are intentionally not exposed here:
/// the remaining legacy components must be migrated to the renderer resource API before
/// they can issue Vulkan commands.
/// </summary>
public sealed class VulkanRenderer : IRenderer
{
    private VeldridDevice? _device;
    private CommandList? _commandList;
    private bool _frameOpen;

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public string Name => _device is null ? "Vulkan" : $"Vulkan ({_device.DeviceName})";

    public Vector2D<int> BackBufferSize { get; private set; }

    internal VeldridDevice Device => _device
        ?? throw new InvalidOperationException("The Vulkan renderer has not been initialized.");

    internal ResourceFactory ResourceFactory => Device.ResourceFactory;

    internal CommandList CommandList => _commandList
        ?? throw new InvalidOperationException("The Vulkan command list has not been initialized.");

    internal bool IsFrameOpen => _frameOpen;

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
        _commandList.ClearColorTarget(0, new RgbaFloat(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));
        _commandList.ClearDepthStencil(1f);
    }

    internal void EndRenderTarget(VeldridRenderTarget target)
    {
        _commandList?.SetFramebuffer(Device.SwapchainFramebuffer);
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
        _commandList = _device.ResourceFactory.CreateCommandList();
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
        commands.ClearColorTarget(0, new RgbaFloat(color.X, color.Y, color.Z, color.W));
        commands.ClearDepthStencil(1.0f);
        _frameOpen = true;
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
        device.SubmitCommands(commands);
        device.SwapBuffers();
        _frameOpen = false;
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
            _device.Dispose();
            _commandList = null;
            _device = null;
        }
    }
}
