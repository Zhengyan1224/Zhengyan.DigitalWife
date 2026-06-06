---
id: collision
title: Collision / Collider API
category: 物理
objects:
  - RuntimeCollider
  - RuntimeRaycastHit
keywords:
  - collision
  - collider
  - capsule
  - box
---

# Collision / Collider API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Collision / Collider API |
| 分类 | 物理 |
| 主要对象 | ``RuntimeCollider``, ``RuntimeRaycastHit`` |
| C# 入口 | `Entity.AddCapsuleCollider/AddBoxCollider` |
| Python 入口 | `entity.add_capsule_collider/add_box_collider` |
| 说明 | 碰撞体创建、查询、射线检测、触发区和简单碰撞判断。 |

## API 内容

每个实体都可以绑定多个 Collider。Collider 相对于绑定对象本地坐标，实体移动、旋转、缩放时 Collider 会跟随变换。

支持形状：

- `capsule`：胶囊体。
- `box`：有向盒体。

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
