using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Vulkan;

/// <summary>Count actual Vulkan pass commands and read back preserved clears.</summary>
public static unsafe class VulkanEmptyPassVerification
{
    private static int _begins, _ends;

    internal static void Run(string path)
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(path);
        VeldridEmptyPassPatch.Check(module);
        TypeDefinition commandsType = module.GetType("Veldrid.Vk.VkCommandList");
        foreach (MethodDefinition method in commandsType.Methods.Where(m => m.HasBody))
        foreach (var instruction in method.Body.Instructions)
            if (instruction.Operand is MethodReference native && native.DeclaringType.FullName == "Vulkan.VulkanNative"
                && native.Name is "vkCmdBeginRenderPass" or "vkCmdEndRenderPass")
                instruction.Operand = module.ImportReference(typeof(VulkanEmptyPassVerification).GetMethod(native.Name)!);
        using MemoryStream stream = new();
        module.Write(stream);
        stream.Position = 0;
        var context = new AssemblyLoadContext("Vulkan empty pass verification", true);
        context.Resolving += (_, name) => Assembly.Load(name);
        try
        {
            Assembly assembly = context.LoadFromStream(stream);
            Type deviceType = assembly.GetType("Veldrid.GraphicsDevice", true)!;
            MethodInfo createDevice = deviceType.GetMethods().Single(m => m.Name == "CreateVulkan" && m.GetParameters().Length == 1);
            using IDisposable device = (IDisposable)createDevice.Invoke(null, [Activator.CreateInstance(createDevice.GetParameters()[0].ParameterType)])!;
            object factory = Read(device, "ResourceFactory");
            List<IDisposable> resources = [];
            try
            {
                object a = Texture("R8_G8_B8_A8_UNorm", "RenderTarget, Sampled");
                object a2 = Texture("R8_G8_B8_A8_UNorm", "RenderTarget, Sampled");
                object b = Texture("R8_G8_B8_A8_UNorm", "RenderTarget, Sampled");
                object b2 = Texture("R8_G8_B8_A8_UNorm", "RenderTarget, Sampled");
                object depthA = Texture("D24_UNorm_S8_UInt", "DepthStencil, Sampled");
                object depthB = Texture("D24_UNorm_S8_UInt", "DepthStencil, Sampled");
                object framebufferA = Framebuffer(depthA, a, a2), framebufferB = Framebuffer(depthB, b, b2);
                object commands = Keep(Call(factory, "CreateCommandList"));
                object[] captures = Enumerable.Range(0, 4).Select(_ => Texture("R8_G8_B8_A8_UNorm", "Staging")).ToArray();
                object green = Color(0, 1, 0), red = Color(1, 0, 0), blue = Color(0, 0, 1);
                Call(commands, "Begin");
                Call(commands, "SetFramebuffer", framebufferA);
                Call(commands, "ClearColorTarget", 0u, green);
                Call(commands, "ClearColorTarget", 1u, red);
                Call(commands, "ClearDepthStencil", 1f, (byte)0);
                Call(commands, "SetFramebuffer", framebufferB);
                Call(commands, "ClearColorTarget", 0u, blue);
                Call(commands, "ClearColorTarget", 1u, red);
                Call(commands, "ClearDepthStencil", 1f, (byte)0);
                Submit();

                _begins = _ends = 0;
                Call(commands, "Begin");
                for (int i = 0; i < 16; i++)
                {
                    Call(commands, "SetFramebuffer", framebufferA);
                    Call(commands, "SetFramebuffer", framebufferB);
                }
                Check(_begins == 0 && _ends == 0, "Framebuffer switches recorded empty load/store passes.");
                Call(commands, "CopyTexture", a, captures[0]);
                Call(commands, "SetFramebuffer", framebufferA);
                // A clear in color attachment 1 must not be mistaken for an empty pass.
                Call(commands, "ClearColorTarget", 1u, blue);
                Call(commands, "SetFramebuffer", framebufferB);
                Check(_begins == 1 && _ends == 1, "A secondary color clear was skipped.");
                Call(commands, "CopyTexture", a2, captures[1]);
                Call(commands, "SetFramebuffer", framebufferA);
                Call(commands, "ClearDepthStencil", .375f, (byte)0);
                Call(commands, "SetFramebuffer", framebufferB);
                Check(_begins == 2 && _ends == 2, "A depth-only clear was skipped.");
                Call(commands, "CopyTexture", a, captures[2]);
                Call(commands, "SetFramebuffer", framebufferA);
                Call(commands, "ClearColorTarget", 0u, red);
                Call(commands, "ClearColorTarget", 1u, blue);
                Call(commands, "ClearDepthStencil", 1f, (byte)0);
                Call(commands, "SetFramebuffer", framebufferB);
                Check(_begins == 3 && _ends == 3, "A full clear was skipped.");
                Call(commands, "CopyTexture", a, captures[3]);
                Call(commands, "ClearColorTarget", 0u, blue);
                Call(commands, "ClearColorTarget", 1u, red);
                Call(commands, "ClearDepthStencil", 1f, (byte)0);
                Submit();
                Check(_begins == 4 && _ends == 4, "Final queued clears were not flushed by End().");
                foreach ((int capture, byte r, byte g, byte bValue) in new[] { (0, (byte)0, (byte)255, (byte)0), (1, (byte)0, (byte)0, (byte)255), (2, (byte)0, (byte)255, (byte)0), (3, (byte)255, (byte)0, (byte)0) })
                {
                    object pixels = Call(device, "Map", captures[capture], Enum.Parse(assembly.GetType("Veldrid.MapMode", true)!, "Read"), 0u);
                    try
                    {
                        byte* data = (byte*)(IntPtr)Read(pixels, "Data");
                        uint pitch = (uint)Read(pixels, "RowPitch");
                        for (int y = 0; y < 32; y++)
                        for (int x = 0; x < 32; x++)
                        {
                            byte* pixel = data + y * pitch + x * 4;
                            Check(pixel[0] == r && pixel[1] == g && pixel[2] == bValue,
                                "Empty switches or clear-only passes corrupted an attachment.");
                        }
                    }
                    finally { Call(device, "Unmap", captures[capture], 0u); }
                }
                Console.WriteLine("PASS native Vulkan: 32 empty framebuffer switches emitted zero passes; secondary color, depth-only, full and final clears preserved; 4096 pixels verified.");

                void Submit() { Call(commands, "End"); Call(device, "SubmitCommands", commands, null); Call(device, "WaitForIdle"); }
                object Keep(object resource) { resources.Add((IDisposable)resource); return resource; }
                object Color(float r, float g, float bValue) => Activator.CreateInstance(assembly.GetType("Veldrid.RgbaFloat", true)!, [r, g, bValue, 1f])!;
                object Texture(string format, string usage)
                {
                    Type descriptionType = assembly.GetType("Veldrid.TextureDescription", true)!;
                    object description = Activator.CreateInstance(descriptionType)!;
                    foreach ((string name, uint value) in new[] { ("Width", 32u), ("Height", 32u), ("Depth", 1u), ("MipLevels", 1u), ("ArrayLayers", 1u) })
                        descriptionType.GetField(name)!.SetValue(description, value);
                    foreach ((string name, string value) in new[] { ("Format", format), ("Usage", usage), ("Type", "Texture2D"), ("SampleCount", "Count1") })
                    {
                        FieldInfo field = descriptionType.GetField(name)!;
                        field.SetValue(description, Enum.Parse(field.FieldType, value));
                    }
                    return Keep(Call(factory, "CreateTexture", description));
                }
                object Framebuffer(object depth, params object[] colors)
                {
                    Type textureType = assembly.GetType("Veldrid.Texture", true)!;
                    Array array = Array.CreateInstance(textureType, colors.Length);
                    for (int i = 0; i < colors.Length; i++) array.SetValue(colors[i], i);
                    Type descriptionType = assembly.GetType("Veldrid.FramebufferDescription", true)!;
                    ConstructorInfo ctor = descriptionType.GetConstructors().Single(c => c.GetParameters() is [_, var p] && p.ParameterType == array.GetType());
                    return Keep(Call(factory, "CreateFramebuffer", ctor.Invoke([depth, array])));
                }
            }
            finally
            {
                Call(device, "WaitForIdle");
                for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
            }
        }
        finally { context.Unload(); }
    }

    private static object Read(object target, string name) => target.GetType().GetProperty(name)?.GetValue(target)
        ?? target.GetType().GetField(name)!.GetValue(target)!;

    private static object Call(object target, string name, params object?[] args)
    {
        MethodInfo method = target.GetType().GetMethods().Single(m => m.Name == name && !m.ContainsGenericParameters
            && m.GetParameters().Length == args.Length && m.GetParameters().Select((p, i) =>
                !p.ParameterType.IsByRef && (args[i] is null || p.ParameterType.IsInstanceOfType(args[i]))).All(v => v));
        return method.Invoke(target, args)!;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void vkCmdBeginRenderPass(VkCommandBuffer cb, ref VkRenderPassBeginInfo info, VkSubpassContents contents)
    { _begins++; VulkanNative.vkCmdBeginRenderPass(cb, ref info, contents); }
    public static void vkCmdEndRenderPass(VkCommandBuffer cb) { _ends++; VulkanNative.vkCmdEndRenderPass(cb); }
}
