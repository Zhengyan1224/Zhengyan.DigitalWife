using Mono.Cecil;

/// <summary>Preserve color/depth contents across interrupted and resumed passes.</summary>
internal static class AndroidVulkanRenderPassPatch
{
    internal static void Apply(ModuleDefinition module)
    {
        VeldridRenderPassSynchronizationPatch.Apply(module);
    }

    internal static void Check(ModuleDefinition module)
    {
        VeldridRenderPassSynchronizationPatch.Check(module);
    }
}
