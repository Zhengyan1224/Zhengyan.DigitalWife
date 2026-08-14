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
    public float NormalOffset { get; init; }
}

internal static class ShadowDepthBias
{
    // System.Numerics projection matrices produce zero-to-one depth. OpenGL
    // maps that NDC interval into the upper half of its normalized depth range.
    public static float ToOpenGlComparisonSpace(float bias)
        => Math.Max(bias, 0.0f) * 0.5f;
}
