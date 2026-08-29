using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public readonly record struct RuntimeDebugLine(Vector3 Start, Vector3 End, Vector4 Color, float RemainingSeconds);

public sealed class RuntimeDebug
{
    private readonly List<RuntimeDebugLine> _lines = [];
    private readonly object _sync = new();

    public void DrawLine(Vector3 start, Vector3 end, Vector4? color = null, float durationSeconds = 0.1f)
    {
        if (!float.IsFinite(durationSeconds) || durationSeconds <= 0) return;
        lock (_sync) _lines.Add(new RuntimeDebugLine(start, end, color ?? new Vector4(1, 1, .1f, 1), durationSeconds));
    }
    public void DrawLine(float startX, float startY, float startZ, float endX, float endY, float endZ, Vector4? color = null, float durationSeconds = .1f)
        => DrawLine(new Vector3(startX, startY, startZ), new Vector3(endX, endY, endZ), color, durationSeconds);
    public void DrawRay(Vector3 origin, Vector3 direction, float length = 10, Vector4? color = null, float durationSeconds = .1f)
        => DrawLine(origin, origin + (direction.LengthSquared() <= .000001f ? -Vector3.UnitZ : Vector3.Normalize(direction)) * Math.Max(0, length), color ?? new Vector4(1, .2f, .1f, 1), durationSeconds);
    public void DrawRay(float ox, float oy, float oz, float dx, float dy, float dz, float length = 10, float durationSeconds = .1f)
        => DrawRay(new Vector3(ox, oy, oz), new Vector3(dx, dy, dz), length, null, durationSeconds);
    public void Clear() { lock (_sync) _lines.Clear(); }
    public void DrawCollider(RuntimeEntity entity, Vector4? color = null, float durationSeconds = 0.1f)
    {
        Vector4 lineColor = color ?? new Vector4(0.1f, 1.0f, 0.2f, 1.0f);
        foreach (RuntimeCollider collider in RuntimePhysics.CreateColliders(entity))
        {
            if (collider.Shape == "mesh")
            {
                foreach (RuntimeMeshTriangle t in collider.Mesh.Triangles) { DrawLine(t.A, t.B, lineColor, durationSeconds); DrawLine(t.B, t.C, lineColor, durationSeconds); DrawLine(t.C, t.A, lineColor, durationSeconds); }
            }
            else if (collider.Shape == "box")
            {
                RuntimeBox b = collider.Box;
                Vector3[] p = new Vector3[8];
                for (int i = 0; i < 8; i++) p[i] = b.Center + b.AxisX * ( ((i & 1) == 0 ? -1 : 1) * b.HalfExtents.X) + b.AxisY * (((i & 2) == 0 ? -1 : 1) * b.HalfExtents.Y) + b.AxisZ * (((i & 4) == 0 ? -1 : 1) * b.HalfExtents.Z);
                int[] edges = [0,1, 1,3, 3,2, 2,0, 4,5, 5,7, 7,6, 6,4, 0,4, 1,5, 2,6, 3,7];
                for (int i = 0; i < edges.Length; i += 2) DrawLine(p[edges[i]], p[edges[i + 1]], lineColor, durationSeconds);
            }
            else
            {
                RuntimeCapsule c = collider.Capsule; Vector3 axis = c.End - c.Start; Vector3 normal = axis.LengthSquared() > 0.000001f ? Vector3.Normalize(axis) : Vector3.UnitY; Vector3 tangent = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Y) > .9f ? Vector3.UnitX : Vector3.UnitY)); Vector3 bitangent = Vector3.Normalize(Vector3.Cross(normal, tangent));
                const int segments = 12; for (int i = 0; i < segments; i++) { float a = i * MathF.PI * 2 / segments, b = (i + 1) * MathF.PI * 2 / segments; Vector3 o0 = (tangent * MathF.Cos(a) + bitangent * MathF.Sin(a)) * c.Radius; Vector3 o1 = (tangent * MathF.Cos(b) + bitangent * MathF.Sin(b)) * c.Radius; DrawLine(c.Start + o0, c.Start + o1, lineColor, durationSeconds); DrawLine(c.End + o0, c.End + o1, lineColor, durationSeconds); DrawLine(c.Start + o0, c.End + o0, lineColor, durationSeconds); }
            }
        }
    }
    public void DrawColliders(IEnumerable<RuntimeEntity> entities, Vector4? color = null, float durationSeconds = 0.1f) { foreach (RuntimeEntity entity in entities) DrawCollider(entity, color, durationSeconds); }
    public void DrawColliders(RuntimeScene scene, Vector4? color = null, float durationSeconds = 0.1f) => DrawColliders(scene.Entities, color, durationSeconds);
    public void DrawNavigation(RuntimeSceneNavigation navigation, Vector4? color = null, float durationSeconds = 0.1f) => navigation.DrawDebug(this, color, durationSeconds);
    public IReadOnlyList<RuntimeDebugLine> Snapshot() { lock (_sync) return _lines.ToArray(); }
    internal void Update(float deltaSeconds)
    {
        lock (_sync)
        {
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                RuntimeDebugLine line = _lines[i] with { RemainingSeconds = _lines[i].RemainingSeconds - deltaSeconds };
                if (line.RemainingSeconds <= 0) _lines.RemoveAt(i); else _lines[i] = line;
            }
        }
    }
}
