using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal sealed class MaterialTextures
{
    public ITexture2D? Texture { get; set; }

    public ITexture2D? SphereTexture { get; set; }

    public ITexture2D? ToonTexture { get; set; }

    public PmxMaterialDescriptorSet? DescriptorSet { get; set; }
}

