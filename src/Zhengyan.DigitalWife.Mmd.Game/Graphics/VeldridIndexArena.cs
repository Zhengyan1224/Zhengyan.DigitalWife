using System.Runtime.InteropServices;
using Veldrid;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal sealed class VeldridIndexArena(Veldrid.GraphicsDevice device)
    : VeldridGeometryArena(device, BufferUsage.IndexBuffer, 64 * 1024)
{
    public VeldridIndexSlice Upload<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        VeldridGeometrySlice slice = UploadBytes(MemoryMarshal.AsBytes(values));
        return new(slice.Buffer, slice.Offset);
    }
}

internal readonly record struct VeldridIndexSlice(DeviceBuffer Buffer, uint Offset)
{
    public void Bind(CommandList commands, IndexFormat format) => commands.SetIndexBuffer(Buffer, format, Offset);
}
