using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Vulkan;

// Execute the emitted runtime IL against a deliberately asynchronous WSI model.
// Offscreen draw/readback tests cannot detect a missing present wait semaphore.
public static unsafe class VulkanPresentationVerification
{
    private static readonly object QueueLock = new();
    private static readonly HashSet<ulong> Live = [];
    private static readonly HashSet<ulong> Signals = [];
    private static readonly Dictionary<uint, ulong> PendingPresents = [];
    private static ulong _next;
    private static int _creates, _submits, _presents, _waits, _destroys, _swapchainDestroys;
    private static VkResult _createResult, _submitResult, _presentResult, _waitResult, _acquireResult;
    private static int _recreated;
    private static readonly VkQueue GraphicsQueue = new((IntPtr)11);
    private static readonly VkQueue PresentQueue = new((IntPtr)12);

    private delegate VkResult PresentCall(VkQueue queue, ref VkPresentInfoKHR info,
        VkDevice device, VkQueue graphics, object queueLock, ref VkSemaphore[]? semaphores);
    private delegate void DestroyCall(VkDevice device, VkSwapchainKHR swapchain,
        VkAllocationCallbacks* allocator, VkQueue queue, ref VkSemaphore[]? semaphores);

    internal static void Run(string path)
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(path);
        AndroidVulkanPresentationPatch.Check(module);
        TypeDefinition runtime = module.GetType(AndroidVulkanPresentationPatch.RuntimeName);
        foreach (MethodDefinition method in runtime.Methods)
            foreach (var instruction in method.Body.Instructions)
                if (instruction.Operand is MethodReference native && native.DeclaringType.FullName == "Vulkan.VulkanNative")
                    instruction.Operand = module.ImportReference(typeof(VulkanPresentationVerification).GetMethod(native.Name)!);
        TypeDefinition chainType = module.GetType("Veldrid.Vk.VkSwapchain");
        MethodDefinition acquireMethod = chainType.Methods.Single(m => m.Name == "AcquireNextImage");
        foreach (Instruction instruction in acquireMethod.Body.Instructions)
            if (instruction.Operand is MethodReference call && call.Name == "vkAcquireNextImageKHR")
                instruction.Operand = module.ImportReference(typeof(VulkanPresentationVerification).GetMethod(call.Name)!);
        MethodDefinition recreateMethod = chainType.Methods.Single(m => m.Name == "RecreateAndReacquire");
        recreateMethod.Body = new(recreateMethod);
        recreateMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Call,
            module.ImportReference(typeof(VulkanPresentationVerification).GetMethod(nameof(Recreated))!)));
        recreateMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        MethodDefinition setImage = module.GetType("Veldrid.Vk.VkSwapchainFramebuffer").Methods.Single(m => m.Name == "SetImageIndex");
        setImage.Body = new(setImage);
        setImage.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        using MemoryStream stream = new();
        module.Write(stream);
        stream.Position = 0;
        var context = new AssemblyLoadContext("Vulkan asynchronous present verification", true);
        context.Resolving += (_, name) => Assembly.Load(name);
        try
        {
            Assembly assembly = context.LoadFromStream(stream);
            Type type = assembly.GetType(AndroidVulkanPresentationPatch.RuntimeName, true)!;
            PresentCall present = type.GetMethod("Present", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<PresentCall>();
            DestroyCall destroy = type.GetMethod("DestroySwapchain", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<DestroyCall>();
            Reset();
            VkSemaphore[]? semaphores = null;
            VkSwapchainKHR swapchain = new(1);
            VkPresentInfoKHR info = VkPresentInfoKHR.New();
            info.swapchainCount = 1;
            info.pSwapchains = &swapchain;
            uint index = 0;
            info.pImageIndices = &index;
            // Non-round-robin acquisition, four images and repeated images:
            // this cannot accidentally pass with the engine's three frame slots.
            uint[] order = [2, 0, 3, 1, 1, 3, 0, 2];
            for (int frame = 0; frame < 240; frame++)
            {
                index = order[frame % order.Length];
                Reacquire(index);
                Check(present(PresentQueue, ref info, default, GraphicsQueue, QueueLock, ref semaphores) == VkResult.Success,
                    "Presentation failed.");
                Check(info.pWaitSemaphores == null && info.waitSemaphoreCount == 0, "Present retained a stack pointer.");
                Check(_waits == 0, "A normal frame waited for queue idle.");
            }
            Check(_creates == 4 && _submits == 240 && _presents == 240, "Semaphores must be pooled by swapchain image.");
            destroy(default, swapchain, null, PresentQueue, ref semaphores);
            Check(semaphores is null && Live.Count == 0 && _destroys == 4 && _waits == 1, "Resize did not retire present resources.");
            index = 0;
            present(PresentQueue, ref info, default, GraphicsQueue, QueueLock, ref semaphores);
            Check(_creates == 5, "A recreated swapchain reused a destroyed semaphore.");
            destroy(default, swapchain, null, PresentQueue, ref semaphores);
            Check(_destroys == 5 && _swapchainDestroys == 2, "Disposal leaked a semaphore or swapchain.");

            foreach (string failure in new[] { "create", "submit", "present" })
            {
                Reset();
                semaphores = null;
                if (failure == "create") _createResult = VkResult.ErrorOutOfDeviceMemory;
                if (failure == "submit") _submitResult = _waitResult = VkResult.ErrorDeviceLost;
                if (failure == "present") _presentResult = VkResult.ErrorSurfaceLostKHR;
                bool threw = false;
                try { present(PresentQueue, ref info, default, GraphicsQueue, QueueLock, ref semaphores); }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("Vulkan presentation failed:")) { threw = true; }
                Check(threw, "Native " + failure + " failure was ignored.");
                Check(_presents == (failure == "present" ? 1 : 0), "An unsuccessful signal was presented.");
                Check(info.pWaitSemaphores == null, "A failed present retained a stack pointer.");
                destroy(default, swapchain, null, PresentQueue, ref semaphores);
                Check(Live.Count == 0, "Error recovery leaked semaphores.");
            }
            foreach (VkResult result in new[] { VkResult.SuboptimalKHR, VkResult.ErrorOutOfDateKHR })
            {
                Reset();
                semaphores = null;
                _presentResult = result;
                Check(present(PresentQueue, ref info, default, GraphicsQueue, QueueLock, ref semaphores) == result,
                    "The swapchain recreation result was swallowed.");
                destroy(default, swapchain, null, PresentQueue, ref semaphores);
            }
            Type swapchainType = assembly.GetType(chainType.FullName, true)!;
            object chain = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(swapchainType);
            object framebuffer = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                assembly.GetType("Veldrid.Vk.VkSwapchainFramebuffer", true)!);
            swapchainType.GetField("_framebuffer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(chain, framebuffer);
            MethodInfo acquire = swapchainType.GetMethod("AcquireNextImage")!;
            _recreated = 0;
            foreach (VkResult result in new[] { VkResult.Success, VkResult.SuboptimalKHR, VkResult.ErrorOutOfDateKHR })
            {
                _acquireResult = result;
                bool acquired = (bool)acquire.Invoke(chain, [default(VkDevice), VkSemaphore.Null, VkFence.Null])!;
                Check(acquired == (result != VkResult.ErrorOutOfDateKHR), "Acquire did not preserve the caller's fence wait.");
                Check(_recreated == (result == VkResult.ErrorOutOfDateKHR ? 1 : 0), "Out-of-date images must be recreated AND reacquired.");
            }
            Console.WriteLine("PASS emitted Vulkan present synchronization: 240 asynchronous frames, 4 image indices, resize/disposal, native failures and reacquisition; no per-frame idle.");
        }
        finally { context.Unload(); }
    }

    public static VkResult vkCreateSemaphore(VkDevice device, ref VkSemaphoreCreateInfo create,
        VkAllocationCallbacks* allocator, out VkSemaphore semaphore)
    {
        semaphore = default;
        if (_createResult != VkResult.Success) return _createResult;
        semaphore = new(++_next);
        Live.Add(semaphore.Handle);
        _creates++;
        return VkResult.Success;
    }

    public static VkResult vkQueueSubmit(VkQueue queue, uint count, ref VkSubmitInfo submit, VkFence fence)
    {
        Check(queue == GraphicsQueue && Monitor.IsEntered(QueueLock), "Signal must use the locked graphics queue.");
        Check(count == 1 && submit.commandBufferCount == 0 && submit.signalSemaphoreCount == 1, "Missing signal submission.");
        Check(submit.waitSemaphoreCount == 0 && fence == VkFence.Null, "Unexpected CPU synchronization.");
        if (_submitResult != VkResult.Success) return _submitResult;
        ulong semaphore = submit.pSignalSemaphores[0].Handle;
        Check(Live.Contains(semaphore) && !PendingPresents.ContainsValue(semaphore) && Signals.Add(semaphore),
            "A semaphore was signaled again before its previous presentation retired.");
        _submits++;
        return VkResult.Success;
    }

    public static VkResult vkQueuePresentKHR(VkQueue queue, ref VkPresentInfoKHR info)
    {
        Check(queue == PresentQueue && info.waitSemaphoreCount == 1 && info.pWaitSemaphores != null,
            "Presentation raced unfinished rendering: missing wait.");
        ulong semaphore = info.pWaitSemaphores[0].Handle;
        Check(Signals.Remove(semaphore), "Present did not wait on the graphics completion signal.");
        Check(PendingPresents.TryAdd(info.pImageIndices[0], semaphore), "Image was not reacquired.");
        _presents++;
        return _presentResult;
    }

    public static VkResult vkQueueWaitIdle(VkQueue queue)
    {
        Check(queue == PresentQueue, "Retirement must include the presentation queue.");
        PendingPresents.Clear();
        Signals.Clear();
        _waits++;
        return _waitResult;
    }

    public static void vkDestroySemaphore(VkDevice device, VkSemaphore semaphore, VkAllocationCallbacks* allocator)
    {
        Check(!PendingPresents.ContainsValue(semaphore.Handle) && Live.Remove(semaphore.Handle),
            "Destroyed an in-use or already destroyed semaphore.");
        _destroys++;
    }

    public static void vkDestroySwapchainKHR(VkDevice device, VkSwapchainKHR swapchain, VkAllocationCallbacks* allocator)
    {
        Check(Live.Count == 0, "Swapchain destruction did not release its semaphores.");
        _swapchainDestroys++;
    }

    private static void Reacquire(uint index) => PendingPresents.Remove(index);
    public static void Recreated() => _recreated++;
    public static VkResult vkAcquireNextImageKHR(VkDevice device, VkSwapchainKHR swapchain, ulong timeout,
        VkSemaphore semaphore, VkFence fence, ref uint index)
    {
        index = 2;
        return _acquireResult;
    }
    private static void Reset()
    {
        Live.Clear(); Signals.Clear(); PendingPresents.Clear();
        _creates = _submits = _presents = _waits = _destroys = _swapchainDestroys = 0;
        _createResult = _submitResult = _presentResult = _waitResult = VkResult.Success;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
