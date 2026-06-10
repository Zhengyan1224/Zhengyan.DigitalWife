---
id: navmesh
title: NavMesh API
category: 物理
objects:
  - RuntimeSceneNavigation
  - RuntimeNavigationBakeResult
  - RuntimeNavigationPath
keywords:
  - navmesh
  - navigation
  - pathfinding
  - mesh collider
  - walkable
---

# NavMesh API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | NavMesh API |
| 分类 | 物理 |
| 主要对象 | ``RuntimeSceneNavigation``, ``RuntimeNavigationBakeResult``, ``RuntimeNavigationPath`` |
| C# 入口 | `Scene.Navigation` |
| Python 入口 | 暂无 |
| 说明 | 从可行走 MeshCollider 烘焙导航三角图，并在 C# 脚本中查询路径。 |

## API 内容

`Scene.Navigation` 是轻量级导航查询入口。当前实现不会做 Recast/Unity 那种体素化烘焙，而是直接从标记为 `Walkable for NavMesh` 的 MeshCollider 三角面构建邻接图，然后用 A* 返回路径点。它适合第一版“导入场景模型、给地面加 MeshCollider、脚本控制人物走动并贴地”的需求。

快速流程：

1. 在 GameEditor 中选中场景模型实体。
2. 在 `Colliders` 面板点击 `Add Mesh Collider`。
3. 勾选 `Walkable for NavMesh`，设置 `Max slope degrees`。
4. 脚本中调用 `Scene.Navigation.Bake()`，再调用 `FindPath(...)` 或 `TryFindPath(...)`。
5. 角色每帧沿路径移动，并用 `Scene.Physics.SampleGround(...)` 修正 Y 高度。

### 什么时候用 MeshCollider，什么时候用 NavMesh

| 目标 | 需要配置 | 脚本侧 API |
| --- | --- | --- |
| 鼠标射线点选导入的复杂模型 | 给目标模型加 `Mesh Collider` | `Scene.Physics.Raycast(...)` |
| 人物 WASD 走路并贴合复杂地面 | 给地面模型加 `Mesh Collider` | `Scene.Physics.SampleGround(...)` |
| 点击某个地面目标点，人物自动绕路走过去 | 给地面模型加 `Mesh Collider`，并勾选 `Walkable for NavMesh` | `Scene.Navigation.Bake(...)` 和 `TryFindPath(...)` |
| 简单方块地板或触发区域 | 用 `Box Collider` 或 `Capsule Collider` 即可 | `CheckCollision(...)`、`SampleGround(...)` |

如果你只是想让人物沿 WASD 移动并贴地，不需要 NavMesh，只需要地面 Collider 和 `SampleGround`。如果你需要“从当前位置到目标点自动找路径”，才需要 NavMesh。

### GameEditor 配置步骤

1. 导入或选中作为地面的场景模型实体，例如教室、房间、地形、平台等。
2. 在右侧实体设置面板找到 `Colliders`。
3. 点击 `Add Mesh Collider`。
4. 如果这个 MeshCollider 只用于射线命中或贴地，不需要勾选 `Walkable for NavMesh`。
5. 如果这个 MeshCollider 要参与自动寻路，勾选 `Walkable for NavMesh`。
6. 设置 `Max slope degrees`，常用值是 `45` 到 `60`。值越大，越陡的坡也会被认为可以走。
7. 如果碰撞网格和模型显示位置有偏移，调整 MeshCollider 的本地 `Center`、`Rotation`、`Scale`。
8. 保存项目，GamePlayer 运行时会读取这些 Collider 设置。

### MeshCollider 字段说明

| 字段 | 说明 | 建议 |
| --- | --- | --- |
| `Name` | Collider 名称，用于调试和命中结果识别。 | 地面可命名为 `GroundMesh` 或 `WalkableMesh`。 |
| `Center X/Y/Z` | Collider 相对实体本地坐标的偏移。 | 模型显示和碰撞不重合时再调整。 |
| `Rotation X/Y/Z` | Collider 相对实体本地旋转。 | 一般保持 `0`。 |
| `Scale X/Y/Z` | Collider 相对实体本地缩放。 | 一般保持 `1`，除非模型单位不匹配。 |
| `Walkable for NavMesh` | 是否参与 `Scene.Navigation.Bake(...)`。 | 地面和平台勾选，墙壁、天花板、装饰物不要勾选。 |
| `Max slope degrees` | 单个 Collider 的最大可行走坡度。 | 平地 `35` 到 `55`，需要爬坡可提高。 |

### 脚本调用流程

1. 在 `IsStart` 中调用一次 `Scene.Navigation.Bake(...)`。
2. 输出 `TriangleCount` 和 `EdgeCount`，确认 NavMesh 是否真的被烘焙出来。
3. 鼠标点击时，用 `Scene.Camera.MousePointToRay(Input)` 生成射线。
4. 用 `Scene.Physics.Raycast(...)` 命中地面，得到目标世界坐标。
5. 用 `Scene.Navigation.TryFindPath(Entity.Position, hit.Point, out path)` 查询路径。
6. 每帧让人物朝当前路径点移动。
7. 每次移动后用 `Scene.Physics.SampleGround(...)` 修正 Y 坐标，让人物贴合地面。
8. 如果运行时移动了地面模型、添加了新的 MeshCollider 或修改了可行走设置，需要重新调用 `Bake(...)`。

### 关键参数

| 参数 | 位置 | 说明 |
| --- | --- | --- |
| `maxSlopeDegrees` | `Scene.Navigation.Bake(maxSlopeDegrees)` | 全局烘焙坡度上限。最终可行走坡度会同时受全局值和 Collider 自身 `Max slope degrees` 影响。 |
| `maxStepHeight` | `Scene.Navigation.Bake(maxStepHeight)` | 自动连接相邻台阶/平台的最大高度差。默认 `0.45`。场景缩放较小时可降低，楼梯较高时可提高。 |
| `maxStepHorizontalDistance` | `Scene.Navigation.Bake(maxStepHorizontalDistance)` | 自动连接相邻台阶/平台的最大水平间距。默认 `0.35`。如果台阶之间有缝隙或模型三角面没有共享边，可适当提高。 |
| `maxSnapDistance` | `TryFindPath(..., maxSnapDistance)` | 起点和终点吸附到最近 NavMesh 三角面的最大距离。人物脚底或鼠标命中点略高于地面时，可以适当增大。 |
| `originY` | `SampleGround(..., originY, maxDistance)` | 向下采样地面的射线起点高度。一般用 `Entity.Position.Y + 10.0f`。 |
| `maxDistance` | `SampleGround(..., maxDistance)` | 向下采样地面的最大距离。地形落差大时需要增大。 |
| `FootOffset` | 示例脚本常量 | 人物脚底和地面之间保留的微小高度，避免模型和地面 Z-fighting 或陷入地面。 |

### C# API

| 属性 / 方法 | 说明 |
| --- | --- |
| `Scene.Navigation.TriangleCount` | 当前已烘焙的可行走三角面数量。 |
| `Scene.Navigation.Bake(maxSlopeDegrees = 55.0f, maxStepHeight = 0.45f, maxStepHorizontalDistance = 0.35f)` | 从所有 walkable MeshCollider 重建导航图，返回 `RuntimeNavigationBakeResult`。除共享边外，也会为高度差和水平距离在阈值内的台阶/平台建立连接。 |
| `Scene.Navigation.FindPath(start, end, maxSnapDistance = 5.0f)` | 返回路径点列表；失败返回空列表。 |
| `Scene.Navigation.TryFindPath(start, end, out path, maxSnapDistance = 5.0f)` | 查询路径，成功时返回 `RuntimeNavigationPath`。 |
| `Scene.Navigation.SamplePosition(position, out nearest, maxDistance = 5.0f)` | 把一个世界坐标吸附到最近 NavMesh 三角面。 |

`RuntimeNavigationBakeResult` 字段：

| 字段 | 说明 |
| --- | --- |
| `TriangleCount` | 参与导航图的三角面数量。 |
| `EdgeCount` | 三角面共享边形成的邻接边数量。 |

`RuntimeNavigationPath` 字段：

| 字段 | 说明 |
| --- | --- |
| `Points` | 路径点列表，世界坐标。 |
| `Length` | 路径折线长度。 |
| `Success` | `Points.Count > 0` 的快捷判断。 |

### C#：点击目标点后沿 NavMesh 移动

下面示例假设脚本绑定在人物实体上，并且场景地面模型已经添加了可行走 MeshCollider。

```csharp
using System.Numerics;

static List<Vector3> path = [];
static int pathIndex = 0;
static bool baked = false;
static bool leftMouseWasDown = false;

const float MoveSpeed = 2.4f;
const float StopDistance = 0.06f;
const float FootOffset = 0.02f;

if (IsStart && !baked)
{
    RuntimeNavigationBakeResult bake = Scene.Navigation.Bake(
        maxSlopeDegrees: 55.0f,
        maxStepHeight: 0.45f,
        maxStepHorizontalDistance: 0.35f);
    Console.WriteLine($"NavMesh triangles={bake.TriangleCount}, edges={bake.EdgeCount}");
    baked = true;
}

if (IsUpdate)
{
    bool leftMouseDown = Input.IsMouseButtonDown("left");

    if (leftMouseDown && !leftMouseWasDown)
    {
        RuntimeRay ray = Scene.Camera.MousePointToRay(Input);
        if (Scene.Physics.Raycast(ray, out RuntimeRaycastHit hit, maxDistance: 200.0f, ignoredEntity: Entity))
        {
            if (Scene.Navigation.TryFindPath(
                Entity.Position,
                hit.Point,
                out RuntimeNavigationPath navPath,
                maxSnapDistance: 5.0f))
            {
                path = navPath.Points.ToList();
                pathIndex = path.Count > 1 ? 1 : 0;
                Console.WriteLine($"Path points={path.Count}, length={navPath.Length:0.00}");
            }
            else
            {
                path.Clear();
                pathIndex = 0;
                Console.WriteLine("No NavMesh path found. Check walkable MeshCollider and maxSnapDistance.");
            }
        }
    }

    leftMouseWasDown = leftMouseDown;
}

if (IsUpdate && pathIndex < path.Count)
{
    Vector3 current = Entity.Position;
    Vector3 target = path[pathIndex];
    Vector3 flatDelta = new(target.X - current.X, 0.0f, target.Z - current.Z);

    if (flatDelta.Length() <= StopDistance)
    {
        pathIndex++;
    }
    else
    {
        Vector3 direction = Vector3.Normalize(flatDelta);
        Vector3 next = current + (direction * MoveSpeed * (float)DeltaSeconds);

        if (Scene.Physics.SampleGround(
            next.X,
            next.Z,
            out RuntimeRaycastHit ground,
            originY: current.Y + 10.0f,
            maxDistance: 30.0f,
            ignoredEntity: Entity))
        {
            next.Y = ground.Point.Y + FootOffset;
        }

        Entity.SetPosition(next.X, next.Y, next.Z);
        Entity.LookAt(next.X + direction.X, next.Y, next.Z + direction.Z);
    }
}
```

### 只需要贴地移动，不需要 NavMesh 的写法

下面示例适合“玩家用 WASD 直接控制人物在导入场景里走动”的情况。地面需要有 `Box Collider`、`Capsule Collider` 或 `Mesh Collider`，但 MeshCollider 不一定要勾选 `Walkable for NavMesh`。

```csharp
using System.Numerics;

const float MoveSpeed = 2.6f;
const float FootOffset = 0.02f;

if (IsUpdate)
{
    Vector3 move = Vector3.Zero;
    if (Input.IsKeyDown("W")) move.Z -= 1.0f;
    if (Input.IsKeyDown("S")) move.Z += 1.0f;
    if (Input.IsKeyDown("A")) move.X -= 1.0f;
    if (Input.IsKeyDown("D")) move.X += 1.0f;

    if (move.LengthSquared() <= 0.0001f)
    {
        return;
    }

    move = Vector3.Normalize(move);
    Vector3 next = Entity.Position + (move * MoveSpeed * (float)DeltaSeconds);

    if (Scene.Physics.SampleGround(
        next.X,
        next.Z,
        out RuntimeRaycastHit ground,
        originY: Entity.Position.Y + 10.0f,
        maxDistance: 30.0f,
        ignoredEntity: Entity))
    {
        next.Y = ground.Point.Y + FootOffset;
    }

    Entity.SetPosition(next.X, next.Y, next.Z);
    Entity.LookAt(next.X + move.X, next.Y, next.Z + move.Z);
}
```

### 调试与排查

| 现象 | 可能原因 | 处理方式 |
| --- | --- | --- |
| `TriangleCount=0` | 没有任何 MeshCollider 勾选 `Walkable for NavMesh`。 | 在 GameEditor 中给地面添加 `Mesh Collider` 并勾选 `Walkable for NavMesh`。 |
| `TriangleCount=0` | 坡度过滤太严格。 | 提高 Collider 的 `Max slope degrees`，或提高 `Scene.Navigation.Bake(maxSlopeDegrees)`。 |
| 鼠标点击有命中，但找不到路径 | 起点或终点离 NavMesh 太远。 | 增大 `TryFindPath(..., maxSnapDistance)`，或确认人物脚底在可行走区域附近。 |
| 路径断开 | 台阶/平台高度差或水平间距超过 `Bake(...)` 的台阶连接阈值。 | 适当提高 `maxStepHeight` 或 `maxStepHorizontalDistance`，或添加一个简化的隐藏导航面。 |
| 人物移动时上下抖动 | `SampleGround` 命中了人物自身 Collider，或地面网格有重叠面。 | 传入 `ignoredEntity: Entity`，并检查场景模型是否有重复地面。 |
| 人物陷入地面 | `FootOffset` 太小，或模型脚底原点不在脚底。 | 增大 `FootOffset`，或调整人物实体原点/脚本高度偏移。 |
| 人物走到墙边或桌子下 | 当前 NavMesh 不做角色半径膨胀和动态避障。 | 用更简单的导航专用地面网格，预留角色宽度。 |

### 建议的工程做法

复杂视觉模型通常三角面很多，而且可能包含墙壁、桌椅、装饰物、天花板。直接把整个场景模型都作为可行走 MeshCollider，可能会导致烘焙慢、路径不稳定或误把不该走的面当成地面。

更稳妥的做法是在建模阶段额外准备一个“导航专用地面网格”：只保留玩家能走的地面、坡道和平台，删除墙壁、桌椅、天花板等装饰面。这个导航网格可以在 GameEditor 里设为透明或隐藏显示，但仍然保留 MeshCollider 并勾选 `Walkable for NavMesh`。

性能建议：

- 静态场景只在 `IsStart` 调用一次 `Scene.Navigation.Bake(...)`。
- 不要每帧调用 `Bake(...)`。
- 如果场景很大，优先使用简化导航网格，而不是完整高模场景网格。
- 如果只是贴地，不要启用 NavMesh，直接使用 `SampleGround`。
- 如果只需要普通触发范围或简单障碍，优先使用 `Box Collider` / `Capsule Collider`。

### 当前边界

- NavMesh 只使用 `Shape=mesh` 且 `Walkable=true` 的 Collider。
- PMX MeshCollider 使用模型静态顶点；不会把骨骼动画后的实时变形写入导航图。
- 当前路径点是三角图路径点，不做角色半径膨胀、障碍物避让或动态障碍。
- 如果导入场景模型的地面三角面没有共享边，NavMesh 会尝试按 `maxStepHeight` 和 `maxStepHorizontalDistance` 建立台阶连接；超过阈值的断面仍然需要在建模阶段保证连续，或用额外平面/简化地面网格作为导航面。
- Python 暂未提供同步 NavMesh 查询，因为 Python 脚本桥接是命令/快照模型，不适合每帧同步返回大网格查询结果。
