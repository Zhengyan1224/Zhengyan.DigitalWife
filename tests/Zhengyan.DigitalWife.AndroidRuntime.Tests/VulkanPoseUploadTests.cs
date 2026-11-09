using System.Numerics;
using System.Reflection;
using Veldrid;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal static unsafe class VulkanPoseUploadTests
{
    public static void TestBatchedUploads()
    {
        if (!VulkanRenderer.IsSupported(out string reason))
        {
            Console.WriteLine($"SKIP batched Vulkan poses: {reason}");
            return;
        }
        using var device = Veldrid.GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions());
        VulkanRenderer renderer = new();
        typeof(VulkanRenderer).GetField("_device", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, device);
        const int count = 100012, frames = 18;
        const uint positionBytes = count * 12, uvBytes = count * 8, snapshotBytes = positionBytes * 2 + uvBytes;
        var factory = device.ResourceFactory;
        using DeviceBuffer positions = factory.CreateBuffer(new(positionBytes, BufferUsage.VertexBuffer));
        using DeviceBuffer normals = factory.CreateBuffer(new(positionBytes, BufferUsage.VertexBuffer));
        using DeviceBuffer uvs = factory.CreateBuffer(new(uvBytes, BufferUsage.VertexBuffer));
        using VeldridPmxPoseUploader uploader = new(renderer, positions, normals, uvs);
        using CommandList commands = factory.CreateCommandList();
        DeviceBuffer[] snapshots = Enumerable.Range(0, frames)
            .Select(_ => factory.CreateBuffer(new BufferDescription(snapshotBytes, BufferUsage.Staging))).ToArray();
        Vector3[] positionData = new Vector3[count], normalData = new Vector3[count];
        Vector2[] uvData = new Vector2[count];
        try
        {
            for (int frame = 0; frame < frames; frame++)
            {
                Array.Fill(positionData, new Vector3(frame, -frame, frame + .5f));
                Array.Fill(normalData, new Vector3(-frame - .25f, frame + .25f, 1));
                Array.Fill(uvData, new Vector2(frame, frame + .5f));
                // Odd frames update only position/normal; UV must retain the preceding pose.
                uploader.Upload(positionData, normalData, (frame & 1) == 0 ? uvData : []);
                commands.Begin();
                VeldridVulkanSynchronization.PrepareBufferTransfer(commands);
                commands.CopyBuffer(positions, 0, snapshots[frame], 0, positionBytes);
                commands.CopyBuffer(normals, 0, snapshots[frame], positionBytes, positionBytes);
                commands.CopyBuffer(uvs, 0, snapshots[frame], positionBytes * 2, uvBytes);
                VeldridVulkanSynchronization.TransferToHost(commands);
                commands.End();
                device.SubmitCommands(commands);
            }
            // Release pending uploads without idling the device first; disposal must retire its own slots.
            uploader.Dispose();
            device.WaitForIdle();
            for (int frame = 0; frame < frames; frame++)
            {
                MappedResource mapped = device.Map(snapshots[frame], MapMode.Read);
                try
                {
                    Vector3* actualPositions = (Vector3*)mapped.Data;
                    Vector3* actualNormals = (Vector3*)((byte*)mapped.Data + positionBytes);
                    Vector2* actualUvs = (Vector2*)((byte*)mapped.Data + positionBytes * 2);
                    for (int i = 0; i < count; i++)
                    {
                        int uvFrame = frame & ~1;
                        if (actualPositions[i] != new Vector3(frame, -frame, frame + .5f)
                            || actualNormals[i] != new Vector3(-frame - .25f, frame + .25f, 1)
                            || actualUvs[i] != new Vector2(uvFrame, uvFrame + .5f))
                            throw new InvalidOperationException($"Stale or overwritten pose in frame {frame}, vertex {i}.");
                    }
                }
                finally { device.Unmap(snapshots[frame]); }
            }
        }
        finally
        {
            device.WaitForIdle();
            foreach (DeviceBuffer snapshot in snapshots) snapshot.Dispose();
        }
    }
}
