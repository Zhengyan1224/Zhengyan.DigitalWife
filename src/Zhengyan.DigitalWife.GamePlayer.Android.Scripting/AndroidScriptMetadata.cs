using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidScriptMetadata
{
    private static readonly Lazy<MetadataReference[]> References = new(() => GetAssemblies().Select(CreateReference).ToArray());
    // Android loads assemblies lazily and has no framework directory to scan.
    // Explicitly root every default import, including imports absent from the
    // host's executed code path. A using directive alone does not load an assembly.
    internal static IEnumerable<Assembly> GetAssemblies()
    {
        Queue<Assembly> pending = new([
            typeof(object).Assembly, typeof(Console).Assembly, typeof(Task).Assembly,
            typeof(List<>).Assembly, typeof(System.Collections.Concurrent.ConcurrentDictionary<,>).Assembly,
            typeof(System.Net.IPAddress).Assembly, typeof(System.Net.Http.HttpClient).Assembly,
            typeof(System.Net.Sockets.Socket).Assembly, typeof(System.Text.Json.JsonSerializer).Assembly,
            typeof(System.Text.RegularExpressions.Regex).Assembly, typeof(System.Numerics.Vector3).Assembly,
            typeof(System.Linq.Enumerable).Assembly, typeof(System.Linq.Expressions.Expression).Assembly,
            typeof(System.Dynamic.DynamicObject).Assembly,
            typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly,
            typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly,
            typeof(AndroidScriptGlobals).Assembly, typeof(RuntimeScene).Assembly,
            typeof(GameProject).Assembly, typeof(PmxModelComponent).Assembly,
            Assembly.Load("System.Runtime"), Assembly.Load("netstandard")
        ]);
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out Assembly? assembly))
        {
            if (assembly.IsDynamic || !identities.Add(assembly.FullName!)) continue;
            yield return assembly;
            foreach (AssemblyName dependency in assembly.GetReferencedAssemblies())
            {
                if (identities.Contains(dependency.FullName)) continue;
                Assembly? loaded = null;
                try { loaded = Assembly.Load(dependency); }
                // Optional desktop rendering dependencies are not Android APIs.
                catch (FileNotFoundException) { }
                catch (FileLoadException) { }
                if (loaded is not null) pending.Enqueue(loaded);
            }
        }
    }

    internal static IEnumerable<MetadataReference> GetReferences()
        => References.Value;

    private static unsafe MetadataReference CreateReference(Assembly assembly)
    {
        // Assembly.Location on Android is often a synthetic APK path. Roslyn
        // needs a metadata directory here, not a file or a complete PE image.
        if (!assembly.TryGetRawMetadata(out byte* blob, out int length) || blob is null || length <= 0)
            throw new InvalidOperationException($"Assembly metadata is unavailable: {assembly.FullName}");
        ModuleMetadata module = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
        return AssemblyMetadata.Create(module).GetReference(
            aliases: assembly.GetName().Name == "Zhengyan.DigitalWife.Mmd" ? ["MmdDesktop"] : default);
    }
}
