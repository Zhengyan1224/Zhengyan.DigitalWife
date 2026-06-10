using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed record RuntimeNavigationBakeResult(int TriangleCount, int EdgeCount);

public sealed record RuntimeNavigationPath(IReadOnlyList<Vector3> Points, float Length)
{
    public bool Success => Points.Count > 0;
}

public sealed record RuntimeNavigationDebugInfo(
    string Status,
    int StartCandidateCount,
    int EndCandidateCount,
    int StartComponentId,
    int EndComponentId,
    int StartComponentSize,
    int EndComponentSize,
    float StartSnapDistance,
    float EndSnapDistance);

public sealed class RuntimeSceneNavigation
{
    private readonly Func<IEnumerable<RuntimeEntity>> _getEntities;
    private readonly List<RuntimeNavNode> _nodes = [];
    private float _maxSlopeDegrees = 55.0f;
    private float _maxStepHeight = 0.45f;
    private float _maxStepHorizontalDistance = 0.35f;
    private RuntimeNavigationDebugInfo _lastDebugInfo = new("not_baked", 0, 0, -1, -1, 0, 0, 0.0f, 0.0f);

    internal RuntimeSceneNavigation(Func<IEnumerable<RuntimeEntity>> getEntities)
    {
        _getEntities = getEntities;
    }

    public int TriangleCount => _nodes.Count;

    public RuntimeNavigationDebugInfo LastDebugInfo => _lastDebugInfo;

    public RuntimeNavigationBakeResult Bake(
        float maxSlopeDegrees = 55.0f,
        float maxStepHeight = 0.45f,
        float maxStepHorizontalDistance = 0.35f)
    {
        _maxSlopeDegrees = Math.Clamp(maxSlopeDegrees, 0.0f, 89.9f);
        _maxStepHeight = Math.Max(0.0f, maxStepHeight);
        _maxStepHorizontalDistance = Math.Max(0.0f, maxStepHorizontalDistance);
        _nodes.Clear();
        _lastDebugInfo = new RuntimeNavigationDebugInfo("baking", 0, 0, -1, -1, 0, 0, 0.0f, 0.0f);
        float minUp = MathF.Cos(_maxSlopeDegrees * MathF.PI / 180.0f);

        foreach (RuntimeEntity entity in _getEntities())
        {
            foreach (RuntimeCollider collider in RuntimePhysics.CreateColliders(entity))
            {
                if (collider.Shape != "mesh" || !collider.Walkable)
                {
                    continue;
                }

                float colliderMaxSlopeDegrees = collider.MaxSlopeDegrees <= 0.001f
                    ? _maxSlopeDegrees
                    : Math.Clamp(collider.MaxSlopeDegrees, 0.0f, _maxSlopeDegrees);
                float colliderMinUp = MathF.Cos(colliderMaxSlopeDegrees * MathF.PI / 180.0f);
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

        int edgeCount = BuildAdjacency(_maxStepHeight, _maxStepHorizontalDistance);
        _lastDebugInfo = new RuntimeNavigationDebugInfo("baked", 0, 0, -1, -1, 0, 0, 0.0f, 0.0f);
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
            _lastDebugInfo = new RuntimeNavigationDebugInfo("empty_navmesh", 0, 0, -1, -1, 0, 0, 0.0f, 0.0f);
            return false;
        }

        float snapDistance = Math.Max(maxSnapDistance, 0.0f);
        if (!TryFindPathEndpoints(
            start,
            end,
            snapDistance,
            out int startIndex,
            out Vector3 snappedStart,
            out int endIndex,
            out Vector3 snappedEnd))
        {
            return false;
        }

        _lastDebugInfo = BuildDebugInfo("endpoints_found", start, end, startIndex, endIndex, snappedStart, snappedEnd, snapDistance);

        if (startIndex == endIndex)
        {
            Vector3[] direct = [snappedStart, snappedEnd];
            path = new RuntimeNavigationPath(direct, Vector3.Distance(snappedStart, snappedEnd));
            return true;
        }

        int[] trianglePath = FindTrianglePath(startIndex, endIndex);
        if (trianglePath.Length == 0)
        {
            _lastDebugInfo = BuildDebugInfo("no_triangle_path", start, end, startIndex, endIndex, snappedStart, snappedEnd, snapDistance);
            return false;
        }

        List<Vector3> points = [snappedStart];
        for (int i = 1; i + 1 < trianglePath.Length; i++)
        {
            points.Add(_nodes[trianglePath[i]].Triangle.Center);
        }

        points.Add(snappedEnd);
        path = new RuntimeNavigationPath(points, ComputePathLength(points));
        _lastDebugInfo = BuildDebugInfo("path_found", start, end, startIndex, endIndex, snappedStart, snappedEnd, snapDistance);
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
            Bake(_maxSlopeDegrees, _maxStepHeight, _maxStepHorizontalDistance);
        }
    }

    private int BuildAdjacency(float maxStepHeight, float maxStepHorizontalDistance)
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

        edgeCount += BuildStepAdjacency(maxStepHeight, maxStepHorizontalDistance);
        AssignConnectedComponents();
        return edgeCount;
    }

    private void AssignConnectedComponents()
    {
        int componentId = 0;
        for (int i = 0; i < _nodes.Count; i++)
        {
            _nodes[i].ComponentId = -1;
            _nodes[i].ComponentSize = 0;
        }

        Queue<int> queue = new();
        List<int> componentNodes = [];
        for (int i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i].ComponentId >= 0)
            {
                continue;
            }

            componentNodes.Clear();
            _nodes[i].ComponentId = componentId;
            queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                componentNodes.Add(current);
                foreach (int neighbor in _nodes[current].Neighbors)
                {
                    if (_nodes[neighbor].ComponentId >= 0)
                    {
                        continue;
                    }

                    _nodes[neighbor].ComponentId = componentId;
                    queue.Enqueue(neighbor);
                }
            }

            int componentSize = componentNodes.Count;
            foreach (int nodeIndex in componentNodes)
            {
                _nodes[nodeIndex].ComponentSize = componentSize;
            }

            componentId++;
        }
    }

    private int BuildStepAdjacency(float maxStepHeight, float maxStepHorizontalDistance)
    {
        if (maxStepHeight <= 0.0f || maxStepHorizontalDistance <= 0.0f || _nodes.Count < 2)
        {
            return 0;
        }

        float cellSize = Math.Max(maxStepHorizontalDistance, 0.001f);
        Dictionary<GridKey, List<int>> buckets = BuildStepBuckets(cellSize);
        float maxHorizontalDistanceSquared = maxStepHorizontalDistance * maxStepHorizontalDistance;
        int edgeCount = 0;
        for (int i = 0; i < _nodes.Count; i++)
        {
            RuntimeNavNode a = _nodes[i];
            HashSet<int> candidates = [];
            AddStepCandidates(buckets, candidates, a.Triangle, cellSize, maxStepHorizontalDistance);

            foreach (int j in candidates)
            {
                if (j <= i)
                {
                    continue;
                }

                RuntimeNavNode b = _nodes[j];
                if (a.Neighbors.Contains(j))
                {
                    continue;
                }

                if (MathF.Abs(a.Triangle.Center.Y - b.Triangle.Center.Y) > maxStepHeight)
                {
                    continue;
                }

                if (MinHorizontalDistanceSquared(a.Triangle, b.Triangle) > maxHorizontalDistanceSquared)
                {
                    continue;
                }

                a.Neighbors.Add(j);
                b.Neighbors.Add(i);
                edgeCount++;
            }
        }

        return edgeCount;
    }

    private Dictionary<GridKey, List<int>> BuildStepBuckets(float cellSize)
    {
        Dictionary<GridKey, List<int>> buckets = [];
        for (int i = 0; i < _nodes.Count; i++)
        {
            AddTriangleBuckets(buckets, _nodes[i].Triangle, i, cellSize, padding: 0.0f);
        }

        return buckets;
    }

    private static void AddStepCandidates(
        Dictionary<GridKey, List<int>> buckets,
        HashSet<int> candidates,
        RuntimeMeshTriangle triangle,
        float cellSize,
        float padding)
    {
        foreach (GridKey key in EnumerateTriangleGridKeys(triangle, cellSize, padding))
        {
            if (buckets.TryGetValue(key, out List<int>? bucket))
            {
                candidates.UnionWith(bucket);
            }
        }
    }

    private static void AddTriangleBuckets(
        Dictionary<GridKey, List<int>> buckets,
        RuntimeMeshTriangle triangle,
        int nodeIndex,
        float cellSize,
        float padding)
    {
        foreach (GridKey key in EnumerateTriangleGridKeys(triangle, cellSize, padding))
        {
            AddStepBucket(buckets, key, nodeIndex);
        }
    }

    private static IEnumerable<GridKey> EnumerateTriangleGridKeys(RuntimeMeshTriangle triangle, float cellSize, float padding)
    {
        float minX = MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X)) - padding;
        float maxX = MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X)) + padding;
        float minZ = MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z)) - padding;
        float maxZ = MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z)) + padding;
        int minCellX = (int)MathF.Floor(minX / cellSize);
        int maxCellX = (int)MathF.Floor(maxX / cellSize);
        int minCellZ = (int)MathF.Floor(minZ / cellSize);
        int maxCellZ = (int)MathF.Floor(maxZ / cellSize);

        for (int x = minCellX; x <= maxCellX; x++)
        {
            for (int z = minCellZ; z <= maxCellZ; z++)
            {
                yield return new GridKey(x, z);
            }
        }
    }

    private static void AddStepBucket(Dictionary<GridKey, List<int>> buckets, GridKey key, int nodeIndex)
    {
        if (!buckets.TryGetValue(key, out List<int>? bucket))
        {
            bucket = [];
            buckets[key] = bucket;
        }

        if (bucket.Count == 0 || bucket[^1] != nodeIndex)
        {
            bucket.Add(nodeIndex);
        }
    }

    private bool TryFindNearestTriangle(Vector3 position, float maxDistance, out int nodeIndex, out Vector3 nearest)
    {
        nodeIndex = -1;
        nearest = default;
        List<RuntimeNavCandidate> candidates = FindNearestTriangles(position, maxDistance, maxCandidates: 1);
        if (candidates.Count == 0)
        {
            return false;
        }

        RuntimeNavCandidate candidate = candidates[0];
        nodeIndex = candidate.NodeIndex;
        nearest = candidate.Point;
        return true;
    }

    private bool TryFindPathEndpoints(
        Vector3 start,
        Vector3 end,
        float maxDistance,
        out int startIndex,
        out Vector3 snappedStart,
        out int endIndex,
        out Vector3 snappedEnd)
    {
        startIndex = -1;
        endIndex = -1;
        snappedStart = default;
        snappedEnd = default;

        List<RuntimeNavCandidate> startCandidates = FindNearestTriangles(start, maxDistance, maxCandidates: 128);
        List<RuntimeNavCandidate> endCandidates = FindNearestTriangles(end, maxDistance, maxCandidates: 128);
        if (startCandidates.Count == 0 || endCandidates.Count == 0)
        {
            _lastDebugInfo = new RuntimeNavigationDebugInfo(
                startCandidates.Count == 0 ? "no_start_candidate" : "no_end_candidate",
                startCandidates.Count,
                endCandidates.Count,
                -1,
                -1,
                0,
                0,
                0.0f,
                0.0f);
            return false;
        }

        float bestScore = float.MaxValue;
        RuntimeNavCandidate bestStart = default;
        RuntimeNavCandidate bestEnd = default;
        bool found = false;
        foreach (RuntimeNavCandidate startCandidate in startCandidates)
        {
            RuntimeNavNode startNode = _nodes[startCandidate.NodeIndex];
            foreach (RuntimeNavCandidate endCandidate in endCandidates)
            {
                RuntimeNavNode endNode = _nodes[endCandidate.NodeIndex];
                if (startNode.ComponentId != endNode.ComponentId)
                {
                    continue;
                }

                float pathHint = Vector3.DistanceSquared(startNode.Triangle.Center, endNode.Triangle.Center);
                float componentPenalty = startNode.ComponentSize <= 1 && startCandidate.NodeIndex != endCandidate.NodeIndex
                    ? 1000.0f
                    : 0.0f;
                float score = startCandidate.DistanceSquared
                    + endCandidate.DistanceSquared
                    + (pathHint * 0.0001f)
                    + componentPenalty;
                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestStart = startCandidate;
                bestEnd = endCandidate;
                found = true;
            }
        }

        if (!found)
        {
            RuntimeNavCandidate nearestStart = startCandidates[0];
            RuntimeNavCandidate nearestEnd = endCandidates[0];
            RuntimeNavNode startNode = _nodes[nearestStart.NodeIndex];
            RuntimeNavNode endNode = _nodes[nearestEnd.NodeIndex];
            _lastDebugInfo = new RuntimeNavigationDebugInfo(
                "no_shared_component",
                startCandidates.Count,
                endCandidates.Count,
                startNode.ComponentId,
                endNode.ComponentId,
                startNode.ComponentSize,
                endNode.ComponentSize,
                MathF.Sqrt(nearestStart.DistanceSquared),
                MathF.Sqrt(nearestEnd.DistanceSquared));
            return false;
        }

        startIndex = bestStart.NodeIndex;
        snappedStart = bestStart.Point;
        endIndex = bestEnd.NodeIndex;
        snappedEnd = bestEnd.Point;
        return true;
    }

    private RuntimeNavigationDebugInfo BuildDebugInfo(
        string status,
        Vector3 start,
        Vector3 end,
        int startIndex,
        int endIndex,
        Vector3 snappedStart,
        Vector3 snappedEnd,
        float maxDistance)
    {
        int startCandidateCount = FindNearestTriangles(start, maxDistance, maxCandidates: 128).Count;
        int endCandidateCount = FindNearestTriangles(end, maxDistance, maxCandidates: 128).Count;
        RuntimeNavNode startNode = _nodes[startIndex];
        RuntimeNavNode endNode = _nodes[endIndex];
        return new RuntimeNavigationDebugInfo(
            status,
            startCandidateCount,
            endCandidateCount,
            startNode.ComponentId,
            endNode.ComponentId,
            startNode.ComponentSize,
            endNode.ComponentSize,
            Vector3.Distance(start, snappedStart),
            Vector3.Distance(end, snappedEnd));
    }

    private List<RuntimeNavCandidate> FindNearestTriangles(Vector3 position, float maxDistance, int maxCandidates)
    {
        float maxDistanceSquared = maxDistance * maxDistance;
        List<RuntimeNavCandidate> candidates = [];

        for (int i = 0; i < _nodes.Count; i++)
        {
            Vector3 point = ClosestPointOnTriangle(position, _nodes[i].Triangle);
            float distanceSquared = Vector3.DistanceSquared(position, point);
            if (distanceSquared > maxDistanceSquared)
            {
                continue;
            }

            candidates.Add(new RuntimeNavCandidate(i, point, distanceSquared));
        }

        return candidates
            .OrderBy(candidate => candidate.DistanceSquared)
            .Take(Math.Max(maxCandidates, 1))
            .ToList();
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

    private static float MinHorizontalDistanceSquared(RuntimeMeshTriangle a, RuntimeMeshTriangle b)
    {
        Vector2 a0 = ToXZ(a.A);
        Vector2 a1 = ToXZ(a.B);
        Vector2 a2 = ToXZ(a.C);
        Vector2 b0 = ToXZ(b.A);
        Vector2 b1 = ToXZ(b.B);
        Vector2 b2 = ToXZ(b.C);
        if (TrianglesOverlap2D(a0, a1, a2, b0, b1, b2))
        {
            return 0.0f;
        }

        float best = float.MaxValue;
        best = MathF.Min(best, PointToTriangleEdgesDistanceSquared(a0, b0, b1, b2));
        best = MathF.Min(best, PointToTriangleEdgesDistanceSquared(a1, b0, b1, b2));
        best = MathF.Min(best, PointToTriangleEdgesDistanceSquared(a2, b0, b1, b2));
        best = MathF.Min(best, PointToTriangleEdgesDistanceSquared(b0, a0, a1, a2));
        best = MathF.Min(best, PointToTriangleEdgesDistanceSquared(b1, a0, a1, a2));
        best = MathF.Min(best, PointToTriangleEdgesDistanceSquared(b2, a0, a1, a2));
        return best;
    }

    private static Vector2 ToXZ(Vector3 value)
    {
        return new Vector2(value.X, value.Z);
    }

    private static bool TrianglesOverlap2D(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 b0, Vector2 b1, Vector2 b2)
    {
        return PointInTriangle2D(a0, b0, b1, b2)
            || PointInTriangle2D(a1, b0, b1, b2)
            || PointInTriangle2D(a2, b0, b1, b2)
            || PointInTriangle2D(b0, a0, a1, a2)
            || PointInTriangle2D(b1, a0, a1, a2)
            || PointInTriangle2D(b2, a0, a1, a2)
            || SegmentsIntersect2D(a0, a1, b0, b1)
            || SegmentsIntersect2D(a0, a1, b1, b2)
            || SegmentsIntersect2D(a0, a1, b2, b0)
            || SegmentsIntersect2D(a1, a2, b0, b1)
            || SegmentsIntersect2D(a1, a2, b1, b2)
            || SegmentsIntersect2D(a1, a2, b2, b0)
            || SegmentsIntersect2D(a2, a0, b0, b1)
            || SegmentsIntersect2D(a2, a0, b1, b2)
            || SegmentsIntersect2D(a2, a0, b2, b0);
    }

    private static float PointToTriangleEdgesDistanceSquared(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float best = PointToSegmentDistanceSquared(point, a, b);
        best = MathF.Min(best, PointToSegmentDistanceSquared(point, b, c));
        best = MathF.Min(best, PointToSegmentDistanceSquared(point, c, a));
        return best;
    }

    private static float PointToSegmentDistanceSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared <= 0.000001f)
        {
            return Vector2.DistanceSquared(point, a);
        }

        float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0.0f, 1.0f);
        Vector2 closest = a + (ab * t);
        return Vector2.DistanceSquared(point, closest);
    }

    private static bool PointInTriangle2D(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        const float epsilon = 0.00001f;
        float d1 = Sign2D(point, a, b);
        float d2 = Sign2D(point, b, c);
        float d3 = Sign2D(point, c, a);
        bool hasNegative = d1 < -epsilon || d2 < -epsilon || d3 < -epsilon;
        bool hasPositive = d1 > epsilon || d2 > epsilon || d3 > epsilon;
        return !(hasNegative && hasPositive);
    }

    private static bool SegmentsIntersect2D(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        const float epsilon = 0.00001f;
        float o1 = Orientation2D(a, b, c);
        float o2 = Orientation2D(a, b, d);
        float o3 = Orientation2D(c, d, a);
        float o4 = Orientation2D(c, d, b);

        if (((o1 > epsilon && o2 < -epsilon) || (o1 < -epsilon && o2 > epsilon))
            && ((o3 > epsilon && o4 < -epsilon) || (o3 < -epsilon && o4 > epsilon)))
        {
            return true;
        }

        return MathF.Abs(o1) <= epsilon && PointOnSegment2D(c, a, b)
            || MathF.Abs(o2) <= epsilon && PointOnSegment2D(d, a, b)
            || MathF.Abs(o3) <= epsilon && PointOnSegment2D(a, c, d)
            || MathF.Abs(o4) <= epsilon && PointOnSegment2D(b, c, d);
    }

    private static float Orientation2D(Vector2 a, Vector2 b, Vector2 c)
    {
        return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
    }

    private static float Sign2D(Vector2 p, Vector2 a, Vector2 b)
    {
        return ((p.X - b.X) * (a.Y - b.Y)) - ((a.X - b.X) * (p.Y - b.Y));
    }

    private static bool PointOnSegment2D(Vector2 point, Vector2 a, Vector2 b)
    {
        const float epsilon = 0.00001f;
        return point.X >= MathF.Min(a.X, b.X) - epsilon
            && point.X <= MathF.Max(a.X, b.X) + epsilon
            && point.Y >= MathF.Min(a.Y, b.Y) - epsilon
            && point.Y <= MathF.Max(a.Y, b.Y) + epsilon;
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

        public int ComponentId { get; set; } = -1;

        public int ComponentSize { get; set; }
    }

    private readonly record struct RuntimeNavCandidate(int NodeIndex, Vector3 Point, float DistanceSquared);

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

    private readonly record struct GridKey(int X, int Z)
    {
        public static GridKey Create(Vector3 value, float cellSize)
        {
            return new GridKey(
                (int)MathF.Floor(value.X / cellSize),
                (int)MathF.Floor(value.Z / cellSize));
        }
    }
}
