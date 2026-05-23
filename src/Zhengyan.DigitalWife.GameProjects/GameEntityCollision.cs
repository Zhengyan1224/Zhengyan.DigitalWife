namespace Zhengyan.DigitalWife.GameProjects;

public static class GameEntityCollision
{
    public static IEnumerable<ColliderSettings> GetEffectiveColliders(GameEntity entity)
    {
        if (entity.Colliders.Count > 0)
        {
            return entity.Colliders;
        }

        return entity.Collision.Enabled
            ? [CollisionGeometry.FromLegacy(entity.Collision)]
            : [];
    }

    public static bool HasEnabledCollider(GameEntity entity)
    {
        return GetEffectiveColliders(entity).Any(collider => collider.Enabled);
    }

    public static void MigrateLegacyCollision(GameEntity entity)
    {
        if (entity.Colliders.Count > 0 || !entity.Collision.Enabled)
        {
            return;
        }

        entity.Colliders.Add(CollisionGeometry.FromLegacy(entity.Collision));
        entity.Collision.Enabled = false;
    }
}
