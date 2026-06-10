---
id: physics-grounding
title: Physics / Grounding API
category: 物理
objects:
  - RuntimeScenePhysics
  - RuntimeRaycastHit
  - scene.physics
keywords:
  - physics
  - ground
  - grounding
  - raycast
  - movement
  - mesh
  - navmesh
---

# Physics / Grounding API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Physics / Grounding API |
| 分类 | 物理 |
| 主要对象 | ``RuntimeScenePhysics``, ``RuntimeRaycastHit``, ``scene.physics`` |
| C# 入口 | `Scene.Physics.Raycast`, `Scene.Physics.SampleGround` |
| Python 入口 | `scene.physics.raycast`, `scene.physics.sample_ground` |
| 说明 | 场景级 Collider 射线检测、地面采样和人物贴地移动示例。 |

## API 内容

`Scene.Physics` / `scene.physics` 用于做场景级物理查询。当前实现基于实体 Collider，不是完整刚体物理，也不会自动推动角色；脚本仍然负责移动实体，只是在移动后用 `SampleGround` 修正高度。

适合场景：

- 人物在导入场景中按脚本移动，并贴合地面高度。
- 鼠标点击、相机射线或脚本射线命中任意带 Collider 的实体。
- 检测地板、坡道、平台等用 Box / Capsule / Mesh Collider 表示的地面。

贴地移动的核心逻辑是：脚本先计算人物下一帧的 X/Z 位置，然后从这个 X/Z 的上方向下打一条射线，找到地面命中点，最后把人物 Y 坐标设置为地面 Y 加上脚底偏移。它不会替你计算路径，也不会替你避开障碍；如果需要“自动从 A 点走到 B 点”，请配合 [NavMesh API](29-navmesh.md) 使用。

GameEditor 配置步骤：

1. 给地面、平台或场景模型添加 Collider。
2. 简单平地优先用 `Box Collider`，复杂导入场景优先用 `Mesh Collider`。
3. 人物自身如果有 Collider，脚本调用 `SampleGround` 时要传 `ignoredEntity: Entity`。
4. 如果地面由多个模型组成，每个会被采样的模型都需要 Collider。
5. 如果只是贴地移动，MeshCollider 不需要勾选 `Walkable for NavMesh`。
6. 如果同时要自动寻路，MeshCollider 需要勾选 `Walkable for NavMesh`，并在脚本中调用 `Scene.Navigation.Bake(...)`。

限制：

- MeshCollider 支持 PMX 静态网格和 TexturedPlane 网格逐三角面射线检测；复杂 PMX 模型每次首次命中会建立缓存，后续同一变换下会复用。
- 胶囊 Collider 射线命中是轻量近似，适合拾取和简单检测；需要精确贴合复杂网格时应使用 MeshCollider。
- `SampleGround` 默认从 `Y=1000` 向下打射线，地面 Collider 必须位于射线路径内。
- Python `scene.physics` 仍基于快照 box/capsule 查询，当前不做 PMX mesh 三角面查询；MeshCollider 精确贴地建议使用 C# 脚本。

### C# API

| 方法 | 说明 |
| --- | --- |
| `Scene.Physics.Raycast(ray, out hit, maxDistance)` | 对场景中全部 Collider 做射线检测，返回最近命中。 |
| `Scene.Physics.Raycast(ray, out hit, maxDistance, ignoredEntity)` | 射线检测时忽略指定实体，常用于避免命中角色自身 Collider。 |
| `Scene.Physics.Raycast(ray, out hit, maxDistance, ignoredEntity, entityType)` | 额外按实体类型过滤，例如只检测 `empty_object` / `textured_plane`。 |
| `Scene.Physics.SampleGround(x, z, out hit)` | 从默认高度向下采样地面。 |
| `Scene.Physics.SampleGround(x, z, out hit, originY, maxDistance)` | 指定采样起点高度和最大距离。 |
| `Scene.Physics.SampleGround(x, z, out hit, originY, maxDistance, ignoredEntity)` | 采样时忽略指定实体。 |
| `Scene.Physics.SampleGround(x, z, out hit, originY, maxDistance, ignoredEntity, entityType)` | 采样时忽略实体并按实体类型过滤。 |

`SampleGround` 参数建议：

| 参数 | 建议值 | 说明 |
| --- | --- | --- |
| `x` / `z` | 人物下一帧位置的 X/Z。 | 不要用旧位置，否则上下坡时高度会滞后。 |
| `originY` | `Entity.Position.Y + 10.0f`。 | 从人物上方向下采样，避免从地面下方开始。 |
| `maxDistance` | `20.0f` 到 `50.0f`。 | 场景高度差很大时增大。 |
| `ignoredEntity` | `Entity`。 | 避免射线命中人物自己的 Collider。 |
| `entityType` | 通常不传。 | 只想采样某类实体时再传，例如只采样 `textured_plane`。 |

`RuntimeRaycastHit` 字段：

| 字段 | 说明 |
| --- | --- |
| `Entity` | 命中的运行时实体。 |
| `ColliderId` / `ColliderName` | 命中的 Collider 标识。 |
| `ColliderShape` | `box`、`capsule` 或 `mesh`。 |
| `Distance` | 射线距离。 |
| `Point` | 命中点世界坐标。 |

### C#：脚本控制人物 WASD 贴地移动

假设：

- 人物脚本绑定在 PMX 人物实体上。
- 地面、平台或场景对象已经在 GameEditor 中添加了 Collider；复杂场景模型推荐添加 `Mesh Collider`。
- 人物自身有 Collider 时，采样地面要忽略 `Entity` 自己。

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

    if (move.LengthSquared() > 0.0001f)
    {
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
    }
}
```

### Python API

| 方法 | 说明 |
| --- | --- |
| `scene.physics.raycast(ray, max_distance=None, ignore_entity=None, entity_type=None)` | 对场景中全部 Collider 做射线检测，返回最近命中字典或 `None`。 |
| `scene.physics.sample_ground(x, z, origin_y=1000.0, max_distance=2000.0, ignore_entity=None, entity_type=None)` | 从上方向下采样地面，返回命中字典或 `None`。 |

Python 命中字典字段：

| 字段 | 说明 |
| --- | --- |
| `entity` | 命中的实体快照对象。 |
| `entityId` / `entityName` / `entityType` | 命中实体信息。 |
| `colliderId` / `colliderName` / `colliderShape` | 命中 Collider 信息。 |
| `distance` | 射线距离。 |
| `point` | 命中点 `[x, y, z]`。 |

### Python：脚本控制人物 WASD 贴地移动

```python
MOVE_SPEED = 2.6
FOOT_OFFSET = 0.02

def update(entity, scene, input, audio, delta_seconds):
    move_x = 0.0
    move_z = 0.0
    if input.is_key_down("W"):
        move_z -= 1.0
    if input.is_key_down("S"):
        move_z += 1.0
    if input.is_key_down("A"):
        move_x -= 1.0
    if input.is_key_down("D"):
        move_x += 1.0

    length = ((move_x * move_x) + (move_z * move_z)) ** 0.5
    if length <= 0.0001:
        return

    move_x /= length
    move_z /= length
    next_x = entity.position[0] + (move_x * MOVE_SPEED * delta_seconds)
    next_y = entity.position[1]
    next_z = entity.position[2] + (move_z * MOVE_SPEED * delta_seconds)

    ground = scene.physics.sample_ground(
        next_x,
        next_z,
        origin_y=entity.position[1] + 10.0,
        max_distance=30.0,
        ignore_entity=entity)
    if ground is not None:
        next_y = ground["point"][1] + FOOT_OFFSET

    entity.set_position(next_x, next_y, next_z)
```

### 常见问题

| 现象 | 可能原因 | 处理方式 |
| --- | --- | --- |
| 人物不贴地，Y 不变 | 地面没有 Collider，或 `maxDistance` 太小。 | 给地面添加 Collider，增大 `maxDistance`。 |
| 人物跳到自己身上 | `SampleGround` 命中了人物自身 Collider。 | C# 传 `ignoredEntity: Entity`，Python 传 `ignore_entity=entity`。 |
| 在复杂模型上高度不准 | 使用了 Box/Capsule 近似 Collider。 | 给场景模型添加 `Mesh Collider`。 |
| 人物在坡道上抖动 | 地面有重复面、模型网格不连续，或脚底偏移太小。 | 清理地面模型，增大 `FootOffset`，或使用简化导航/地面网格。 |
| Python 贴复杂 PMX 地面不准 | Python `scene.physics` 当前不做 PMX mesh 三角面查询。 | 使用 C# `.csx` 脚本做 MeshCollider 精确贴地。 |
