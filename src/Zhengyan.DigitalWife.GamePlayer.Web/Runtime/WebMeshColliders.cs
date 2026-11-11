using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed class WebMeshColliders
{
    private sealed record Entry(WebPmxMesh Mesh, Matrix4x4 World, RuntimeMeshCollider Collider);
    private readonly Dictionary<string, Entry> _cache = new();

    public RuntimeMeshCollider? Resolve(RuntimeEntity entity, ColliderSettings settings, WebPmxMesh mesh)
    {
        Vector3 scale = settings.Size.ToVector3(), angles = settings.RotationDegrees.ToVector3() * (MathF.PI / 180);
        scale = new(MathF.Abs(scale.X) < 1e-6f ? 1 : scale.X, MathF.Abs(scale.Y) < 1e-6f ? 1 : scale.Y, MathF.Abs(scale.Z) < 1e-6f ? 1 : scale.Z);
        Matrix4x4 world = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromYawPitchRoll(angles.Y, angles.X, angles.Z))
            * Matrix4x4.CreateTranslation(settings.Position.ToVector3()) * entity.TransformMatrix;
        string key = entity.Id + ":" + settings.Id;
        if (_cache.TryGetValue(key, out var old) && ReferenceEquals(old.Mesh, mesh) && old.World == world) return old.Collider;
        Vector3 Vertex(uint index) => Vector3.Transform(new(mesh.Positions[index * 3], mesh.Positions[index * 3 + 1], mesh.Positions[index * 3 + 2]), world);
        List<RuntimeMeshTriangle> triangles = [];
        for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            uint a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
            if (a * 3 + 2 >= mesh.Positions.Length || b * 3 + 2 >= mesh.Positions.Length || c * 3 + 2 >= mesh.Positions.Length) continue;
            RuntimeMeshTriangle triangle = new(Vertex(a), Vertex(b), Vertex(c));
            if (Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).LengthSquared() > 1e-10f) triangles.Add(triangle);
        }
        RuntimeMeshCollider collider = new(triangles);
        _cache[key] = new(mesh, world, collider);
        return collider;
    }
}
