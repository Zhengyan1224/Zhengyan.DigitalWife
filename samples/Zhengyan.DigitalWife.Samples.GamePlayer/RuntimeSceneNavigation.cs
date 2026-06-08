using System.Numerics;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed record RuntimeNavigationBakeResult(int TriangleCount, int EdgeCount);

public sealed record RuntimeNavigationPath(IReadOnlyList<Vector3> Points, float Length)
{
    public bool Success => Points.Count > 0;
}

public sealed class RuntimeSceneNavigation
{
    private readonly Func<IEnumerable<RuntimeEntity>> _getEntities;
    private readonly List<RuntimeNavNode> _nodes = [];
    private float _maxSlopeDegrees = 55.0f;

    internal RuntimeSceneNavigation(Func<IEnumerable<RuntimeEntity>> getEntities)
    {
        _getEntities = getEntities;
    }

    public int TriangleCount => _nodes.Count;

    public RuntimeNavigationBakeResult Bake(float maxSlopeDegrees = 55.0f)
    {
        _maxSlopeDegrees = Math.Clamp(maxSlopeDegrees, 0.0f, 89.9f);
        _nodes.Clear();
        float minUp = MathF.Cos(_maxSlopeDegrees * MathF.PI / 180.0f);

        foreach (RuntimeEntity entity in _getEntities())
        {
            foreach (RuntimeCollider collider in RuntimePhysics.CreateColliders(entity))
            {
                if (collider.Shape != "mesh" || !collider.Walkable)
                {
                    continue;
                }

                float colliderMinUp = MathF.Cos(Math.Clamp(collider.MaxSlopeDegrees, 0.0f, _maxSlopeDegrees) * MathF.PI / 180.0f);
                foreach (RuntimeMeshTriangle triangle in collider.Mesh.Triangles)
                {
                    if (MathF.Abs(triangle.Normal.Y) < MathF.Max(minUp, colliderMinUp))
                    {
                        continue;
                    }

                    _nodes.Add(new RuntimeNavNode(triangle));
                }
            }
        }

        int edgeCount = BuildAdjacency();
        return new RuntimeNavigationBakeResult(_nodes.Count, edgeCount);
    }

    public IReadOnlyList<Vector3> FindPath(Vector3 start, Vector3 end, float maxSnapDistance = 5.0f)
    {
        return TryFindPath(start, end, out RuntimeNavigationPath path, maxSnapDistance)
            ? path.Points
            : [];
    }

    public bool TryFindPath(Vector3 start, Vector3 end, out RuntimeNavigationPath path, float maxSnapDistance = 5.0f)
    {
        EnsureBaked();
        path = new RuntimeNavigationPath([], 0.0f);
        if (_nodes.Count == 0)
        {
            return false;
        }

        float snapDistance = Math.Max(maxSnapDistance, 0.0f);
        if (!TryFindNearestTriangle(start, snapDistance, out int startIndex, out Vector3 snappedStart)
            || !TryFindNearestTriangle(end, snapDistance, out int endIndex, out Vector3 snappedEnd))
        {
            return false;
        }

        if (startIndex == endIndex)
        {
            Vector3[] direct = [snappedStart, snappedEnd];
            path = new RuntimeNavigationPath(direct, Vector3.Distance(snappedStart, snappedEnd));
            return true;
        }

        int[] trianglePath = FindTrianglePath(startIndex, endIndex);
        if (trianglePath.Length == 0)
        {
            return false;
        }

        List<Vector3> points = [snappedStart];
        for (int i = 1; i + 1 < trianglePath.Length; i++)
        {
            points.Add(_nodes[trianglePath[i]].Triangle.Center);
        }

        points.Add(snappedEnd);
        path = new RuntimeNavigationPath(points, ComputePathLength(points));
        return true;
    }

    public bool SamplePosition(Vector3 position, out Vector3 nearest, float maxDistance = 5.0f)
    {
        EnsureBaked();
        return TryFindNearestTriangle(position, Math.Max(maxDistance, 0.0f), out _, out nearest);
    }

    public bool SamplePosition(float x, float y, float z, out Vector3 nearest, float maxDistance = 5.0f)
    {
        return SamplePosition(new Vector3(x, y, z), out nearest, maxDistance);
    }

    private void EnsureBaked()
    {
        if (_nodes.Count == 0)
        {
            Bake(_maxSlopeDegrees);
        }
    }

    private int BuildAdjacency()
    {
        Dictionary<EdgeKey, List<int>> edges = [];
        for (int i = 0; i < _nodes.Count; i++)
        {
            RuntimeMeshTriangle triangle = _nodes[i].Triangle;
            AddEdge(edges, triangle.A, triangle.B, i);
            AddEdge(edges, triangle.B, triangle.C, i);
            AddEdge(edges, triangle.C, triangle.A, i);
        }

        int edgeCount = 0;
        foreach (List<int> linkedNodes in edges.Values)
        {
            if (linkedNodes.Count < 2)
            {
                continue;
            }

            for (int i = 0; i < linkedNodes.Count; i++)
            {
                for (int j = i + 1; j < linkedNodes.Count; j++)
                {
                    int a = linkedNodes[i];
                    int b = linkedNodes[j];
                    if (_nodes[a].Neighbors.Add(b))
                    {
                        edgeCount++;
                    }

                    _nodes[b].Neighbors.Add(a);
                }
            }
        }

        return edgeCount;
    }

    private bool TryFindNearestTriangle(Vector3 position, float maxDistance, out int nodeIndex, out Vector3 nearest)
    {
        nodeIndex = -1;
        nearest = default;
        float maxDistanceSquared = maxDistance * maxDistance;
        float bestDistanceSquared = float.MaxValue;
        Vector3 bestPoint = default;

        for (int i = 0; i < _nodes.Count; i++)
        {
            Vector3 point = ClosestPointOnTriangle(position, _nodes[i].Triangle);
            float distanceSquared = Vector3.DistanceSquared(position, point);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestPoint = point;
                nodeIndex = i;
            }
        }

        if (nodeIndex < 0 || bestDistanceSquared > maxDistanceSquared)
        {
            return false;
        }

        nearest = bestPoint;
        return true;
    }

    private int[] FindTrianglePath(int startIndex, int endIndex)
    {
        PriorityQueue<int, float> open = new();
        Dictionary<int, int> cameFrom = [];
        Dictionary<int, float> costSoFar = [];
        HashSet<int> closed = [];

        costSoFar[startIndex] = 0.0f;
        open.Enqueue(startIndex, Heuristic(startIndex, endIndex));

        while (open.Count > 0)
        {
            int current = open.Dequeue();
            if (!closed.Add(current))
            {
                continue;
            }

            if (current == endIndex)
            {
                return ReconstructPath(cameFrom, startIndex, endIndex);
            }

            foreach (int next in _nodes[current].Neighbors)
            {
                float newCost = costSoFar[current] + Vector3.Distance(_nodes[current].Triangle.Center, _nodes[next].Triangle.Center);
                if (!costSoFar.TryGetValue(next, out float existingCost) || newCost < existingCost)
                {
                    costSoFar[next] = newCost;
                    cameFrom[next] = current;
                    open.Enqueue(next, newCost + Heuristic(next, endIndex));
                }
            }
        }

        return [];
    }

    private float Heuristic(int index, int endIndex)
    {
        return Vector3.Distance(_nodes[index].Triangle.Center, _nodes[endIndex].Triangle.Center);
    }

    private int[] ReconstructPath(Dictionary<int, int> cameFrom, int startIndex, int endIndex)
    {
        List<int> path = [endIndex];
        int current = endIndex;
        while (current != startIndex)
        {
            if (!cameFrom.TryGetValue(current, out current))
            {
                return [];
            }

            path.Add(current);
        }

        path.Reverse();
        return [.. path];
    }

    private static float ComputePathLength(IReadOnlyList<Vector3> points)
    {
        float length = 0.0f;
        for (int i = 1; i < points.Count; i++)
        {
            length += Vector3.Distance(points[i - 1], points[i]);
        }

        return length;
    }

    private static void AddEdge(Dictionary<EdgeKey, List<int>> edges, Vector3 a, Vector3 b, int nodeIndex)
    {
        EdgeKey key = EdgeKey.Create(a, b);
        if (!edges.TryGetValue(key, out List<int>? nodes))
        {
            nodes = [];
            edges[key] = nodes;
        }

        nodes.Add(nodeIndex);
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 point, RuntimeMeshTriangle triangle)
    {
        Vector3 ab = triangle.B - triangle.A;
        Vector3 ac = triangle.C - triangle.A;
        Vector3 ap = point - triangle.A;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0.0f && d2 <= 0.0f)
        {
            return triangle.A;
        }

        Vector3 bp = point - triangle.B;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0.0f && d4 <= d3)
        {
            return triangle.B;
        }

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0.0f && d1 >= 0.0f && d3 <= 0.0f)
        {
            float v = d1 / (d1 - d3);
            return triangle.A + (ab * v);
        }

        Vector3 cp = point - triangle.C;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0.0f && d5 <= d6)
        {
            return triangle.C;
        }

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0.0f && d2 >= 0.0f && d6 <= 0.0f)
        {
            float w = d2 / (d2 - d6);
            return triangle.A + (ac * w);
        }

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0.0f && (d4 - d3) >= 0.0f && (d5 - d6) >= 0.0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return triangle.B + ((triangle.C - triangle.B) * w);
        }

        float denominator = 1.0f / (va + vb + vc);
        float vInside = vb * denominator;
        float wInside = vc * denominator;
        return triangle.A + (ab * vInside) + (ac * wInside);
    }

    private sealed class RuntimeNavNode(RuntimeMeshTriangle triangle)
    {
        public RuntimeMeshTriangle Triangle { get; } = triangle;

        public HashSet<int> Neighbors { get; } = [];
    }

    private readonly record struct EdgeKey(VertexKey A, VertexKey B)
    {
        public static EdgeKey Create(Vector3 a, Vector3 b)
        {
            VertexKey keyA = VertexKey.Create(a);
            VertexKey keyB = VertexKey.Create(b);
            return keyA.CompareTo(keyB) <= 0 ? new EdgeKey(keyA, keyB) : new EdgeKey(keyB, keyA);
        }
    }

    private readonly record struct VertexKey(long X, long Y, long Z) : IComparable<VertexKey>
    {
        private const float QuantizeScale = 1000.0f;

        public static VertexKey Create(Vector3 value)
        {
            return new VertexKey(
                (long)MathF.Round(value.X * QuantizeScale),
                (long)MathF.Round(value.Y * QuantizeScale),
                (long)MathF.Round(value.Z * QuantizeScale));
        }

        public int CompareTo(VertexKey other)
        {
            int x = X.CompareTo(other.X);
            if (x != 0)
            {
                return x;
            }

            int y = Y.CompareTo(other.Y);
            return y != 0 ? y : Z.CompareTo(other.Z);
        }
    }
}
