using System.Numerics;
using Veldrid;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

/// <summary>Uploads one CPU pose in a single queue submission, including after Compute fallback.</summary>
internal sealed class VeldridPmxPoseUploader(
    VulkanRenderer renderer, DeviceBuffer positions, DeviceBuffer normals, DeviceBuffer uvs) : IDisposable
{
    private readonly Slot?[] _slots = new Slot?[VulkanRenderer.FrameSlotCount];
    private int _nextSlot;
    private bool _disposed;

    public void Upload(ReadOnlySpan<Vector3> positionData, ReadOnlySpan<Vector3> normalData, ReadOnlySpan<Vector2> uvData)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint positionBytes = checked((uint)positionData.Length * 12);
        uint normalBytes = checked((uint)normalData.Length * 12);
        uint uvBytes = checked((uint)uvData.Length * 8);
        if (positionBytes > positions.SizeInBytes || normalBytes > normals.SizeInBytes || uvBytes > uvs.SizeInBytes)
            throw new ArgumentOutOfRangeException(nameof(positionData), "The pose does not fit inside the GPU buffers.");
        if (positionBytes == 0 && normalBytes == 0 && uvBytes == 0) return;

        Slot slot = _slots[_nextSlot] ??= new(renderer.ResourceFactory,
            checked(positions.SizeInBytes + normals.SizeInBytes + uvs.SizeInBytes));
        if (slot.Pending && !slot.Fence.Signaled) renderer.Device.WaitForFence(slot.Fence);
        // Each slot owns its host-visible source until the copy fence completes.
        // Destinations remain device-local and retain their identity for Compute.
        if (positionBytes != 0) renderer.Device.UpdateBuffer(slot.Staging, 0, positionData);
        if (normalBytes != 0) renderer.Device.UpdateBuffer(slot.Staging, positionBytes, normalData);
        if (uvBytes != 0) renderer.Device.UpdateBuffer(slot.Staging, positionBytes + normalBytes, uvData);
        slot.Commands.Begin();
        // Order earlier graphics/Compute reads and writes before overwriting the
        // shared pose; this dependency also covers preceding queue submissions.
        VeldridVulkanSynchronization.PrepareBufferTransfer(slot.Commands);
        if (positionBytes != 0) slot.Commands.CopyBuffer(slot.Staging, 0, positions, 0, positionBytes);
        if (normalBytes != 0) slot.Commands.CopyBuffer(slot.Staging, positionBytes, normals, 0, normalBytes);
        if (uvBytes != 0) slot.Commands.CopyBuffer(slot.Staging, positionBytes + normalBytes, uvs, 0, uvBytes);
        VeldridVulkanSynchronization.TransferToGraphics(slot.Commands);
        slot.Commands.End();
        renderer.Device.ResetFence(slot.Fence);
        renderer.Device.SubmitCommands(slot.Commands, slot.Fence);
        slot.Pending = true;
        _nextSlot = (_nextSlot + 1) % _slots.Length;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Slot? slot in _slots)
        {
            if (slot is null) continue;
            if (slot.Pending && !slot.Fence.Signaled) renderer.Device.WaitForFence(slot.Fence);
            slot.Commands.Dispose();
            slot.Staging.Dispose();
            slot.Fence.Dispose();
        }
    }

    private sealed class Slot(ResourceFactory factory, uint size)
    {
        public DeviceBuffer Staging { get; } = factory.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
        public CommandList Commands { get; } = factory.CreateCommandList();
        public Fence Fence { get; } = factory.CreateFence(false);
        public bool Pending { get; set; }
    }
}
