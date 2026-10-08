using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class AndroidVkLoaderPatch
{
    internal static void Apply(string input, string output)
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(input, new ReaderParameters { InMemory = true });
        if (module.GetType("NativeLibraryLoader.Libdl") is { } sharedLibdl)
        {
            PatchSharedLoader(module, sharedLibdl);
            Write(module, output);
            return;
        }
        TypeDefinition libdl = module.GetType("Vulkan.Libdl")
            ?? throw new InvalidOperationException("Unsupported Vk binding: Vulkan.Libdl is missing.");
        MethodDefinition dlerror = libdl.Methods.Single(method => method.Name == "dlerror");
        if (!dlerror.IsPInvokeImpl || dlerror.ReturnType.MetadataType != MetadataType.String || dlerror.Parameters.Count != 0)
            throw new InvalidOperationException("Expected the original Vk 1.0.25 string-returning dlerror import.");

        // dlerror returns a borrowed, thread-local char*. Declaring the return
        // value as string tells the interop marshaller to free it after copying.
        // Android aborts in free() even if the caller discards the string.
        // The Vk loader only clears the pending error; it never reads the text.
        int callers = 0;
        foreach (TypeDefinition type in AllTypes(module.Types))
        foreach (MethodDefinition method in type.Methods.Where(method => method.HasBody))
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (instruction.Operand is not MethodReference target || target.FullName != dlerror.FullName) continue;
            if (instruction.OpCode != OpCodes.Call || instruction.Next?.OpCode != OpCodes.Pop)
                throw new InvalidOperationException("Unexpected dlerror consumer; cannot change its return contract safely.");
            instruction.Operand = dlerror;
            callers++;
        }
        if (callers != 1) throw new InvalidOperationException($"Expected one error-clearing call, found {callers}.");
        dlerror.ReturnType = module.TypeSystem.IntPtr;
        dlerror.MethodReturnType.MarshalInfo = null;

        ModuleReference library = new("libdl.so");
        module.ModuleReferences.Add(library);
        foreach (MethodDefinition import in libdl.Methods.Where(method => method.IsPInvokeImpl))
        {
            import.PInvokeInfo.Module = library;
            import.PInvokeInfo.Attributes = (import.PInvokeInfo.Attributes & ~PInvokeAttributes.CallConvMask) | PInvokeAttributes.CallConvCdecl;
        }
        Write(module, output);
    }

    private static void PatchSharedLoader(ModuleDefinition module, TypeDefinition libdl)
    {
        // Veldrid.SPIRV's NativeLibraryLoader has the same defect in both of
        // its Unix library variants. Preserve its managed string API, but copy
        // the borrowed bytes explicitly instead of using string-return P/Invoke.
        TypeDefinition[] variants = libdl.NestedTypes.Where(type => type.Name is "Libdl1" or "Libdl2").ToArray();
        if (variants.Length != 2) throw new InvalidOperationException("Unsupported NativeLibraryLoader variants.");
        ModuleReference library = new("libdl.so");
        module.ModuleReferences.Add(library);
        foreach (TypeDefinition variant in variants)
        {
            MethodDefinition error = variant.Methods.Single(method => method.Name == "dlerror");
            if (!error.IsPInvokeImpl || error.ReturnType.MetadataType != MetadataType.String || error.Parameters.Count != 0)
                throw new InvalidOperationException("Expected the original NativeLibraryLoader dlerror import.");
            foreach (MethodDefinition import in variant.Methods.Where(method => method.IsPInvokeImpl))
                import.PInvokeInfo.Module = library;
            MethodDefinition pointer = new("dlerror_pointer", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.PInvokeImpl, module.TypeSystem.IntPtr)
            {
                PInvokeInfo = new(PInvokeAttributes.CallConvCdecl, "dlerror", library),
                IsPreserveSig = true
            };
            variant.Methods.Add(pointer);
            error.PInvokeInfo = null;
            error.IsPInvokeImpl = false;
            error.ImplAttributes = Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed;
            error.Body = new Mono.Cecil.Cil.MethodBody(error);
            ILProcessor il = error.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Call, pointer));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(typeof(Marshal).GetMethod(nameof(Marshal.PtrToStringAnsi), [typeof(nint)])!)));
            il.Append(il.Create(OpCodes.Ret));
        }
    }

    private static void Write(ModuleDefinition module, string output)
    {
        // Distinguish this image from the NuGet binary in metadata/JNI caches.
        module.Mvid = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(module.Mvid + ":AndroidBorrowedDlerrorV1")).AsSpan(0, 16));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        module.Write(output);
        Check(output);
    }

    internal static void Check(string path)
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(path);
        MethodDefinition[] imports = AllTypes(module.Types).SelectMany(type => type.Methods)
            .Where(method => method.IsPInvokeImpl && method.PInvokeInfo.EntryPoint == "dlerror").ToArray();
        int expected = module.GetType("Vulkan.Libdl") is not null ? 1 : module.GetType("NativeLibraryLoader.Libdl") is not null ? 2 : 0;
        if (expected == 0 || imports.Length != expected || imports.Any(method =>
            method.ReturnType.MetadataType != MetadataType.IntPtr || method.MethodReturnType.HasMarshalInfo ||
            method.PInvokeInfo.Module.Name != "libdl.so"))
            throw new InvalidOperationException($"Safe borrowed-pointer dlerror imports are missing from {path}");
        Console.WriteLine($"PASS Android Vulkan loader: dlerror returns a borrowed IntPtr; {path}");
    }

    internal static void Verify(string path)
    {
        Check(path);
        string directory = Path.Combine(Path.GetTempPath(), "vk-borrowed-pointer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var context = new AssemblyLoadContext("Vk borrowed native string test", isCollectible: true);
        try
        {
            string copy = Path.Combine(directory, "vk.dll");
            using (ModuleDefinition module = ModuleDefinition.ReadModule(path))
            {
                if (OperatingSystem.IsWindows())
                {
                    // GetCommandLineA has the same no-argument char* ABI and
                    // returns OS-owned nonempty text. This exercises the actual
                    // patched P/Invoke return marshalling on Windows as well.
                    ModuleReference library = new("kernel32.dll");
                    module.ModuleReferences.Add(library);
                    foreach (MethodDefinition import in AllTypes(module.Types).SelectMany(type => type.Methods)
                        .Where(method => method.IsPInvokeImpl && method.PInvokeInfo.EntryPoint == "dlerror"))
                        import.PInvokeInfo = new(PInvokeAttributes.CallConvWinapi, "GetCommandLineA", library);
                }
                module.Write(copy);
            }
            using FileStream image = File.OpenRead(copy);
            Assembly assembly = context.LoadFromStream(image);
            Type libdl = assembly.GetType("Vulkan.Libdl") ?? assembly.GetType("NativeLibraryLoader.Libdl", true)!;
            MethodInfo error = libdl.GetMethod("dlerror", BindingFlags.Static | BindingFlags.Public)!;
            static string? ReadText(object? result) => result is nint pointer ? Marshal.PtrToStringAnsi(pointer) : (string?)result;
            if (OperatingSystem.IsWindows())
            {
                MethodInfo pointerMethod = error.ReturnType == typeof(nint) ? error
                    : libdl.GetNestedType("Libdl1", BindingFlags.NonPublic)!.GetMethod("dlerror_pointer", BindingFlags.NonPublic | BindingFlags.Static)!;
                nint borrowed = (nint)pointerMethod.Invoke(null, null)!;
                string text = Marshal.PtrToStringAnsi(borrowed) ?? throw new Exception("Expected nonempty borrowed text.");
                if (text.Length == 0) throw new Exception("Expected nonempty borrowed text.");
                for (int i = 0; i < 1000; i++)
                {
                    // Exercise both of NativeLibraryLoader's Unix variants.
                    libdl.GetField("m_useLibdl1", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, (i & 1) == 0);
                    if (ReadText(error.Invoke(null, null)) != text || Marshal.PtrToStringAnsi(borrowed) != text)
                        throw new Exception("Borrowed native memory was changed or released.");
                }
            }
            else
            {
                MethodInfo open = libdl.GetMethod("dlopen", BindingFlags.Static | BindingFlags.Public)!;
                for (int i = 0; i < 1000; i++)
                {
                    _ = error.Invoke(null, null);
                    nint handle = (nint)open.Invoke(null, ["/missing/zhengyan-vulkan-loader-test.so", 2])!;
                    if (handle != 0) throw new Exception("Expected a missing library.");
                    if (string.IsNullOrEmpty(ReadText(error.Invoke(null, null)))) throw new Exception("Expected dlerror text.");
                    if (ReadText(error.Invoke(null, null)) is not null) throw new Exception("dlerror did not clear the error.");
                }
            }
            Console.WriteLine("PASS 1000 borrowed native error-string returns without freeing native-owned memory.");
        }
        finally
        {
            context.Unload();
            File.Delete(Path.Combine(directory, "vk.dll"));
            Directory.Delete(directory);
        }
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition nested in AllTypes(type.NestedTypes)) yield return nested;
        }
    }
}
