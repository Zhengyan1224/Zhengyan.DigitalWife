using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public readonly record struct PointLightData(
    Vector3 Position,
    Vector3 Color,
    float Intensity,
    float Range,
    bool Enabled = true,
    bool CastShadows = false);

internal static class PointLightPacking
{
    public const int MaxLights = 16;

    public static bool IsValid(PointLightData light)
    {
        return light.Enabled
            && IsFinite(light.Position)
            && IsFinite(light.Color)
            && float.IsFinite(light.Intensity)
            && float.IsFinite(light.Range)
            && light.Intensity > 0.0f
            && light.Range > 0.0f;
    }

    public static int PackViewSpace(
        IReadOnlyList<PointLightData>? lights,
        Matrix4x4 view,
        Span<Vector4> positionRanges,
        Span<Vector4> colorIntensities)
    {
        int capacity = Math.Min(MaxLights, Math.Min(positionRanges.Length, colorIntensities.Length));
        int count = 0;
        if (lights is null)
        {
            return count;
        }

        foreach (PointLightData light in lights)
        {
            if (count >= capacity)
            {
                break;
            }

            if (!IsValid(light))
            {
                continue;
            }

            Vector3 viewPosition = Vector3.Transform(light.Position, view);
            positionRanges[count] = new Vector4(viewPosition, MathF.Max(light.Range, 0.001f));
            colorIntensities[count] = new Vector4(Vector3.Max(light.Color, Vector3.Zero), MathF.Max(light.Intensity, 0.0f));
            count++;
        }

        return count;
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
