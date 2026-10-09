using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class VeldridMemorySafetyPatch
{
    private const string Marker = "Veldrid.Vk.ZhengyanVulkanMemorySafetyPatch";

    public static bool Apply(ModuleDefinition module)
    {
        if (module.GetType(Marker) is not null) return false;
        TypeDefinition memoryManager = module.GetType("Veldrid.Vk.VkDeviceMemoryManager");
        TypeDefinition chunk = memoryManager.NestedTypes.Single(t => t.Name == "ChunkAllocator");
        MethodDefinition constructor = chunk.Methods.Single(m => m.IsConstructor && !m.IsStatic);
        TypeDefinition marker = new("Veldrid.Vk", "ZhengyanVulkanMemorySafetyPatch",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(marker);
        MethodDefinition check = new("RequireSuccess", MethodAttributes.Assembly | MethodAttributes.Static, module.TypeSystem.Void);
        check.Parameters.Add(new("result", ParameterAttributes.None, module.TypeSystem.Int32));
        marker.Methods.Add(check);
        ILProcessor checkIl = check.Body.GetILProcessor();
        Instruction ret = checkIl.Create(OpCodes.Ret);
        checkIl.Append(checkIl.Create(OpCodes.Ldarg_0));
        checkIl.Append(checkIl.Create(OpCodes.Brfalse, ret));
        checkIl.Append(checkIl.Create(OpCodes.Ldstr, "Vulkan memory allocation/mapping failed; VkResult="));
        checkIl.Append(checkIl.Create(OpCodes.Ldarg_0));
        checkIl.Append(checkIl.Create(OpCodes.Box, module.TypeSystem.Int32));
        checkIl.Append(checkIl.Create(OpCodes.Call, module.ImportReference(typeof(string).GetMethod(nameof(string.Concat), [typeof(object), typeof(object)])!)));
        checkIl.Append(checkIl.Create(OpCodes.Newobj, module.ImportReference(typeof(InvalidOperationException).GetConstructor([typeof(string)])!)));
        checkIl.Append(checkIl.Create(OpCodes.Throw));
        checkIl.Append(ret);

        ILProcessor il = constructor.Body.GetILProcessor();
        foreach (Instruction call in constructor.Body.Instructions.Where(i => i.Operand is MethodReference m &&
            m.DeclaringType.FullName == "Vulkan.VulkanNative" && m.Name is "vkAllocateMemory" or "vkMapMemory").ToArray())
        {
            Instruction pop = call.Next;
            if (pop.OpCode != OpCodes.Pop) throw new InvalidOperationException("Unexpected Vulkan allocation result handling.");
            pop.OpCode = OpCodes.Call;
            pop.Operand = check;
            if (((MethodReference)call.Operand).Name == "vkMapMemory")
            {
                // Mapping failure still owns the successful allocation. Free it
                // before throwing; never publish an allocator with a null pointer.
                MethodReference free = memoryManager.Methods.Single(m => m.Name == "Free").Body.Instructions
                    .Select(i => i.Operand).OfType<MethodReference>().First(m => m.Name == "vkFreeMemory");
                il.InsertBefore(pop, il.Create(OpCodes.Dup));
                il.InsertBefore(pop, il.Create(OpCodes.Brfalse, pop));
                il.InsertBefore(pop, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(pop, il.Create(OpCodes.Ldfld, chunk.Fields.Single(f => f.Name == "_device")));
                il.InsertBefore(pop, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(pop, il.Create(OpCodes.Ldfld, chunk.Fields.Single(f => f.Name == "_memory")));
                il.InsertBefore(pop, il.Create(OpCodes.Ldc_I4_0));
                il.InsertBefore(pop, il.Create(OpCodes.Conv_U));
                il.InsertBefore(pop, il.Create(OpCodes.Call, free));
            }
        }
        Check(module);
        return true;
    }

    public static void Check(ModuleDefinition module)
    {
        if (module.GetType(Marker) is null) throw new InvalidOperationException("Vulkan memory result checks are missing.");
        MethodDefinition constructor = module.GetType("Veldrid.Vk.VkDeviceMemoryManager").NestedTypes
            .Single(t => t.Name == "ChunkAllocator").Methods.Single(m => m.IsConstructor && !m.IsStatic);
        if (constructor.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.FullName == Marker && m.Name == "RequireSuccess") != 2)
            throw new InvalidOperationException("Both Vulkan allocation and mapping must check VkResult.");
    }

    public static void VerifyFailures(string path)
    {
        foreach (bool allocationFailure in new[] { true, false })
        {
            using ModuleDefinition module = ModuleDefinition.ReadModule(path);
            Check(module);
            TypeDefinition chunk = module.GetType("Veldrid.Vk.VkDeviceMemoryManager").NestedTypes.Single(t => t.Name == "ChunkAllocator");
            MethodDefinition constructor = chunk.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            TypeDefinition marker = module.GetType(Marker);
            FieldDefinition mapped = new("MapCalled", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32);
            FieldDefinition freed = new("FreeCalled", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32);
            marker.Fields.Add(mapped);
            marker.Fields.Add(freed);
            ILProcessor il = constructor.Body.GetILProcessor();
            foreach (Instruction call in constructor.Body.Instructions.Where(i => i.Operand is MethodReference m &&
                m.DeclaringType.FullName == "Vulkan.VulkanNative" && m.Name is "vkAllocateMemory" or "vkMapMemory" or "vkFreeMemory").ToArray())
            {
                MethodReference native = (MethodReference)call.Operand!;
                Instruction next = call.Next;
                call.OpCode = OpCodes.Pop;
                call.Operand = null;
                for (int i = 1; i < native.Parameters.Count; i++) il.InsertBefore(next, il.Create(OpCodes.Pop));
                if (native.Name != "vkAllocateMemory")
                {
                    il.InsertBefore(next, il.Create(OpCodes.Ldc_I4_1));
                    il.InsertBefore(next, il.Create(OpCodes.Stsfld, native.Name == "vkMapMemory" ? mapped : freed));
                }
                if (native.Name != "vkFreeMemory")
                    il.InsertBefore(next, il.Create(OpCodes.Ldc_I4, native.Name == "vkMapMemory" ? -5 : allocationFailure ? -2 : 0));
            }
            using MemoryStream stream = new();
            module.Write(stream);
            stream.Position = 0;
            var context = new System.Runtime.Loader.AssemblyLoadContext("Vulkan allocation failure injection", true);
            context.Resolving += (_, name) => System.Reflection.Assembly.Load(name);
            try
            {
                System.Reflection.Assembly assembly = context.LoadFromStream(stream);
                Type type = assembly.GetType("Veldrid.Vk.VkDeviceMemoryManager+ChunkAllocator", true)!;
                var ctor = type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Single();
                try
                {
                    ctor.Invoke([Activator.CreateInstance(ctor.GetParameters()[0].ParameterType), (uint)0, true]);
                    throw new Exception("An unsuccessful allocation must not construct an allocator.");
                }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException failure)
                {
                    if (!failure.Message.Contains(allocationFailure ? "VkResult=-2" : "VkResult=-5")) throw;
                }
                Type markerType = assembly.GetType(Marker, true)!;
                int mapCalls = (int)markerType.GetField(mapped.Name)!.GetValue(null)!;
                int freeCalls = (int)markerType.GetField(freed.Name)!.GetValue(null)!;
                if (mapCalls != (allocationFailure ? 0 : 1) || freeCalls != (allocationFailure ? 0 : 1))
                    throw new Exception("Failed allocations must not be mapped; failed maps must release their allocation.");
            }
            finally { context.Unload(); }
        }
        Console.WriteLine("PASS Vulkan injected allocation/map failures: no invalid map, allocation released after map failure.");
    }
}
