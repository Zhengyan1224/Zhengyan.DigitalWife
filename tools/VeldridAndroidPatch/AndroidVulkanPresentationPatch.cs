using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Vulkan;

internal static class AndroidVulkanPresentationPatch
{
    internal const string RuntimeName = "Veldrid.Vk.ZhengyanVulkanPresent";
    private const string SemaphoresName = "_zhengyanPresentSemaphores";

    internal static void Apply(ModuleDefinition module)
    {
        TypeDefinition runtime = CopyRuntime(module);
        TypeDefinition swapchain = module.GetType("Veldrid.Vk.VkSwapchain");
        FieldDefinition semaphores = new(SemaphoresName, FieldAttributes.Assembly,
            new ArrayType(module.ImportReference(typeof(VkSemaphore))));
        swapchain.Fields.Add(semaphores);
        TypeDefinition device = module.GetType("Veldrid.Vk.VkGraphicsDevice");
        MethodDefinition present = device.Methods.Single(m => m.Name == "SwapBuffersCore");
        VariableDefinition chain = present.Body.Variables.Single(v => v.VariableType.FullName == swapchain.FullName);
        Instruction call = present.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "vkQueuePresentKHR");
        ILProcessor il = present.Body.GetILProcessor();
        foreach (string field in new[] { "_device", "_graphicsQueue", "_graphicsQueueLock" })
        {
            il.InsertBefore(call, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(call, il.Create(OpCodes.Ldfld, device.Fields.Single(f => f.Name == field)));
        }
        il.InsertBefore(call, il.Create(OpCodes.Ldloc, chain));
        il.InsertBefore(call, il.Create(OpCodes.Ldflda, semaphores));
        call.Operand = runtime.Methods.Single(m => m.Name == "Present");

        foreach (MethodDefinition method in swapchain.Methods.Where(m => m.Name is "CreateSwapchain" or "DisposeCore"))
        {
            Instruction destroy = method.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "vkDestroySwapchainKHR");
            ILProcessor cleanup = method.Body.GetILProcessor();
            cleanup.InsertBefore(destroy, cleanup.Create(OpCodes.Ldarg_0));
            cleanup.InsertBefore(destroy, cleanup.Create(OpCodes.Ldfld, swapchain.Fields.Single(f => f.Name == "_presentQueue")));
            cleanup.InsertBefore(destroy, cleanup.Create(OpCodes.Ldarg_0));
            cleanup.InsertBefore(destroy, cleanup.Create(OpCodes.Ldflda, semaphores));
            destroy.Operand = runtime.Methods.Single(m => m.Name == "DestroySwapchain");
        }

        // A fresh swapchain does not own an acquired image. Veldrid's original
        // OUT_OF_DATE branch only creates it and then lets rendering continue.
        MethodDefinition acquire = swapchain.Methods.Single(m => m.Name == "AcquireNextImage");
        ILProcessor acquisition = acquire.Body.GetILProcessor();
        Instruction nativeAcquire = acquire.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "vkAcquireNextImageKHR");
        acquisition.InsertAfter(nativeAcquire, acquisition.Create(OpCodes.Call, runtime.Methods.Single(m => m.Name == "NormalizeAcquireResult")));
        Instruction recreate = acquire.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "CreateSwapchain");
        if (recreate.Next.OpCode != OpCodes.Pop) throw new InvalidOperationException("Unexpected Veldrid swapchain recreation.");
        recreate.Operand = swapchain.Methods.Single(m => m.Name == "RecreateAndReacquire");
        recreate.Next.OpCode = OpCodes.Nop;
        Check(module);
    }

    internal static void Check(ModuleDefinition module)
    {
        TypeDefinition runtime = module.GetType(RuntimeName)
            ?? throw new InvalidOperationException("Vulkan present synchronization is missing.");
        CheckCall(module.GetType("Veldrid.Vk.VkGraphicsDevice").Methods.Single(m => m.Name == "SwapBuffersCore"), RuntimeName, "Present");
        foreach (string name in new[] { "CreateSwapchain", "DisposeCore" })
            CheckCall(module.GetType("Veldrid.Vk.VkSwapchain").Methods.Single(m => m.Name == name), RuntimeName, "DestroySwapchain");
        MethodDefinition acquire = module.GetType("Veldrid.Vk.VkSwapchain").Methods.Single(m => m.Name == "AcquireNextImage");
        CheckCall(acquire, RuntimeName, "NormalizeAcquireResult");
        if (acquire.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "CreateSwapchain"))
            throw new InvalidOperationException("A recreated swapchain must reacquire and wait for an image before drawing.");
        MethodDefinition present = runtime.Methods.Single(m => m.Name == "Present");
        CheckCall(present, "Vulkan.VulkanNative", "vkQueueSubmit");
        CheckCall(present, "Vulkan.VulkanNative", "vkQueuePresentKHR");
        if (present.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name is "vkQueueWaitIdle" or "vkDeviceWaitIdle" or "vkWaitForFences"))
            throw new InvalidOperationException("Presentation must not wait on the CPU each frame.");
        if (module.AssemblyReferences.Any(r => r.Name == "VeldridAndroidPatch"))
            throw new InvalidOperationException("The runtime cannot depend on the build tool.");
    }

    private static void CheckCall(MethodDefinition method, string type, string name)
    {
        if (method.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.FullName == type && m.Name == name) != 1)
            throw new InvalidOperationException($"Missing Vulkan presentation hook: {method.Name} -> {name}.");
    }

    // Copy compiled C# instead of hand-writing unsafe control flow in IL. Keep
    // local/branch/exception-handler identities and remap every external token.
    private static TypeDefinition CopyRuntime(ModuleDefinition module)
    {
        using ModuleDefinition donor = ModuleDefinition.ReadModule(typeof(VulkanPresentRuntime).Assembly.Location);
        TypeDefinition source = donor.GetType(nameof(VulkanPresentRuntime));
        TypeDefinition target = new("Veldrid.Vk", "ZhengyanVulkanPresent", source.Attributes, module.TypeSystem.Object);
        module.Types.Add(target);
        Dictionary<MethodDefinition, MethodDefinition> methods = new();
        foreach (MethodDefinition method in source.Methods)
        {
            MethodDefinition copy = new(method.Name, method.Attributes, module.ImportReference(method.ReturnType));
            foreach (ParameterDefinition parameter in method.Parameters)
                copy.Parameters.Add(new(parameter.Name, parameter.Attributes, module.ImportReference(parameter.ParameterType)));
            target.Methods.Add(copy);
            methods.Add(method, copy);
        }
        foreach ((MethodDefinition method, MethodDefinition copy) in methods)
        {
            method.Body.SimplifyMacros();
            copy.Body.InitLocals = method.Body.InitLocals;
            foreach (VariableDefinition variable in method.Body.Variables)
                copy.Body.Variables.Add(new(module.ImportReference(variable.VariableType)));
            Dictionary<Instruction, Instruction> instructions = new();
            foreach (Instruction instruction in method.Body.Instructions)
            {
                Instruction clone = Instruction.Create(OpCodes.Nop);
                clone.OpCode = instruction.OpCode;
                copy.Body.Instructions.Add(clone);
                instructions.Add(instruction, clone);
            }
            foreach ((Instruction instruction, Instruction clone) in instructions)
                clone.Operand = instruction.Operand switch
                {
                    Instruction branch => instructions[branch],
                    Instruction[] branches => branches.Select(b => instructions[b]).ToArray(),
                    VariableDefinition variable => copy.Body.Variables[variable.Index],
                    ParameterDefinition parameter => copy.Parameters[parameter.Index],
                    MethodReference reference when reference.DeclaringType.FullName == source.FullName => methods[reference.Resolve()],
                    MethodReference reference => module.ImportReference(reference),
                    FieldReference reference => module.ImportReference(reference),
                    TypeReference reference => module.ImportReference(reference),
                    null => null,
                    var operand => operand
                };
            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
                copy.Body.ExceptionHandlers.Add(new(handler.HandlerType)
                {
                    TryStart = instructions[handler.TryStart], TryEnd = Map(handler.TryEnd),
                    HandlerStart = instructions[handler.HandlerStart], HandlerEnd = Map(handler.HandlerEnd),
                    FilterStart = Map(handler.FilterStart),
                    CatchType = handler.CatchType is null ? null : module.ImportReference(handler.CatchType)
                });
            Instruction? Map(Instruction? instruction) => instruction is null ? null : instructions[instruction];
        }
        return target;
    }
}
