using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Input;

static List<Vector3> path = [];
static int pathIndex = 0;
static bool baked = false;
static PointerClickGesture click = new();

const float MoveSpeed = 2.4f;
const float StopDistance = 0.06f;
const float FootOffset = 0.02f;

if (IsStart)
{
    click.Reset();
    path.Clear();
    pathIndex = 0;
    if (!baked)
    {
        RuntimeNavigationBakeResult bake = Scene.Navigation.Bake(maxSlopeDegrees: 0.1f,
            maxStepHeight: 2.2f,
            maxStepHorizontalDistance: 0.35f);
        Console.WriteLine($"NavMesh triangles={bake.TriangleCount}, edges={bake.EdgeCount}");
        baked = true;
    }
}

if (IsUpdate && click.Update(
    Input.IsMouseButtonDown("left"), new Vector2(Input.MouseX, Input.MouseY),
    Input.Touches, Input.TouchTransitions, DeltaSeconds, out Vector2 screenPoint,
    movementThreshold: Input.TouchSlopPixels,
    isBlocked: point => Scene.IsScreenPointOverGui(point.X, point.Y)))
{
    RuntimeRay ray = Scene.Camera.ScreenPointToRay(screenPoint.X, screenPoint.Y);
    if (Scene.Physics.Raycast(ray, out RuntimeRaycastHit hit, maxDistance: 200.0f, ignoredEntity: Entity))
    {
        if (Scene.Navigation.TryFindPath(Entity.Position, hit.Point,
            out RuntimeNavigationPath navPath, maxSnapDistance: 5.0f))
        {
            path = navPath.Points.ToList();
            pathIndex = path.Count > 1 ? 1 : 0;
            Console.WriteLine($"Path points={path.Count}, length={navPath.Length:0.00}");
        }
        else
        {
            path.Clear();
            pathIndex = 0;
            RuntimeNavigationDebugInfo debug = Scene.Navigation.LastDebugInfo;
            Console.WriteLine(
                $"No NavMesh path found. status={debug.Status}, " +
                $"startCandidates={debug.StartCandidateCount}, endCandidates={debug.EndCandidateCount}, " +
                $"startComponent={debug.StartComponentId}/{debug.StartComponentSize}, " +
                $"endComponent={debug.EndComponentId}/{debug.EndComponentSize}, " +
                $"startSnap={debug.StartSnapDistance:0.00}, endSnap={debug.EndSnapDistance:0.00}");
        }
    }
}

if (IsUpdate && pathIndex < path.Count)
{
    Vector3 current = Entity.Position;
    Vector3 target = path[pathIndex];
    Vector3 flatDelta = new(target.X - current.X, 0.0f, target.Z - current.Z);
    float distance = flatDelta.Length();
    if (distance <= StopDistance)
    {
        pathIndex++;
    }
    else
    {
        Vector3 direction = flatDelta / distance;
        float step = Math.Min(distance, MoveSpeed * Math.Max((float)DeltaSeconds, 0.0f));
        Vector3 next = current + direction * step;
        if (Scene.Physics.SampleGround(next.X, next.Z, out RuntimeRaycastHit ground,
            originY: current.Y + 10.0f, maxDistance: 30.0f, ignoredEntity: Entity))
        {
            next.Y = ground.Point.Y + FootOffset;
        }
        Entity.SetPosition(next.X, next.Y, next.Z);
        Entity.LookAt(next.X + direction.X, next.Y, next.Z + direction.Z);
    }
}
