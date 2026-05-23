using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public readonly record struct RuntimeRaycastHit(RuntimeEntity Entity, string ColliderId, string ColliderName, string ColliderShape, float Distance, Vector3 Point);

public readonly record struct RuntimeCapsule(Vector3 Start, Vector3 End, float Radius)
{
    public Vector3 Center => (Start + End) * 0.5f;
}

public readonly record struct RuntimeBox(Vector3 Center, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ, Vector3 HalfExtents);

public readonly record struct RuntimeCollider(
    string Id,
    string Name,
    string Shape,
    RuntimeCapsule Capsule,
    RuntimeBox Box);

public static class RuntimePhysics
{
    public static IEnumerable<RuntimeCollider> CreateColliders(RuntimeEntity entity)
    {
        foreach (ColliderSettings settings in entity.EffectiveColliders)
        {
            if (!settings.Enabled)
            {
                continue;
            }

            ColliderGeometry geometry = CollisionGeometry.CreateCollider(settings, entity.Position, entity.Rotation, entity.Scale);
            yield return geometry.Shape == "box"
                ? new RuntimeCollider(
                    geometry.Id,
                    geometry.Name,
                    geometry.Shape,
                    default,
                    new RuntimeBox(
                        geometry.Box.Center,
                        geometry.Box.AxisX,
                        geometry.Box.AxisY,
                        geometry.Box.AxisZ,
                        geometry.Box.HalfExtents))
                : new RuntimeCollider(
                    geometry.Id,
                    geometry.Name,
                    geometry.Shape,
                    new RuntimeCapsule(
                        geometry.Capsule.Start,
                        geometry.Capsule.End,
                        geometry.Capsule.Radius),
                    default);
        }
    }

    public static bool TryCreateCapsule(RuntimeEntity entity, out RuntimeCapsule capsule)
    {
        capsule = default;
        RuntimeCollider collider = CreateColliders(entity).FirstOrDefault(item => item.Shape == "capsule");
        if (string.IsNullOrEmpty(collider.Id))
        {
            return false;
        }

        capsule = collider.Capsule;
        return true;
    }

    public static bool TryRaycastCollider(RuntimeRay ray, RuntimeCollider collider, out float distance, out Vector3 point)
    {
        return CollisionGeometry.TryRaycastCollider(
            ray.Origin,
            ray.Direction,
            ToGeometry(collider),
            out distance,
            out point);
    }

    public static bool TryRaycastEntity(RuntimeEntity entity, RuntimeRay ray, out RuntimeCollider hitCollider, out float distance, out Vector3 point)
    {
        hitCollider = default;
        distance = 0.0f;
        point = default;
        float bestDistance = float.MaxValue;
        Vector3 bestPoint = default;
        RuntimeCollider bestCollider = default;

        foreach (RuntimeCollider collider in CreateColliders(entity))
        {
            if (TryRaycastCollider(ray, collider, out float hitDistance, out Vector3 hitPoint)
                && hitDistance < bestDistance)
            {
                bestDistance = hitDistance;
                bestPoint = hitPoint;
                bestCollider = collider;
            }
        }

        if (string.IsNullOrEmpty(bestCollider.Id))
        {
            return false;
        }

        hitCollider = bestCollider;
        distance = bestDistance;
        point = bestPoint;
        return true;
    }

    public static bool CheckCapsuleCollision(RuntimeCapsule left, RuntimeCapsule right)
    {
        return CollisionGeometry.CheckCapsuleCollision(
            new CapsuleGeometry(left.Start, left.End, left.Radius),
            new CapsuleGeometry(right.Start, right.End, right.Radius));
    }

    public static float DistanceBetweenCapsules(RuntimeCapsule left, RuntimeCapsule right)
    {
        return CollisionGeometry.DistanceBetweenCapsules(
            new CapsuleGeometry(left.Start, left.End, left.Radius),
            new CapsuleGeometry(right.Start, right.End, right.Radius));
    }

    public static bool CheckCollision(RuntimeEntity left, RuntimeEntity right)
    {
        foreach (RuntimeCollider leftCollider in CreateColliders(left))
        {
            foreach (RuntimeCollider rightCollider in CreateColliders(right))
            {
                if (CollisionGeometry.CheckColliderCollision(ToGeometry(leftCollider), ToGeometry(rightCollider)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static float DistanceBetween(RuntimeEntity left, RuntimeEntity right)
    {
        float bestDistance = float.MaxValue;
        bool hasCollider = false;

        foreach (RuntimeCollider leftCollider in CreateColliders(left))
        {
            foreach (RuntimeCollider rightCollider in CreateColliders(right))
            {
                hasCollider = true;
                bestDistance = MathF.Min(bestDistance, CollisionGeometry.DistanceBetweenColliders(ToGeometry(leftCollider), ToGeometry(rightCollider)));
            }
        }

        return hasCollider
            ? bestDistance
            : Vector3.Distance(left.Position, right.Position);
    }

    private static ColliderGeometry ToGeometry(RuntimeCollider collider)
    {
        return collider.Shape == "box"
            ? new ColliderGeometry(
                collider.Id,
                collider.Name,
                collider.Shape,
                default,
                new BoxGeometry(
                    collider.Box.Center,
                    collider.Box.AxisX,
                    collider.Box.AxisY,
                    collider.Box.AxisZ,
                    collider.Box.HalfExtents))
            : new ColliderGeometry(
                collider.Id,
                collider.Name,
                "capsule",
                new CapsuleGeometry(
                    collider.Capsule.Start,
                    collider.Capsule.End,
                    collider.Capsule.Radius),
                default);
    }
}
