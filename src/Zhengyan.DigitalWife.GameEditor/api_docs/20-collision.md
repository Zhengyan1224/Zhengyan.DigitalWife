---
id: collision
title: Collision / Collider API
category: 物理
objects:
  - RuntimeCollider
  - RuntimeRaycastHit
  - MeshCollider
keywords:
  - collision
  - collider
  - capsule
  - box
  - mesh
  - navmesh
---

# Collision / Collider API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Collision / Collider API |
| 分类 | 物理 |
| 主要对象 | ``RuntimeCollider``, ``RuntimeRaycastHit`` |
| C# 入口 | `Entity.AddCapsuleCollider/AddBoxCollider/AddMeshCollider` |
| Python 入口 | `entity.add_capsule_collider/add_box_collider/add_mesh_collider` |
| 说明 | 碰撞体创建、查询、射线检测、触发区、MeshCollider 和简单碰撞判断。 |

## API 内容

每个实体都可以绑定多个 Collider。Collider 相对于绑定对象本地坐标，实体移动、旋转、缩放时 Collider 会跟随变换。

支持形状：

- `capsule`：胶囊体。
- `box`：有向盒体。
- `mesh`：使用实体自身网格三角面。当前支持 PMX 模型静态网格和 TexturedPlane 网格，适合精确射线、贴地采样和 NavMesh 烘焙。

选择建议：

| Collider | 适合用途 | 不适合用途 |
| --- | --- | --- |
| `capsule` | 人物身体、NPC 身体、圆柱形触发范围。 | 精确贴合复杂模型表面。 |
| `box` | 地板、墙体、门、按钮、矩形触发区。 | 弯曲地形、复杂模型点选。 |
| `mesh` | 导入场景地面、复杂模型点选、贴地采样、NavMesh。 | 高频实体间碰撞、动态骨骼变形碰撞、水面交互。 |

MeshCollider 说明：

- GameEditor 中可在实体 `Colliders` 面板点击 `Add Mesh Collider` 添加。
- `Walkable for NavMesh` 决定该 MeshCollider 是否参与 `Scene.Navigation.Bake(...)`。
- `Max slope degrees` 用于过滤可行走三角面，坡度超过该值的面不会进入 NavMesh。
- MeshCollider 会参与 C# `Scene.Physics.Raycast(...)`、`Scene.Physics.SampleGround(...)`、`Scene.Camera.RaycastEntity(...)` 和右键菜单对象拾取。
- MeshCollider 当前不参与 `CheckCollision(...)` / `DistanceToCollider(...)` 的实体间简单碰撞，也不触发水面交互；需要这些功能时仍使用 `box` / `capsule`。
- Python 快照层的 `scene.physics` 当前会忽略 `mesh`，需要 mesh 精确贴地或 NavMesh 时建议使用 C# `.csx` 脚本。

MeshCollider 配置步骤：

1. 选中拥有网格的实体，例如 PMX 场景模型或 TexturedPlane。
2. 在 `Colliders` 面板点击 `Add Mesh Collider`。
3. 如果只用于射线命中或贴地，保持 `Walkable for NavMesh` 关闭也可以。
4. 如果要让人物自动寻路，勾选 `Walkable for NavMesh`。
5. 调整 `Max slope degrees`。常见地面用 `45` 到 `55`，需要允许陡坡时提高。
6. 如果模型显示和 Collider 不重合，再调整 `Center`、`Rotation`、`Scale`。

`AddMeshCollider` 参数：

| C# 参数 | Python 参数 | 说明 |
| --- | --- | --- |
| `name` | `name` | Collider 名称。 |
| `walkable` | `walkable` | 是否参与 NavMesh 烘焙。 |
| `maxSlopeDegrees` | `max_slope_degrees` | 最大可行走坡度，范围会被限制在 `0` 到 `89.9`。值为 `0` 时运行时会使用 `Scene.Navigation.Bake(...)` 的全局坡度。 |
| `offsetX/Y/Z` | `offset_x/y/z` | MeshCollider 本地位置偏移。 |
| `scaleX/Y/Z` | `scale_x/y/z` | MeshCollider 本地缩放，最小值会限制到 `0.001`。 |
| `rotationX/Y/Z` | `rotation_x/y/z` | MeshCollider 本地旋转，单位是角度。 |

注意：MeshCollider 使用“实体自身”的网格。给没有网格数据的空对象添加 MeshCollider 不会产生可用三角面；这种情况应改用 `Box Collider` / `Capsule Collider`，或把 MeshCollider 加到真正的场景模型实体上。

C#：

```csharp
if (IsStart)
{
    Entity.ClearColliders();

    Entity.AddCapsuleCollider(
        name: "Body",
        radius: 0.35f,
        height: 1.7f,
        centerX: 0.0f,
        centerY: 0.85f,
        centerZ: 0.0f,
        axis: "y");

    Entity.AddBoxCollider(
        name: "PickupRange",
        sizeX: 1.2f,
        sizeY: 1.0f,
        sizeZ: 1.2f,
        centerX: 0.0f,
        centerY: 0.5f,
        centerZ: 0.0f);

    // 如果当前实体本身是 PMX 场景模型或 TexturedPlane，可以让它作为可行走网格。
    Entity.AddMeshCollider(
        name: "WalkableMesh",
        walkable: true,
        maxSlopeDegrees: 55.0f);
}

if (IsUpdate)
{
    RuntimeEntity? enemy = Scene.GetEntity("Enemy");
    if (enemy is not null && Entity.CheckCollision(enemy))
    {
        Console.WriteLine("colliding");
    }
}
```

Python：

```python
def start(entity, scene, input, audio):
    entity.clear_colliders()
    entity.add_capsule_collider(
        name="Body",
        radius=0.35,
        height=1.7,
        center_y=0.85,
        axis="y")
    entity.add_box_collider(
        name="PickupRange",
        size_x=1.2,
        size_y=1.0,
        size_z=1.2,
        center_y=0.5)

    entity.add_mesh_collider(
        name="WalkableMesh",
        walkable=True,
        max_slope_degrees=55.0)

def update(entity, scene, input, audio, delta_seconds):
    enemy = scene.get_entity("Enemy")
    if enemy is not None and entity.check_collision(enemy):
        print("colliding")
```

API 列表：

| C# | Python | 说明 |
| --- | --- | --- |
| `SetCapsuleCollider(...)` | `set_capsule_collider(...)` | 清空并设置单个胶囊 Collider。 |
| `AddCapsuleCollider(...)` | `add_capsule_collider(...)` | 增加胶囊 Collider。 |
| `AddBoxCollider(...)` | `add_box_collider(...)` | 增加盒体 Collider。 |
| `AddMeshCollider(name, walkable, maxSlopeDegrees, ...)` | `add_mesh_collider(...)` | 增加 MeshCollider，使用实体自身网格三角面。 |
| `RemoveCollider(idOrName)` | `remove_collider(id_or_name)` | 删除指定 Collider。 |
| `ClearColliders()` | `clear_colliders()` | 清空所有 Collider。 |
| `DisableCollider()` | `disable_collider()` | 禁用所有 Collider。 |
| `Raycast(ray, out distance, out point)` | `raycast(ray)` | 检测射线与本实体 Collider。 |
| `CheckCollision(other)` | `check_collision(other)` | 检测两个实体 Collider 是否相交。 |
| `DistanceToCollider(other)` | `distance_to_collider(other)` | 计算 Collider 间距离。 |
| `TryGetCapsule(out capsule)` | `capsule()` | 获取第一个胶囊 Collider，通常用于兼容旧逻辑。 |

水面交互：

- 在水面对象上开启 `Enable water interaction` 后，GamePlayer 会检测其它实体 Collider 与水面的接触。
- 对粒子系统，如果 `EnableWaterInteraction` 开启，则会按每个活跃粒子的当前位置和当前显示尺寸做近似球检测，不再依赖实体级 Collider。
- 水面对象的 `ParticleRippleMinIntervalSeconds` 和 `ParticleRippleMergeDistance` 控制粒子触水波纹的密度与合并粒度。
- 接触时生成视觉波纹。
- 该功能不提供浮力、刚体推挤或动力学响应。
