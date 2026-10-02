using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Veldrid;
using Vulkan;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>Memory dependencies missing from Veldrid 4.9's compute/copy API.</summary>
internal static unsafe class VeldridVulkanSynchronization
{
    private static readonly PropertyInfo CommandBufferProperty = FindCommandBufferProperty();

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, "Veldrid.Vk.VkCommandList", "Veldrid")]
    private static PropertyInfo FindCommandBufferProperty()
        => typeof(CommandList).Assembly.GetType("Veldrid.Vk.VkCommandList")
            ?.GetProperty("CommandBuffer", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new NotSupportedException("This Veldrid version does not expose its Vulkan command buffer.");

    public static void PrepareSkinning(CommandList commands)
        => Barrier(commands, VkPipelineStageFlags.AllCommands,
            VkAccessFlags.MemoryRead | VkAccessFlags.MemoryWrite,
            VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.Transfer,
            VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite | VkAccessFlags.TransferWrite);

    public static void ComputeToTransfer(CommandList commands)
        => Barrier(commands, VkPipelineStageFlags.ComputeShader, VkAccessFlags.ShaderWrite,
            VkPipelineStageFlags.Transfer, VkAccessFlags.TransferRead);

    public static void TransferToHost(CommandList commands)
        => Barrier(commands, VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite,
            VkPipelineStageFlags.Host, VkAccessFlags.HostRead);

    // Veldrid CopyBuffer only supplies Transfer -> VertexInput. Uniform and
    // storage buffers also need visibility in vertex/fragment shaders. Record
    // this immediately after UpdateBuffer, which has ended any active render
    // pass; a pipeline barrier inserted in an active render pass is not valid.
    private static void TransferToGraphics(CommandList commands)
        => Barrier(commands, VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite,
            VkPipelineStageFlags.AllGraphics,
            VkAccessFlags.UniformRead | VkAccessFlags.ShaderRead |
            VkAccessFlags.VertexAttributeRead | VkAccessFlags.IndexRead);

    public static void UpdateGraphicsBuffer<T>(this CommandList commands, DeviceBuffer buffer, uint offset, T data)
        where T : unmanaged
    {
        commands.UpdateBuffer(buffer, offset, data);
        TransferToGraphics(commands);
    }

    public static void UpdateGraphicsBuffer<T>(this CommandList commands, DeviceBuffer buffer, uint offset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        if (data.IsEmpty) return;
        commands.UpdateBuffer(buffer, offset, data);
        TransferToGraphics(commands);
    }

    public static void UpdateGraphicsBuffer<T>(this CommandList commands, DeviceBuffer buffer, uint offset, T[] data)
        where T : unmanaged => commands.UpdateGraphicsBuffer(buffer, offset, (ReadOnlySpan<T>)data);

    public static void UpdateGraphicsBuffer<T>(this CommandList commands, DeviceBuffer buffer, uint offset, Span<T> data)
        where T : unmanaged => commands.UpdateGraphicsBuffer(buffer, offset, (ReadOnlySpan<T>)data);

    public static void UpdateGraphicsBuffer(this CommandList commands, DeviceBuffer buffer, uint offset, nint data, uint size)
    {
        if (size == 0) return;
        commands.UpdateBuffer(buffer, offset, data, size);
        TransferToGraphics(commands);
    }

    private static void Barrier(CommandList commands, VkPipelineStageFlags sourceStage,
        VkAccessFlags sourceAccess, VkPipelineStageFlags destinationStage, VkAccessFlags destinationAccess)
    {
        // VkCommandList is internal, but its public property returns the Vk
        // binding already used by Veldrid. Keep the getter for Android trimming.
        // Resolve it on each call: Begin() can rotate the native command buffer.
        VkCommandBuffer commandBuffer = (VkCommandBuffer)CommandBufferProperty.GetValue(commands)!;
        VkMemoryBarrier barrier = VkMemoryBarrier.New();
        barrier.srcAccessMask = sourceAccess;
        barrier.dstAccessMask = destinationAccess;
        VulkanNative.vkCmdPipelineBarrier(commandBuffer, sourceStage, destinationStage,
            VkDependencyFlags.None, 1, ref barrier, 0, null, 0, null);
    }
}
