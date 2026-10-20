using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidPrecompiledScriptCompatibility
{
    internal static bool ReferencesPrivateInlineArray(byte[] image)
    {
        using MemoryStream stream = new(image, writable: false);
        using PEReader pe = new(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            TypeReference type = metadata.GetTypeReference(handle);
            if (metadata.GetString(type.Namespace) != "System.Runtime.CompilerServices"
                || !metadata.GetString(type.Name).StartsWith("InlineArray", StringComparison.Ordinal)
                || type.ResolutionScope.Kind != HandleKind.AssemblyReference) continue;
            AssemblyReference assembly = metadata.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
            if (metadata.GetString(assembly.Name) == "System.Private.CoreLib") return true;
        }
        return false;
    }
}
