using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public readonly record struct RuntimeRay(Vector3 Origin, Vector3 Direction)
{
    public RuntimeRay Normalized => new(Origin, Direction.LengthSquared() <= 0.000001f ? -Vector3.UnitZ : Vector3.Normalize(Direction));
}

public readonly record struct RuntimeRaycastHit(RuntimeEntity Entity, string ColliderId, string ColliderName, string ColliderShape, float Distance, Vector3 Point);
public readonly record struct RuntimeCapsule(Vector3 Start, Vector3 End, float Radius) { public Vector3 Center => (Start + End) * 0.5f; }
public readonly record struct RuntimeBox(Vector3 Center, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ, Vector3 HalfExtents);
public readonly record struct RuntimeMeshTriangle(Vector3 A, Vector3 B, Vector3 C)
{
    public Vector3 Center => (A + B + C) / 3.0f;
    public Vector3 Normal => Vector3.Cross(B - A, C - A) is var n && n.LengthSquared() > 0.000001f ? Vector3.Normalize(n) : Vector3.UnitY;
}
public readonly record struct RuntimeMeshCollider(IReadOnlyList<RuntimeMeshTriangle> Triangles);
public readonly record struct RuntimeCollider(string Id, string Name, string Shape, RuntimeCapsule Capsule, RuntimeBox Box, RuntimeMeshCollider Mesh, bool Walkable, float MaxSlopeDegrees);

public static class RuntimePhysics
{
    public static IEnumerable<RuntimeCollider> CreateColliders(RuntimeEntity entity)
    {
        foreach (ColliderSettings settings in entity.EffectiveColliders)
        {
            if (!settings.Enabled) continue;
            string shape = NormalizeShape(settings.Shape);
            if (shape == "mesh")
            {
                if (entity.TryCreateMeshCollider(settings, out RuntimeMeshCollider mesh))
                    yield return new RuntimeCollider(settings.Id, settings.Name, shape, default, default, mesh, settings.Walkable, Math.Clamp(settings.MaxSlopeDegrees, 0.0f, 89.9f));
                continue;
            }
            ColliderGeometry geometry = CollisionGeometry.CreateCollider(settings, entity.GetColliderParentWorld(settings));
            if (geometry.Shape == "box")
                yield return new RuntimeCollider(geometry.Id, geometry.Name, geometry.Shape, default, new RuntimeBox(geometry.Box.Center, geometry.Box.AxisX, geometry.Box.AxisY, geometry.Box.AxisZ, geometry.Box.HalfExtents), default, settings.Walkable, Math.Clamp(settings.MaxSlopeDegrees, 0.0f, 89.9f));
            else
                yield return new RuntimeCollider(geometry.Id, geometry.Name, geometry.Shape, new RuntimeCapsule(geometry.Capsule.Start, geometry.Capsule.End, geometry.Capsule.Radius), default, default, settings.Walkable, Math.Clamp(settings.MaxSlopeDegrees, 0.0f, 89.9f));
        }
    }

    public static bool TryCreateCapsule(RuntimeEntity entity, out RuntimeCapsule capsule)
    {
        RuntimeCollider hit = CreateColliders(entity).FirstOrDefault(c => c.Shape == "capsule");
        capsule = hit.Capsule;
        return !string.IsNullOrEmpty(hit.Id);
    }

    public static bool TryRaycastEntity(RuntimeEntity entity, RuntimeRay ray, out RuntimeCollider hitCollider, out float distance, out Vector3 point)
    {
        hitCollider = default; distance = float.MaxValue; point = default;
        foreach (RuntimeCollider collider in CreateColliders(entity))
        {
            bool hit = collider.Shape == "mesh" ? TryRaycastMesh(ray, collider.Mesh, out float d, out Vector3 p) : CollisionGeometry.TryRaycastCollider(ray.Origin, ray.Direction, ToGeometry(collider), out d, out p);
            if (hit && d >= 0.0f && d < distance) { distance = d; point = p; hitCollider = collider; }
        }
        return !string.IsNullOrEmpty(hitCollider.Id);
    }

    public static bool CheckCollision(RuntimeEntity left, RuntimeEntity right)
    {
        foreach (RuntimeCollider a in CreateColliders(left)) foreach (RuntimeCollider b in CreateColliders(right))
            if (a.Shape != "mesh" && b.Shape != "mesh" && CollisionGeometry.CheckColliderCollision(ToGeometry(a), ToGeometry(b))) return true;
        return false;
    }

    public static float DistanceBetween(RuntimeEntity left, RuntimeEntity right)
    {
        float best = float.MaxValue; bool found = false;
        foreach (RuntimeCollider a in CreateColliders(left)) foreach (RuntimeCollider b in CreateColliders(right))
            if (a.Shape != "mesh" && b.Shape != "mesh") { found = true; best = MathF.Min(best, CollisionGeometry.DistanceBetweenColliders(ToGeometry(a), ToGeometry(b))); }
        return found ? best : Vector3.Distance(left.Position, right.Position);
    }

    public static bool TryRaycastMesh(RuntimeRay ray, RuntimeMeshCollider mesh, out float distance, out Vector3 point)
    {
        distance = float.MaxValue; point = default; RuntimeRay normalized = ray.Normalized;
        foreach (RuntimeMeshTriangle t in mesh.Triangles)
            if (TryRaycastTriangle(normalized.Origin, normalized.Direction, t, out float d) && d < distance) { distance = d; point = normalized.Origin + normalized.Direction * d; }
        return distance != float.MaxValue;
    }

    private static ColliderGeometry ToGeometry(RuntimeCollider c) => c.Shape == "box"
        ? new ColliderGeometry(c.Id, c.Name, c.Shape, default, new BoxGeometry(c.Box.Center, c.Box.AxisX, c.Box.AxisY, c.Box.AxisZ, c.Box.HalfExtents))
        : new ColliderGeometry(c.Id, c.Name, "capsule", new CapsuleGeometry(c.Capsule.Start, c.Capsule.End, c.Capsule.Radius), default);

    private static bool TryRaycastTriangle(Vector3 origin, Vector3 direction, RuntimeMeshTriangle t, out float distance)
    {
        const float eps = 0.000001f; distance = 0.0f;
        Vector3 e1 = t.B - t.A, e2 = t.C - t.A, p = Vector3.Cross(direction, e2);
        float det = Vector3.Dot(e1, p); if (MathF.Abs(det) < eps) return false;
        float inv = 1.0f / det; Vector3 q = origin - t.A; float u = Vector3.Dot(q, p) * inv;
        if (u < 0 || u > 1) return false; Vector3 r = Vector3.Cross(q, e1); float v = Vector3.Dot(direction, r) * inv;
        if (v < 0 || u + v > 1) return false; distance = Vector3.Dot(e2, r) * inv; return distance >= 0;
    }

    private static string NormalizeShape(string? shape) => (shape ?? string.Empty).Trim().ToLowerInvariant() switch { "box" or "cube" or "aabb" => "box", "mesh" => "mesh", _ => "capsule" };
}
