using Vulkan;

// Copied into the private Android Veldrid assembly by the build patch. This
// class must only depend on the BCL and vk, never on the build tool at runtime.
internal static unsafe class VulkanPresentRuntime
{
    internal static VkResult Present(VkQueue presentQueue, ref VkPresentInfoKHR present,
        VkDevice device, VkQueue graphicsQueue, object graphicsQueueLock, ref VkSemaphore[]? semaphores)
    {
        if (present.swapchainCount != 1 || present.waitSemaphoreCount != 0)
            throw new InvalidOperationException("Unexpected Veldrid Vulkan presentation contract.");

        // The acquire fence has been waited by Veldrid. Reacquiring this IMAGE
        // retires its previous present wait. A graphics frame fence alone does
        // not make a present semaphore reusable (nor does the CPU frame slot).
        uint imageIndex = present.pImageIndices[0];
        if (semaphores is null || imageIndex >= semaphores.Length)
            Array.Resize(ref semaphores, checked((int)imageIndex + 1));
        if (semaphores[imageIndex] == VkSemaphore.Null)
        {
            VkSemaphoreCreateInfo create = VkSemaphoreCreateInfo.New();
            VkSemaphore created;
            RequireSuccess(VulkanNative.vkCreateSemaphore(device, ref create, null, out created));
            semaphores[imageIndex] = created;
        }

        VkSemaphore finished = semaphores[imageIndex];
        VkSubmitInfo signal = VkSubmitInfo.New();
        signal.signalSemaphoreCount = 1;
        signal.pSignalSemaphores = &finished;
        // A signal-only submission includes all earlier graphics submissions in
        // its synchronization scope, including the final MSAA resolve/copy.
        // No CPU fence wait or queue/device idle is needed per frame.
        lock (graphicsQueueLock)
            RequireSuccess(VulkanNative.vkQueueSubmit(graphicsQueue, 1, ref signal, VkFence.Null));

        present.waitSemaphoreCount = 1;
        present.pWaitSemaphores = &finished;
        try
        {
            VkResult result = VulkanNative.vkQueuePresentKHR(presentQueue, ref present);
            if (result != VkResult.ErrorOutOfDateKHR && result != VkResult.SuboptimalKHR)
                RequireSuccess(result);
            return result;
        }
        finally
        {
            present.waitSemaphoreCount = 0;
            present.pWaitSemaphores = null;
        }
    }

    internal static void DestroySwapchain(VkDevice device, VkSwapchainKHR swapchain,
        VkAllocationCallbacks* allocator, VkQueue presentQueue, ref VkSemaphore[]? semaphores)
    {
        if (semaphores is not null)
        {
            // Called only on resize/disposal, after Veldrid retires rendering.
            // The presentation queue may be distinct from the graphics queue.
            VkResult idle = VulkanNative.vkQueueWaitIdle(presentQueue);
            if (idle != VkResult.ErrorDeviceLost) RequireSuccess(idle);
            foreach (VkSemaphore semaphore in semaphores)
                if (semaphore != VkSemaphore.Null)
                    VulkanNative.vkDestroySemaphore(device, semaphore, null);
            semaphores = null;
        }
        VulkanNative.vkDestroySwapchainKHR(device, swapchain, allocator);
    }

    internal static VkResult NormalizeAcquireResult(VkResult result)
        // SUBOPTIMAL still acquired an image and scheduled the acquire fence.
        // Let the caller wait/reset that fence and use the image normally.
        // Recreate once OUT_OF_DATE is reported, or on an explicit resize.
        => result == VkResult.SuboptimalKHR ? VkResult.Success : result;

    private static void RequireSuccess(VkResult result)
    {
        if (result != VkResult.Success)
            throw new InvalidOperationException("Vulkan presentation failed: " + result);
    }
}
