using System.Reflection;
using System.Runtime.InteropServices;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal static class LinuxVulkanLoader
{
    static LinuxVulkanLoader()
    {
        if (OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid())
        {
            // The vk binding imports "libdl", while current glibc distributions
            // provide the runtime library as libdl.so.2 without a libdl.so alias.
            NativeLibrary.SetDllImportResolver(typeof(global::Vulkan.VulkanNative).Assembly, ResolveLibrary);
        }
    }

    internal static void EnsureInitialized()
    {
        // The static constructor installs the resolver before vk's first P/Invoke.
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == "libdl"
            && NativeLibrary.TryLoad("libdl.so.2", assembly, searchPath, out nint handle))
        {
            return handle;
        }

        return nint.Zero;
    }
}
