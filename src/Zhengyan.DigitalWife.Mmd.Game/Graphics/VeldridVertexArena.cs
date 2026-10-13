using System.Runtime.InteropServices;
using Veldrid;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>
/// Host-visible vertex snapshots. Separate ranges protect draws in the same
/// frame (shadow, reflection, camera); frame fences protect reuse across frames.
/// Pages remain alive when growing, including pages referenced by earlier draws.
/// </summary>
internal sealed class VeldridVertexArena(Veldrid.GraphicsDevice device) : IDisposable
{
    private const uint PageSize = 256 * 1024;
    private readonly List<DeviceBuffer>[] _pages = Enumerable.Range(0, VulkanRenderer.FrameSlotCount)
        .Select(_ => new List<DeviceBuffer>()).ToArray();
    private long _frameId = -1;
    private int _slot;
    private int _page;
    private uint _offset;

    public int AllocationCount { get; private set; }
    public ulong BytesWritten { get; private set; }

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

    public VeldridVertexSlice Upload<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(values);
        ArgumentOutOfRangeException.ThrowIfZero(bytes.Length);
        uint size = (uint)bytes.Length;
        uint stride = checked((size + 15u) / 16u * 16u);
        List<DeviceBuffer> pages = _pages[_slot];
        while (_page < pages.Count && stride > pages[_page].SizeInBytes - _offset)
        {
            _page++;
            _offset = 0;
        }
        if (_page == pages.Count)
            pages.Add(device.ResourceFactory.CreateBuffer(new BufferDescription(
                Math.Max(PageSize, stride), BufferUsage.VertexBuffer | BufferUsage.Dynamic)));

        DeviceBuffer buffer = pages[_page];
        device.UpdateBuffer(buffer, _offset, bytes);
        VeldridVertexSlice result = new(buffer, _offset);
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

internal readonly record struct VeldridVertexSlice(DeviceBuffer Buffer, uint Offset)
{
    public void Bind(CommandList commands) => commands.SetVertexBuffer(0, Buffer, Offset);
}
