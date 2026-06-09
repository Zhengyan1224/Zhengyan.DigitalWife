using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public readonly record struct ShadowMapBinding(
    uint TextureId,
    Matrix4x4 LightViewProjection,
    float NearDistance,
    float FarDistance,
    float Strength,
    float Bias);
