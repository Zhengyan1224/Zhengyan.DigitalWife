using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Components;

/// <summary>Android's localized surface-contact window, shared by desktop preview and players.</summary>
public static class ParticleWaterRipplePolicy
{
    public static bool IsRippleContact(ParticleCollisionSample sample, Vector3 waterPosition, float waterSize, float interactionRadius)
    {
        Vector3 local = sample.Position - waterPosition;
        float verticalReach = Math.Max(interactionRadius, 0.05f) + sample.Radius;
        // Keep the existing Android ripple footprint (Size / 2 in world space).
        // It is a visual interaction region, separate from the scaled water mesh and particle removal bounds.
        float rippleRegionRadius = Math.Max(waterSize, 0.1f) * 0.5f;
        return MathF.Abs(local.Y) <= verticalReach
            && local.X * local.X + local.Z * local.Z <= rippleRegionRadius * rippleRegionRadius;
    }

    public static string BuildKey(string waterId, string particleSystemId, int particleIndex)
        => $"{waterId}:{particleSystemId}:particle:{particleIndex}";

    public static bool IsSurfaceContact(ParticleCollisionSample sample, float surfaceHeight, float interactionRadius)
    {
        float distance = sample.Position.Y - surfaceHeight;
        return distance <= sample.Radius && distance >= -(Math.Max(interactionRadius, 0.05f) + sample.Radius);
    }
}
