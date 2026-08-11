using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public readonly record struct SpotLightData(
    Vector3 Position,
    Vector3 Direction,
    Vector3 Color,
    float Intensity,
    float Range,
    float InnerConeAngleDegrees,
    float OuterConeAngleDegrees,
    bool Enabled = true,
    bool CastShadows = false);

public static class SpotLightTransform
{
    public static Vector3 GetDirection(Quaternion rotation)
    {
        Vector3 direction = Vector3.Transform(-Vector3.UnitZ, Quaternion.Normalize(rotation));
        return direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : -Vector3.UnitZ;
    }

    public static Vector3 GetDirectionFromEulerDegrees(Vector3 rotationDegrees)
    {
        const float toRadians = MathF.PI / 180.0f;
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
            rotationDegrees.Y * toRadians,
            rotationDegrees.X * toRadians,
            rotationDegrees.Z * toRadians);
        return GetDirection(rotation);
    }

    public static Quaternion CreateRotation(Vector3 direction)
    {
        Vector3 target = IsFinite(direction) && direction.LengthSquared() > 1e-8f
            ? Vector3.Normalize(direction)
            : -Vector3.UnitZ;
        Vector3 source = -Vector3.UnitZ;
        float dot = Math.Clamp(Vector3.Dot(source, target), -1.0f, 1.0f);
        if (dot > 0.999999f)
        {
            return Quaternion.Identity;
        }

        if (dot < -0.999999f)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        }

        return Quaternion.Normalize(new Quaternion(Vector3.Cross(source, target), 1.0f + dot));
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}

internal static class SpotLightPacking
{
    public const int MaxLights = 16;

    public static bool IsValid(SpotLightData light)
    {
        return light.Enabled
            && IsFinite(light.Position)
            && IsFinite(light.Direction)
            && IsFinite(light.Color)
            && float.IsFinite(light.Intensity)
            && float.IsFinite(light.Range)
            && light.Direction.LengthSquared() > 1e-8f
            && light.Intensity > 0.0f
            && light.Range > 0.0f;
    }

    public static int PackViewSpace(
        IReadOnlyList<SpotLightData>? lights,
        Matrix4x4 view,
        Span<Vector4> positionRanges,
        Span<Vector4> directionOuterCosines,
        Span<Vector4> colorIntensities,
        Span<Vector4> coneParameters)
    {
        int capacity = Math.Min(MaxLights,
            Math.Min(Math.Min(positionRanges.Length, directionOuterCosines.Length),
                Math.Min(colorIntensities.Length, coneParameters.Length)));
        int count = 0;
        if (lights is null)
        {
            return 0;
        }

        foreach (SpotLightData light in lights)
        {
            if (count >= capacity)
            {
                break;
            }

            if (!IsValid(light))
            {
                continue;
            }

            float innerAngle = Math.Clamp(light.InnerConeAngleDegrees, 0.0f, 89.0f);
            float outerAngle = Math.Clamp(light.OuterConeAngleDegrees, innerAngle + 0.01f, 89.5f);
            Vector3 viewPosition = Vector3.Transform(light.Position, view);
            Vector3 viewDirection = Vector3.Normalize(Vector3.TransformNormal(light.Direction, view));
            positionRanges[count] = new Vector4(viewPosition, MathF.Max(light.Range, 0.001f));
            directionOuterCosines[count] = new Vector4(viewDirection, MathF.Cos(outerAngle * MathF.PI / 180.0f));
            colorIntensities[count] = new Vector4(Vector3.Max(light.Color, Vector3.Zero), MathF.Max(light.Intensity, 0.0f));
            coneParameters[count] = new Vector4(MathF.Cos(innerAngle * MathF.PI / 180.0f), 0.0f, 0.0f, 0.0f);
            count++;
        }

        return count;
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
