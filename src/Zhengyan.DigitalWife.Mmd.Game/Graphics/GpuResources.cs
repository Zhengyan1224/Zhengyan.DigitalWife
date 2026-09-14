using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGLES;
using Veldrid;
using VeldridSampler = Veldrid.Sampler;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public enum GpuBufferKind
{
    Vertex,
    Index,
    Uniform
}

public readonly record struct GpuBufferDescription(uint SizeInBytes, GpuBufferKind Kind, bool Dynamic = false);

public interface IGpuBuffer : IDisposable
{
    GraphicsBackend Backend { get; }
    GpuBufferKind Kind { get; }
    uint SizeInBytes { get; }
    uint LegacyBufferId { get; }
    object? NativeResource { get; }
    void Update<T>(ReadOnlySpan<T> data, uint offsetInBytes = 0) where T : unmanaged;
}

public enum GpuSamplerAddressMode
{
    Repeat,
    ClampToEdge
}

public enum GpuSamplerFilter
{
    Linear,
    Point
}

public readonly record struct GpuSamplerDescription(
    GpuSamplerAddressMode AddressModeU = GpuSamplerAddressMode.Repeat,
    GpuSamplerAddressMode AddressModeV = GpuSamplerAddressMode.Repeat,
    GpuSamplerFilter Filter = GpuSamplerFilter.Linear);

public interface IGpuSampler : IDisposable
{
    GraphicsBackend Backend { get; }
    GpuSamplerDescription Description { get; }
    uint LegacySamplerId { get; }
    object? NativeResource { get; }
}

internal sealed unsafe class OpenGlGpuBuffer : IGpuBuffer
{
    private readonly GL _gl;
    private readonly GLEnum _target;
    private bool _disposed;

    public OpenGlGpuBuffer(GL gl, GpuBufferDescription description)
    {
        _gl = gl;
        Kind = description.Kind;
        SizeInBytes = description.SizeInBytes;
        _target = description.Kind switch
        {
            GpuBufferKind.Index => GLEnum.ElementArrayBuffer,
            GpuBufferKind.Uniform => GLEnum.UniformBuffer,
            _ => GLEnum.ArrayBuffer
        };
        LegacyBufferId = gl.GenBuffer();
        gl.BindBuffer(_target, LegacyBufferId);
        gl.BufferData(_target, SizeInBytes, null, description.Dynamic ? GLEnum.DynamicDraw : GLEnum.StaticDraw);
        gl.BindBuffer(_target, 0);
    }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;
    public GpuBufferKind Kind { get; }
    public uint SizeInBytes { get; }
    public uint LegacyBufferId { get; }
    public object NativeResource => LegacyBufferId;

    public void Update<T>(ReadOnlySpan<T> data, uint offsetInBytes = 0) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint byteCount = checked((uint)(data.Length * Marshal.SizeOf<T>()));
        if (offsetInBytes > SizeInBytes || byteCount > SizeInBytes - offsetInBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "The update does not fit inside the GPU buffer.");
        }

        _gl.BindBuffer(_target, LegacyBufferId);
        fixed (T* pointer = data)
        {
            _gl.BufferSubData(_target, (nint)offsetInBytes, byteCount, pointer);
        }

        _gl.BindBuffer(_target, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteBuffer(LegacyBufferId);
        GC.SuppressFinalize(this);
    }
}

internal sealed class VeldridGpuBuffer : IGpuBuffer
{
    private const int FrameSlotCount = 3;
    private readonly VulkanRenderer _renderer;
    private readonly DeviceBuffer[] _buffers;
    private bool _disposed;

    public VeldridGpuBuffer(VulkanRenderer renderer, GpuBufferDescription description)
    {
        _renderer = renderer;
        Kind = description.Kind;
        SizeInBytes = description.SizeInBytes;
        BufferUsage usage = description.Kind switch
        {
            GpuBufferKind.Index => BufferUsage.IndexBuffer,
            GpuBufferKind.Uniform => BufferUsage.UniformBuffer,
            _ => BufferUsage.VertexBuffer
        };
        if (description.Dynamic)
        {
            usage |= BufferUsage.Dynamic;
        }

        int count = description.Dynamic ? FrameSlotCount : 1;
        _buffers = Enumerable.Range(0, count)
            .Select(_ => renderer.ResourceFactory.CreateBuffer(new BufferDescription(SizeInBytes, usage)))
            .ToArray();
    }

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;
    public GpuBufferKind Kind { get; }
    public uint SizeInBytes { get; }
    public uint LegacyBufferId => 0;
    public object NativeResource => CurrentBuffer;
    internal DeviceBuffer NativeBuffer => CurrentBuffer;
    internal DeviceBuffer GetBufferForSlot(int slot) => _buffers[Math.Clamp(slot, 0, _buffers.Length - 1)];
    private DeviceBuffer CurrentBuffer => _buffers[Math.Min(_renderer.CurrentFrameSlot, _buffers.Length - 1)];

    public void Update<T>(ReadOnlySpan<T> data, uint offsetInBytes = 0) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint byteCount = checked((uint)(data.Length * Marshal.SizeOf<T>()));
        if (offsetInBytes > SizeInBytes || byteCount > SizeInBytes - offsetInBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "The update does not fit inside the GPU buffer.");
        }

        _renderer.Device.UpdateBuffer(CurrentBuffer, offsetInBytes, data);
    }

    internal void UpdateAllSlots<T>(ReadOnlySpan<T> data, uint offsetInBytes = 0) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint byteCount = checked((uint)(data.Length * Marshal.SizeOf<T>()));
        if (offsetInBytes > SizeInBytes || byteCount > SizeInBytes - offsetInBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "The update does not fit inside the GPU buffer.");
        }

        foreach (DeviceBuffer buffer in _buffers)
        {
            _renderer.Device.UpdateBuffer(buffer, offsetInBytes, data);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (DeviceBuffer buffer in _buffers) buffer.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class OpenGlGpuSampler : IGpuSampler
{
    private readonly GL _gl;
    private bool _disposed;

    public OpenGlGpuSampler(GL gl, GpuSamplerDescription description)
    {
        _gl = gl;
        Description = description;
        LegacySamplerId = gl.GenSampler();
        gl.SamplerParameter(LegacySamplerId, GLEnum.TextureWrapS, (int)ToAddressMode(description.AddressModeU));
        gl.SamplerParameter(LegacySamplerId, GLEnum.TextureWrapT, (int)ToAddressMode(description.AddressModeV));
        gl.SamplerParameter(LegacySamplerId, GLEnum.TextureMinFilter, (int)ToMinFilter(description.Filter));
        gl.SamplerParameter(LegacySamplerId, GLEnum.TextureMagFilter, (int)ToMagFilter(description.Filter));
    }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;
    public GpuSamplerDescription Description { get; }
    public uint LegacySamplerId { get; }
    public object NativeResource => LegacySamplerId;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteSampler(LegacySamplerId);
        GC.SuppressFinalize(this);
    }

    private static GLEnum ToAddressMode(GpuSamplerAddressMode mode)
    {
        return mode == GpuSamplerAddressMode.ClampToEdge
            ? GLEnum.ClampToEdge
            : GLEnum.Repeat;
    }

    private static GLEnum ToMinFilter(GpuSamplerFilter filter)
    {
        return filter == GpuSamplerFilter.Point ? GLEnum.Nearest : GLEnum.LinearMipmapLinear;
    }

    private static GLEnum ToMagFilter(GpuSamplerFilter filter)
    {
        return filter == GpuSamplerFilter.Point ? GLEnum.Nearest : GLEnum.Linear;
    }
}

internal sealed class VeldridGpuSampler : IGpuSampler
{
    public VeldridGpuSampler(VulkanRenderer renderer, GpuSamplerDescription description)
    {
        Description = description;
        NativeSampler = renderer.ResourceFactory.CreateSampler(new SamplerDescription(
            ToAddressMode(description.AddressModeU),
            ToAddressMode(description.AddressModeV),
            SamplerAddressMode.Wrap,
            description.Filter == GpuSamplerFilter.Point
                ? SamplerFilter.MinPoint_MagPoint_MipPoint
                : SamplerFilter.MinLinear_MagLinear_MipLinear,
            null,
            0,
            0,
            uint.MaxValue,
            0,
            SamplerBorderColor.TransparentBlack));
    }

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;
    public GpuSamplerDescription Description { get; }
    public uint LegacySamplerId => 0;
    public object NativeResource => NativeSampler;
    internal VeldridSampler NativeSampler { get; }
    public void Dispose() => NativeSampler.Dispose();

    private static SamplerAddressMode ToAddressMode(GpuSamplerAddressMode mode)
    {
        return mode == GpuSamplerAddressMode.ClampToEdge
            ? SamplerAddressMode.Clamp
            : SamplerAddressMode.Wrap;
    }
}
