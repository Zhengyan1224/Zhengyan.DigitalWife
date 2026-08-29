using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed class RuntimeScenePhysics
{
    private readonly Func<IEnumerable<RuntimeEntity>> _getEntities;
    internal RuntimeScenePhysics(Func<IEnumerable<RuntimeEntity>> getEntities) => _getEntities = getEntities;

    public bool Raycast(RuntimeRay ray, out RuntimeRaycastHit hit, float maxDistance = float.MaxValue, RuntimeEntity? ignoredEntity = null, string? entityType = null)
    {
        hit = default; float best = maxDistance > 0 ? maxDistance : float.MaxValue;
        foreach (RuntimeEntity entity in _getEntities())
        {
            if (ReferenceEquals(entity, ignoredEntity) || (ignoredEntity != null && string.Equals(entity.Id, ignoredEntity.Id, StringComparison.OrdinalIgnoreCase))) continue;
            if (!string.IsNullOrWhiteSpace(entityType) && !string.Equals(Normalize(entity.Type), Normalize(entityType), StringComparison.OrdinalIgnoreCase)) continue;
            if (RuntimePhysics.TryRaycastEntity(entity, ray, out RuntimeCollider collider, out float distance, out Vector3 point) && distance <= best)
            { best = distance; hit = new RuntimeRaycastHit(entity, collider.Id, collider.Name, collider.Shape, distance, point); }
        }
        return hit.Entity is not null;
    }

    public bool SampleGround(float x, float z, out RuntimeRaycastHit hit, float originY = 1000.0f, float maxDistance = 2000.0f, RuntimeEntity? ignoredEntity = null, string? entityType = null)
        => Raycast(new RuntimeRay(new Vector3(x, originY, z), -Vector3.UnitY), out hit, maxDistance, ignoredEntity, entityType);

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
}
