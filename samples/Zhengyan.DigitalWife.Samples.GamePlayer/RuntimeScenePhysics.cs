using System.Numerics;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeScenePhysics
{
    private readonly Func<IEnumerable<RuntimeEntity>> _getEntities;

    internal RuntimeScenePhysics(Func<IEnumerable<RuntimeEntity>> getEntities)
    {
        _getEntities = getEntities;
    }

    public bool Raycast(RuntimeRay ray, out RuntimeRaycastHit hit, float maxDistance = float.MaxValue)
    {
        return Raycast(ray, out hit, maxDistance, ignoredEntity: null, entityType: null);
    }

    public bool Raycast(RuntimeRay ray, out RuntimeRaycastHit hit, float maxDistance, RuntimeEntity? ignoredEntity)
    {
        return Raycast(ray, out hit, maxDistance, ignoredEntity, entityType: null);
    }

    public bool Raycast(RuntimeRay ray, out RuntimeRaycastHit hit, float maxDistance, RuntimeEntity? ignoredEntity, string? entityType)
    {
        hit = default;
        float safeMaxDistance = maxDistance > 0.0f ? maxDistance : float.MaxValue;
        RuntimeRaycastHit bestHit = default;
        float bestDistance = float.MaxValue;

        foreach (RuntimeEntity entity in _getEntities())
        {
            if (ReferenceEquals(entity, ignoredEntity) || IsSameEntity(entity, ignoredEntity))
            {
                continue;
            }

            if (!MatchesEntityType(entity, entityType))
            {
                continue;
            }

            if (RuntimePhysics.TryRaycastEntity(
                entity,
                ray,
                out RuntimeCollider collider,
                out float distance,
                out Vector3 point)
                && distance <= safeMaxDistance
                && distance < bestDistance)
            {
                bestDistance = distance;
                bestHit = new RuntimeRaycastHit(
                    entity,
                    collider.Id,
                    collider.Name,
                    collider.Shape,
                    distance,
                    point);
            }
        }

        if (bestDistance == float.MaxValue)
        {
            return false;
        }

        hit = bestHit;
        return true;
    }

    public bool SampleGround(float x, float z, out RuntimeRaycastHit hit)
    {
        return SampleGround(x, z, out hit, originY: 1000.0f, maxDistance: 2000.0f, ignoredEntity: null, entityType: null);
    }

    public bool SampleGround(float x, float z, out RuntimeRaycastHit hit, float originY, float maxDistance)
    {
        return SampleGround(x, z, out hit, originY, maxDistance, ignoredEntity: null, entityType: null);
    }

    public bool SampleGround(float x, float z, out RuntimeRaycastHit hit, float originY, float maxDistance, RuntimeEntity? ignoredEntity)
    {
        return SampleGround(x, z, out hit, originY, maxDistance, ignoredEntity, entityType: null);
    }

    public bool SampleGround(float x, float z, out RuntimeRaycastHit hit, float originY, float maxDistance, RuntimeEntity? ignoredEntity, string? entityType)
    {
        RuntimeRay ray = new(new Vector3(x, originY, z), -Vector3.UnitY);
        return Raycast(ray, out hit, maxDistance, ignoredEntity, entityType);
    }

    private static bool IsSameEntity(RuntimeEntity entity, RuntimeEntity? other)
    {
        return other is not null
            && (string.Equals(entity.Id, other.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entity.Name, other.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesEntityType(RuntimeEntity entity, string? entityType)
    {
        return string.IsNullOrWhiteSpace(entityType)
            || string.Equals(NormalizeType(entity.Type), NormalizeType(entityType), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeType(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    }
}
