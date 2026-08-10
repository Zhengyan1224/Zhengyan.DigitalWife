using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public readonly record struct ShadowMapBinding(
    RuntimeTextureHandle Texture,
    Matrix4x4 LightViewProjection,
    float NearDistance,
    float FarDistance,
    float Strength,
    float Bias)
{
    public uint TextureId => Texture.LegacyTextureId;
    public object? NativeTexture => Texture.NativeResource;
    public object? NativeSampler { get; init; }
    public Vector2 TexelSize { get; init; }
}
