using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public readonly record struct RuntimeRaycastHit(RuntimeEntity Entity, string ColliderId, string ColliderName, string ColliderShape, float Distance, Vector3 Point);

public readonly record struct RuntimeCapsule(Vector3 Start, Vector3 End, float Radius)
{
    public Vector3 Center => (Start + End) * 0.5f;
}

public readonly record struct RuntimeBox(Vector3 Center, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ, Vector3 HalfExtents);

public readonly record struct RuntimeMeshTriangle(Vector3 A, Vector3 B, Vector3 C)
{
    public Vector3 Center => (A + B + C) / 3.0f;

    public Vector3 Normal => SafeNormalize(Vector3.Cross(B - A, C - A), Vector3.UnitY);

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() <= 0.000001f ? fallback : Vector3.Normalize(value);
    }
}

public readonly record struct RuntimeMeshCollider(IReadOnlyList<RuntimeMeshTriangle> Triangles);

public readonly record struct RuntimeCollider(
    string Id,
    string Name,
    string Shape,
    RuntimeCapsule Capsule,
    RuntimeBox Box,
    RuntimeMeshCollider Mesh,
    bool Walkable,
    float MaxSlopeDegrees);

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

            string shape = NormalizeShape(settings.Shape);
            if (shape == "mesh")
            {
                if (entity.TryCreateMeshCollider(settings, out RuntimeMeshCollider mesh))
                {
                    yield return new RuntimeCollider(
                        settings.Id,
                        settings.Name,
                        "mesh",
                        default,
                        default,
                        mesh,
                        settings.Walkable,
                        Math.Clamp(settings.MaxSlopeDegrees, 0.0f, 89.9f));
                }

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
                        geometry.Box.HalfExtents),
                    default,
                    settings.Walkable,
                    Math.Clamp(settings.MaxSlopeDegrees, 0.0f, 89.9f))
                : new RuntimeCollider(
                    geometry.Id,
                    geometry.Name,
                    geometry.Shape,
                    new RuntimeCapsule(
                        geometry.Capsule.Start,
                        geometry.Capsule.End,
                        geometry.Capsule.Radius),
                    default,
                    default,
                    settings.Walkable,
                    Math.Clamp(settings.MaxSlopeDegrees, 0.0f, 89.9f));
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
        if (collider.Shape == "mesh")
        {
            return TryRaycastMesh(ray, collider.Mesh, out distance, out point);
        }

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
            if (leftCollider.Shape == "mesh")
            {
                continue;
            }

            foreach (RuntimeCollider rightCollider in CreateColliders(right))
            {
                if (rightCollider.Shape == "mesh")
                {
                    continue;
                }

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
            if (leftCollider.Shape == "mesh")
            {
                continue;
            }

            foreach (RuntimeCollider rightCollider in CreateColliders(right))
            {
                if (rightCollider.Shape == "mesh")
                {
                    continue;
                }

                hasCollider = true;
                bestDistance = MathF.Min(bestDistance, CollisionGeometry.DistanceBetweenColliders(ToGeometry(leftCollider), ToGeometry(rightCollider)));
            }
        }

        return hasCollider
            ? bestDistance
            : Vector3.Distance(left.Position, right.Position);
    }

    public static bool TryRaycastMesh(RuntimeRay ray, RuntimeMeshCollider mesh, out float distance, out Vector3 point)
    {
        distance = 0.0f;
        point = default;
        Vector3 direction = SafeNormalize(ray.Direction, -Vector3.UnitZ);
        float bestDistance = float.MaxValue;
        Vector3 bestPoint = default;

        foreach (RuntimeMeshTriangle triangle in mesh.Triangles)
        {
            if (TryRaycastTriangle(ray.Origin, direction, triangle, out float hitDistance)
                && hitDistance < bestDistance)
            {
                bestDistance = hitDistance;
                bestPoint = ray.Origin + (direction * hitDistance);
            }
        }

        if (bestDistance == float.MaxValue)
        {
            return false;
        }

        distance = bestDistance;
        point = bestPoint;
        return true;
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

    private static bool TryRaycastTriangle(Vector3 origin, Vector3 direction, RuntimeMeshTriangle triangle, out float distance)
    {
        const float epsilon = 0.000001f;
        distance = 0.0f;
        Vector3 edge1 = triangle.B - triangle.A;
        Vector3 edge2 = triangle.C - triangle.A;
        Vector3 pVector = Vector3.Cross(direction, edge2);
        float determinant = Vector3.Dot(edge1, pVector);
        if (MathF.Abs(determinant) < epsilon)
        {
            return false;
        }

        float inverseDeterminant = 1.0f / determinant;
        Vector3 tVector = origin - triangle.A;
        float u = Vector3.Dot(tVector, pVector) * inverseDeterminant;
        if (u < 0.0f || u > 1.0f)
        {
            return false;
        }

        Vector3 qVector = Vector3.Cross(tVector, edge1);
        float v = Vector3.Dot(direction, qVector) * inverseDeterminant;
        if (v < 0.0f || u + v > 1.0f)
        {
            return false;
        }

        distance = Vector3.Dot(edge2, qVector) * inverseDeterminant;
        return distance >= 0.0f;
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() <= 0.000001f ? fallback : Vector3.Normalize(value);
    }

    private static string NormalizeShape(string shape)
    {
        return (shape ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "box" => "box",
            "mesh" => "mesh",
            _ => "capsule"
        };
    }
}
