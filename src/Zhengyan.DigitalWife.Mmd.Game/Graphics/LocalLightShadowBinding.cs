using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public static class LocalLightShadowLimits
{
    public const int MaxShadowedPointLights = 2;
    public const int PointFacesPerLight = 6;
    public const int MaxPointShadowFaces = MaxShadowedPointLights * PointFacesPerLight;
    public const int MaxShadowedSpotLights = 4;
}

public sealed record PointLightShadowBinding(
    int PackedLightIndex,
    IReadOnlyList<Matrix4x4> FaceViewProjections,
    IReadOnlyList<Vector4> AtlasRects);

public sealed record SpotLightShadowBinding(
    int PackedLightIndex,
    Matrix4x4 LightViewProjection,
    Vector4 AtlasRect);

public sealed class LocalLightShadowBinding
{
    public required RuntimeTextureHandle Texture { get; init; }
    public object? NativeSampler { get; init; }
    public required IReadOnlyList<PointLightShadowBinding> PointLights { get; init; }
    public required IReadOnlyList<SpotLightShadowBinding> SpotLights { get; init; }
    public float Strength { get; init; } = 0.7f;
    public float Bias { get; init; } = 0.0018f;
    public Vector2 TexelSize { get; init; }

    public uint TextureId => Texture.LegacyTextureId;
    public object? NativeTexture => Texture.NativeResource;
}
