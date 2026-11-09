using System.Runtime.InteropServices;
using Veldrid;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>
/// Host-visible vertex snapshots. Separate ranges protect draws in the same
/// frame (shadow, reflection, camera); frame fences protect reuse across frames.
/// Pages remain alive when growing, including pages referenced by earlier draws.
/// </summary>
internal sealed class VeldridVertexArena(Veldrid.GraphicsDevice device)
    : VeldridGeometryArena(device, BufferUsage.VertexBuffer, 256 * 1024)
{
    public VeldridVertexSlice Upload<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        VeldridGeometrySlice slice = UploadBytes(MemoryMarshal.AsBytes(values));
        return new(slice.Buffer, slice.Offset);
    }
}

internal readonly record struct VeldridVertexSlice(DeviceBuffer Buffer, uint Offset)
{
    public void Bind(CommandList commands) => commands.SetVertexBuffer(0, Buffer, Offset);
}
