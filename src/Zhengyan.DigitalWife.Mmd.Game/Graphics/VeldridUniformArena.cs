using System.Runtime.InteropServices;
using Veldrid;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>
/// Immutable uniform snapshots for each draw, including reflection and shadow
/// passes. A slot may only be reset after its submission fence has completed.
/// </summary>
internal sealed class VeldridUniformArena(Veldrid.GraphicsDevice device) : IDisposable
{
    private const uint PageSize = 64 * 1024;
    private readonly List<DeviceBuffer>[] _pages = Enumerable.Range(0, VulkanRenderer.FrameSlotCount)
        .Select(_ => new List<DeviceBuffer>()).ToArray();
    private long _frameId = -1;
    private int _slot;
    private int _page;
    private uint _offset;

    public int AllocationCount { get; private set; }
    public uint BytesWritten { get; private set; }

    public void BeginFrame(int slot, long frameId)
    {
        if (_frameId == frameId) return;
        _frameId = frameId;
        _slot = slot;
        _page = 0;
        _offset = 0;
        AllocationCount = 0;
        BytesWritten = 0;
    }

    public VeldridUniformSlice Upload<T>(in T value) where T : unmanaged
    {
        uint size = (uint)Marshal.SizeOf<T>();
        uint alignment = Math.Max(device.UniformBufferMinOffsetAlignment, 16);
        uint stride = checked((size + alignment - 1) / alignment * alignment);
        List<DeviceBuffer> pages = _pages[_slot];
        if (_page < pages.Count && _offset + stride > pages[_page].SizeInBytes)
        {
            _page++;
            _offset = 0;
        }
        if (_page == pages.Count)
            pages.Add(device.ResourceFactory.CreateBuffer(new BufferDescription(
                Math.Max(PageSize, stride), BufferUsage.UniformBuffer | BufferUsage.Dynamic)));
        // A previously small page can be encountered by a larger allocation in
        // a later frame. Keep it alive for cached descriptor sets; skip it.
        while (pages[_page].SizeInBytes < stride)
        {
            _page++;
            _offset = 0;
            if (_page == pages.Count)
                pages.Add(device.ResourceFactory.CreateBuffer(new BufferDescription(
                    Math.Max(PageSize, stride), BufferUsage.UniformBuffer | BufferUsage.Dynamic)));
        }

        DeviceBuffer buffer = pages[_page];
        // Dynamic buffers are host-visible. Each draw gets a different range;
        // queue submission makes these host writes available to the shaders.
        // No transfer command or render-pass interruption is needed.
        device.UpdateBuffer(buffer, _offset, value);
        VeldridUniformSlice result = new(buffer, _offset, size);
        _offset += stride;
        AllocationCount++;
        BytesWritten += size;
        return result;
    }

    public void Dispose()
    {
        foreach (List<DeviceBuffer> pages in _pages)
            foreach (DeviceBuffer page in pages) page.Dispose();
    }
}

internal readonly record struct VeldridUniformSlice(DeviceBuffer Buffer, uint Offset, uint Size)
{
    public DeviceBufferRange BindingRange => new(Buffer, 0, Size);

    public void Bind(CommandList commands, uint index, ResourceSet resources)
    {
        uint offset = Offset;
        commands.SetGraphicsResourceSet(index, resources, 1, ref offset);
    }
}
