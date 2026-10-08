using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;

internal static class AndroidVeldridPatchVerification
{
    internal static void Check(string path)
    {
        using (ModuleDefinition module = ModuleDefinition.ReadModule(path))
        {
            if (module.GetType(AndroidVeldridPatch.MarkerName) is null)
                throw new Exception($"Android Vulkan startup patch is missing from {path}");
            MethodDefinition method = module.GetType("Veldrid.Vk.VkGraphicsDevice").Methods.Single(m => m.Name == "CreateLogicalDevice");
            if (!method.Body.ExceptionHandlers.Any(handler => handler.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally) ||
                !method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call && call.DeclaringType.FullName == typeof(System.Runtime.InteropServices.GCHandle).FullName && call.Name == "Alloc"))
                throw new Exception("Extension names must remain pinned through device creation and be freed in finally.");
        }
        Console.WriteLine($"PASS Android Vulkan startup patch present: {path}");
    }

    internal static void Run(string path)
    {
        Check(path);
        string loaderPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "vk.dll");
        AndroidVkLoaderPatch.Check(loaderPath);
        var context = new AssemblyLoadContext("Android Vulkan patch verification", isCollectible: true);
        context.Resolving += (target, name) => name.Name == "vk"
            ? target.LoadFromAssemblyPath(loaderPath) : Assembly.Load(name);
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
            Type marker = assembly.GetType(AndroidVeldridPatch.MarkerName, throwOnError: true)!;
            MethodInfo select = marker.GetMethod("SelectCompositeAlpha", BindingFlags.Static | BindingFlags.NonPublic)!;
            Type flags = select.GetParameters()[0].ParameterType;
            foreach ((int supported, int expected) in new[] { (1, 1), (8, 8), (2, 2), (4, 4), (15, 1), (12, 8) })
                if (Convert.ToInt32(select.Invoke(null, [Enum.ToObject(flags, supported)])) != expected)
                    throw new Exception("Swapchain selected an unsupported alpha mode.");

            Type deviceType = assembly.GetType("Veldrid.GraphicsDevice", true)!;
            MethodInfo create = deviceType.GetMethods().Single(m => m.Name == "CreateVulkan" && m.GetParameters().Length == 1);
            Type options = create.GetParameters()[0].ParameterType;
            Type descriptionType = assembly.GetType("Veldrid.BufferDescription", true)!;
            Type usageType = assembly.GetType("Veldrid.BufferUsage", true)!;
            ConstructorInfo descriptionCtor = descriptionType.GetConstructor([typeof(uint), usageType])!;
            using CancellationTokenSource stop = new();
            Task collector = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    Thread.Sleep(1);
                }
            });
            try
            {
                for (int i = 0; i < 12; ++i)
                {
                    using IDisposable device = (IDisposable)create.Invoke(null, [Activator.CreateInstance(options)])!;
                    object factory = deviceType.GetProperty("ResourceFactory")!.GetValue(device)!;
                    MethodInfo createBuffer = factory.GetType().GetMethods().Single(m => m.Name == "CreateBuffer" && m.GetParameters() is [{ ParameterType: var type }] && type == descriptionType);
                    object description = descriptionCtor.Invoke([(uint)256, Enum.Parse(usageType, "UniformBuffer, Dynamic")]);
                    using IDisposable buffer = (IDisposable)createBuffer.Invoke(factory, [description])!;
                }
            }
            finally { stop.Cancel(); collector.GetAwaiter().GetResult(); }
            Console.WriteLine("PASS patched Vulkan: 12 device creations/resource allocations under compacting GC; supported swapchain alpha modes.");
        }
        finally { context.Unload(); }
    }
}
