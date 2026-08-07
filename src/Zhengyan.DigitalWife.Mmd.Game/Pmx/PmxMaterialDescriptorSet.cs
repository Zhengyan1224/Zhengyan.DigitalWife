using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal readonly record struct PmxTextureDescriptor(
    uint Binding,
    string Name,
    ITexture2D Texture,
    IGpuSampler Sampler);

/// <summary>
/// Backend-neutral PMX texture descriptor table. The Vulkan shader migration
/// turns these bindings into Veldrid ResourceSets; OpenGL maps them to units 0-2.
/// </summary>
internal sealed class PmxMaterialDescriptorSet
{
    public PmxMaterialDescriptorSet(
        ITexture2D baseTexture,
        ITexture2D sphereTexture,
        ITexture2D toonTexture,
        IGpuSampler sampler,
        IGpuSampler toonSampler)
    {
        Bindings =
        [
            new PmxTextureDescriptor(0, "BaseTexture", baseTexture, sampler),
            new PmxTextureDescriptor(1, "SphereTexture", sphereTexture, sampler),
            new PmxTextureDescriptor(2, "ToonTexture", toonTexture, toonSampler)
        ];
    }

    public IReadOnlyList<PmxTextureDescriptor> Bindings { get; }
}
