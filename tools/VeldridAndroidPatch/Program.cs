using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Runtime.InteropServices;

try
{
    if (args is ["--check", string linkedAssemblyPath])
    {
        AndroidVeldridPatchVerification.Check(linkedAssemblyPath);
        return 0;
    }
    if (args is ["--verify", string assemblyPath])
    {
        AndroidVeldridPatchVerification.Run(assemblyPath);
        return 0;
    }
    if (args.Length != 2) throw new ArgumentException("Usage: VeldridAndroidPatch <original Veldrid.dll> <output Veldrid.dll>");
    AndroidVeldridPatch.Apply(args[0], args[1]);
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }

public static class AndroidVeldridPatch
{
    public const string MarkerName = "Veldrid.Vk.ZhengyanAndroidStartupPatch";

    public static void Apply(string input, string output)
    {
        using DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input))!);
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        using ModuleDefinition module = ModuleDefinition.ReadModule(input, new ReaderParameters
            { InMemory = true, AssemblyResolver = resolver });
        if (module.GetType(MarkerName) is not null) throw new InvalidOperationException("Expected an unpatched Veldrid assembly.");
        TypeDefinition device = module.GetType("Veldrid.Vk.VkGraphicsDevice");
        MethodDefinition createDevice = device.Methods.Single(m => m.Name == "CreateLogicalDevice");
        foreach (TypeDefinition type in module.Types)
            foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody)) method.Body.SimplifyMacros();

        // Veldrid saves pointers into the managed VkExtensionProperties[] but
        // ends its fixed block before vkCreateDevice. Mono's moving GC can then
        // invalidate ppEnabledExtensionNames during the intervening allocations.
        PinExtensionProperties(module, createDevice);

        // Veldrid creates a Vulkan 1.0 instance and explicitly enables these KHR
        // extensions. Use their enabled entry points instead of trying promoted
        // core entry points first (a non-null loader trampoline is insufficient).
        foreach (string name in new[] { "vkGetPhysicalDeviceProperties2", "vkGetBufferMemoryRequirements2", "vkGetImageMemoryRequirements2" })
        {
            Instruction instruction = device.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                .Single(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, name));
            instruction.Operand = name + "KHR";
        }

        TypeDefinition marker = new("Veldrid.Vk", "ZhengyanAndroidStartupPatch",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(marker);
        MethodDefinition log = CreateLogger(module, marker);
        AddStartupLogs(module, log);
        PatchCompositeAlpha(module, marker);

        foreach (TypeDefinition type in module.Types)
            foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody)) method.Body.OptimizeMacros();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        module.Write(output);
        Console.WriteLine($"Patched Android Vulkan startup: {output}");
    }

    private static void PinExtensionProperties(ModuleDefinition module, MethodDefinition method)
    {
        ILProcessor il = method.Body.GetILProcessor();
        Instruction call = method.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "GetDeviceExtensionProperties");
        Instruction store = call.Next;
        if (store.OpCode != OpCodes.Stloc || store.Operand is not VariableDefinition properties)
            throw new InvalidOperationException("Unexpected Veldrid extension property assignment.");
        VariableDefinition pin = new(module.ImportReference(typeof(GCHandle)));
        method.Body.Variables.Add(pin);
        Instruction tryStart = store.Next;
        il.InsertBefore(tryStart, il.Create(OpCodes.Ldloc, properties));
        il.InsertBefore(tryStart, il.Create(OpCodes.Ldc_I4, (int)GCHandleType.Pinned));
        il.InsertBefore(tryStart, il.Create(OpCodes.Call, module.ImportReference(typeof(GCHandle).GetMethod(nameof(GCHandle.Alloc), [typeof(object), typeof(GCHandleType)])!)));
        il.InsertBefore(tryStart, il.Create(OpCodes.Stloc, pin));
        Instruction returnLabel = il.Create(OpCodes.Ret);
        foreach (Instruction ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
        {
            ret.OpCode = OpCodes.Leave;
            ret.Operand = returnLabel;
        }
        Instruction finallyStart = il.Create(OpCodes.Ldloca, pin);
        il.Append(finallyStart);
        il.Append(il.Create(OpCodes.Call, module.ImportReference(typeof(GCHandle).GetMethod(nameof(GCHandle.Free))!)));
        il.Append(il.Create(OpCodes.Endfinally));
        il.Append(returnLabel);
        method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = tryStart, TryEnd = finallyStart,
            HandlerStart = finallyStart, HandlerEnd = returnLabel
        });
    }

    private static MethodDefinition CreateLogger(ModuleDefinition module, TypeDefinition marker)
    {
        ModuleReference library = new("liblog.so");
        module.ModuleReferences.Add(library);
        MethodDefinition native = new("WriteAndroidLog", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PInvokeImpl,
            module.TypeSystem.Int32)
        {
            PInvokeInfo = new(PInvokeAttributes.CallConvCdecl | PInvokeAttributes.CharSetAnsi, "__android_log_write", library),
            IsPreserveSig = true
        };
        native.Parameters.Add(new("priority", ParameterAttributes.None, module.TypeSystem.Int32));
        native.Parameters.Add(new("tag", ParameterAttributes.None, module.TypeSystem.String));
        native.Parameters.Add(new("text", ParameterAttributes.None, module.TypeSystem.String));
        marker.Methods.Add(native);
        MethodDefinition log = new("Log", MethodAttributes.Assembly | MethodAttributes.Static, module.TypeSystem.Void);
        log.Parameters.Add(new("message", ParameterAttributes.None, module.TypeSystem.String));
        marker.Methods.Add(log);
        ILProcessor il = log.Body.GetILProcessor();
        Instruction ret = il.Create(OpCodes.Ret);
        il.Append(il.Create(OpCodes.Call, module.ImportReference(typeof(OperatingSystem).GetMethod(nameof(OperatingSystem.IsAndroid))!)));
        il.Append(il.Create(OpCodes.Brfalse, ret));
        il.Append(il.Create(OpCodes.Ldc_I4_4));
        il.Append(il.Create(OpCodes.Ldstr, "ZhengyanGamePlayer"));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, native));
        il.Append(il.Create(OpCodes.Pop));
        il.Append(ret);
        return log;
    }

    private static void AddStartupLogs(ModuleDefinition module, MethodDefinition log)
    {
        foreach (string typeName in new[] { "VkGraphicsDevice", "VkSurfaceUtil", "VkSwapchain" })
        {
            TypeDefinition type = module.GetType("Veldrid.Vk." + typeName);
            foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody &&
                m.Name is "CreateInstance" or "CreatePhysicalDevice" or "CreateLogicalDevice" or "CreateAndroidSurface" or "CreateSwapchain"))
            {
                ILProcessor il = method.Body.GetILProcessor();
                Instruction first = method.Body.Instructions[0];
                il.InsertBefore(first, il.Create(OpCodes.Ldstr, $"Vulkan startup: {method.Name} begin"));
                il.InsertBefore(first, il.Create(OpCodes.Call, log));
                foreach (Instruction call in method.Body.Instructions.Where(i => i.Operand is MethodReference m &&
                    m.Name is "vkCreateInstance" or "ANativeWindow_fromSurface" or "vkCreateAndroidSurfaceKHR" or
                    "vkCreateDevice" or "vkGetDeviceQueue" or "vkCreateSwapchainKHR" or "vkGetPhysicalDeviceProperties" or "vkGetPhysicalDeviceMemoryProperties").ToArray())
                {
                    string name = ((MethodReference)call.Operand).Name;
                    il.InsertBefore(call, il.Create(OpCodes.Ldstr, $"Vulkan startup: {name} begin"));
                    il.InsertBefore(call, il.Create(OpCodes.Call, log));
                    Instruction next = call.Next;
                    il.InsertBefore(next, il.Create(OpCodes.Ldstr, $"Vulkan startup: {name} returned"));
                    il.InsertBefore(next, il.Create(OpCodes.Call, log));
                }
            }
        }
    }

    private static void PatchCompositeAlpha(ModuleDefinition module, TypeDefinition marker)
    {
        MethodDefinition createSwapchain = module.GetType("Veldrid.Vk.VkSwapchain").Methods.Single(m => m.Name == "CreateSwapchain");
        Instruction store = createSwapchain.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "compositeAlpha");
        FieldReference alpha = (FieldReference)store.Operand;
        VariableDefinition caps = createSwapchain.Body.Variables.Single(v => v.VariableType.FullName == "Vulkan.VkSurfaceCapabilitiesKHR");
        MethodDefinition select = new("SelectCompositeAlpha", MethodAttributes.Assembly | MethodAttributes.Static, alpha.FieldType);
        select.Parameters.Add(new("supported", ParameterAttributes.None, alpha.FieldType));
        marker.Methods.Add(select);
        ILProcessor selector = select.Body.GetILProcessor();
        foreach (int flag in new[] { 1, 8, 2, 4 })
        {
            Instruction next = selector.Create(OpCodes.Nop);
            selector.Append(selector.Create(OpCodes.Ldarg_0));
            selector.Append(selector.Create(OpCodes.Ldc_I4, flag));
            selector.Append(selector.Create(OpCodes.And));
            selector.Append(selector.Create(OpCodes.Brfalse, next));
            selector.Append(selector.Create(OpCodes.Ldc_I4, flag));
            selector.Append(selector.Create(OpCodes.Ret));
            selector.Append(next);
        }
        selector.Append(selector.Create(OpCodes.Ldstr, "Vulkan surface has no supported composite alpha mode."));
        selector.Append(selector.Create(OpCodes.Newobj, module.ImportReference(typeof(NotSupportedException).GetConstructor([typeof(string)])!)));
        selector.Append(selector.Create(OpCodes.Throw));
        ILProcessor il = createSwapchain.Body.GetILProcessor();
        if (store.Previous.OpCode != OpCodes.Ldc_I4 || (int)store.Previous.Operand != 1)
            throw new InvalidOperationException("Unexpected Veldrid composite alpha assignment.");
        il.Replace(store.Previous, il.Create(OpCodes.Ldloca, caps));
        il.InsertBefore(store, il.Create(OpCodes.Ldfld, new FieldReference("supportedCompositeAlpha", alpha.FieldType, caps.VariableType)));
        il.InsertBefore(store, il.Create(OpCodes.Call, select));
    }
}
