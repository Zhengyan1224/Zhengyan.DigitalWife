using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeNavigationBakeResult(int TriangleCount, int EdgeCount);
public sealed record RuntimeNavigationPath(IReadOnlyList<Vector3> Points, float Length) { public bool Success => Points.Count > 0; }
public sealed record RuntimeNavigationDebugInfo(string Status, int StartCandidateCount, int EndCandidateCount, int StartComponentId, int EndComponentId, int StartComponentSize, int EndComponentSize, float StartSnapDistance, float EndSnapDistance);

public sealed class RuntimeSceneNavigation
{
    private readonly Func<IEnumerable<RuntimeEntity>> _getEntities;
    private readonly List<Node> _nodes = [];
    private float _slope = 55, _stepHeight = .45f, _stepDistance = .35f;
    private RuntimeNavigationDebugInfo _debug = new("not_baked", 0, 0, -1, -1, 0, 0, 0, 0);
    internal RuntimeSceneNavigation(Func<IEnumerable<RuntimeEntity>> getEntities) => _getEntities = getEntities;
    public int TriangleCount => _nodes.Count;
    public IReadOnlyList<RuntimeMeshTriangle> Triangles => _nodes.Select(n => n.Triangle).ToArray();
    public RuntimeNavigationDebugInfo LastDebugInfo => _debug;

    public RuntimeNavigationBakeResult Bake(float maxSlopeDegrees = 55, float maxStepHeight = .45f, float maxStepHorizontalDistance = .35f)
    {
        _slope = Math.Clamp(maxSlopeDegrees, 0, 89.9f); _stepHeight = Math.Max(0, maxStepHeight); _stepDistance = Math.Max(0, maxStepHorizontalDistance); _nodes.Clear();
        float minUp = MathF.Cos(_slope * MathF.PI / 180);
        foreach (RuntimeEntity e in _getEntities()) foreach (RuntimeCollider c in RuntimePhysics.CreateColliders(e))
            if (c.Shape == "mesh" && c.Walkable) foreach (RuntimeMeshTriangle t in c.Mesh.Triangles) if (t.Normal.Y >= minUp) _nodes.Add(new Node(t));
        int edges = 0;
        for (int i = 0; i < _nodes.Count; i++) for (int j = i + 1; j < _nodes.Count; j++)
        {
            bool shared = SharedEdge(_nodes[i].Triangle, _nodes[j].Triangle);
            bool step = MathF.Abs(_nodes[i].Triangle.Center.Y - _nodes[j].Triangle.Center.Y) <= _stepHeight && HorizontalDistanceSquared(_nodes[i].Triangle, _nodes[j].Triangle) <= _stepDistance * _stepDistance;
            if (shared || step) { _nodes[i].Neighbors.Add(j); _nodes[j].Neighbors.Add(i); edges++; }
        }
        AssignComponents(); _debug = new RuntimeNavigationDebugInfo("baked", 0, 0, -1, -1, 0, 0, 0, 0); return new RuntimeNavigationBakeResult(_nodes.Count, edges);
    }
    public IReadOnlyList<Vector3> FindPath(Vector3 start, Vector3 end, float maxSnapDistance = 5) => TryFindPath(start, end, out RuntimeNavigationPath path, maxSnapDistance) ? path.Points : [];
    public bool TryFindPath(Vector3 start, Vector3 end, out RuntimeNavigationPath path, float maxSnapDistance = 5)
    {
        EnsureBaked(); path = new RuntimeNavigationPath([], 0); if (_nodes.Count == 0) return false;
        if (!Nearest(start, maxSnapDistance, out int si, out Vector3 sp) || !Nearest(end, maxSnapDistance, out int ei, out Vector3 ep)) return false;
        if (si == ei) { path = new RuntimeNavigationPath([sp, ep], Vector3.Distance(sp, ep)); return true; }
        PriorityQueue<int, float> open = new(); Dictionary<int, int> came = []; Dictionary<int, float> cost = new() { [si] = 0 }; HashSet<int> closed = []; open.Enqueue(si, 0);
        while (open.Count > 0) { int cur = open.Dequeue(); if (!closed.Add(cur)) continue; if (cur == ei) break; foreach (int n in _nodes[cur].Neighbors) { float next = cost[cur] + Vector3.Distance(_nodes[cur].Triangle.Center, _nodes[n].Triangle.Center); if (!cost.TryGetValue(n, out float old) || next < old) { cost[n] = next; came[n] = cur; open.Enqueue(n, next + Vector3.Distance(_nodes[n].Triangle.Center, _nodes[ei].Triangle.Center)); } } }
        if (!came.ContainsKey(ei)) return false; List<Vector3> points = [sp]; List<int> ids = [ei]; for (int c = ei; c != si; c = came[c]) ids.Add(c); ids.Reverse(); for (int i = 1; i < ids.Count; i++) points.Add(_nodes[ids[i]].Triangle.Center); points.Add(ep); path = new RuntimeNavigationPath(points, Length(points)); return true;
    }
    public bool SamplePosition(Vector3 position, out Vector3 nearest, float maxDistance = 5) { EnsureBaked(); return Nearest(position, maxDistance, out _, out nearest); }
    public bool SamplePosition(float x, float y, float z, out Vector3 nearest, float maxDistance = 5) => SamplePosition(new Vector3(x, y, z), out nearest, maxDistance);
    public void DrawDebug(RuntimeDebug debug, Vector4? color = null, float durationSeconds = .1f) { foreach (RuntimeMeshTriangle t in Triangles) { debug.DrawLine(t.A, t.B, color, durationSeconds); debug.DrawLine(t.B, t.C, color, durationSeconds); debug.DrawLine(t.C, t.A, color, durationSeconds); } }
    private void EnsureBaked() { if (_nodes.Count == 0) Bake(_slope, _stepHeight, _stepDistance); }
    private bool Nearest(Vector3 p, float max, out int index, out Vector3 point) { index = -1; point = default; float best = max * max; for (int i = 0; i < _nodes.Count; i++) { Vector3 q = Closest(p, _nodes[i].Triangle); float d = Vector3.DistanceSquared(p, q); if (d <= best) { best = d; index = i; point = q; } } return index >= 0; }
    private void AssignComponents() { int id = 0; for (int i = 0; i < _nodes.Count; i++) _nodes[i].Component = -1; for (int i = 0; i < _nodes.Count; i++) if (_nodes[i].Component < 0) { Queue<int> q = new([i]); _nodes[i].Component = id; while (q.Count > 0) foreach (int n in _nodes[q.Dequeue()].Neighbors) if (_nodes[n].Component < 0) { _nodes[n].Component = id; q.Enqueue(n); } id++; } }
    private static bool SharedEdge(RuntimeMeshTriangle a, RuntimeMeshTriangle b) => new[] { a.A, a.B, a.C }.Count(x => new[] { b.A, b.B, b.C }.Any(y => Vector3.DistanceSquared(x, y) < .00001f)) >= 2;
    private static float HorizontalDistanceSquared(RuntimeMeshTriangle a, RuntimeMeshTriangle b) => Vector2.DistanceSquared(new(a.Center.X, a.Center.Z), new(b.Center.X, b.Center.Z));
    private static float Length(IReadOnlyList<Vector3> p) { float l = 0; for (int i = 1; i < p.Count; i++) l += Vector3.Distance(p[i - 1], p[i]); return l; }
    private static Vector3 Closest(Vector3 p, RuntimeMeshTriangle t) { Vector3 n = t.Normal; float d = Vector3.Dot(p - t.A, n); Vector3 q = p - n * d; return q; }
    private sealed class Node(RuntimeMeshTriangle triangle) { public RuntimeMeshTriangle Triangle { get; } = triangle; public HashSet<int> Neighbors { get; } = []; public int Component { get; set; } }
}
