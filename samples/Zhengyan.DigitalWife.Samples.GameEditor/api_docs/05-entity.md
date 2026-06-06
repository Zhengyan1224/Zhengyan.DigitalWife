---
id: entity
title: Entity API
category: 对象
objects:
  - RuntimeEntity
  - entity
keywords:
  - entity
  - RuntimeEntity
  - Transform
  - PMX
  - collider
---

# Entity API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Entity API |
| 分类 | 对象 |
| 主要对象 | ``RuntimeEntity``, ``entity`` |
| C# 入口 | `Entity / Scene.GetEntity(...)` |
| Python 入口 | `entity / scene.get_entity(...)` |
| 说明 | 实体属性、Transform、播放状态、碰撞、水面交互、材质和模型相关入口。 |

## API 内容

`RuntimeEntity` 代表场景中的对象。对象类型包括：

| 类型 | 说明 |
| --- | --- |
| `pmx_model` | PMX 模型，支持动作、材质贴图覆盖、TTS 口型、PMX 绑定关系。 |
| `empty_object` | 空对象，不渲染；支持 Transform、脚本、碰撞体，适合触发器、挂点、相机目标。 |
| `textured_plane` | 3D 矩形面，可设置图片纹理和 Billboard。 |
| `particle_system` | 粒子系统。 |
| `water_surface` | 水面对象，可开启水体交互波纹。 |

常用属性：

| C# 属性 | Python 快照 | 说明 |
| --- | --- | --- |
| `Id` | `id` | 实体 Id。 |
| `Name` | `name` | 实体名称。 |
| `Type` | `type` | 实体类型。 |
| `Position` | `position` | 世界坐标。C# 为 `Vector3`，Python 为 `[x,y,z]`。 |
| `Scale` | `scale` | 缩放。 |
| `Rotation` | `rotation` | 四元数旋转。 |
| `Visible` | 无直接快照字段 | 是否可见。Python 用 `set_visible` 修改。 |
| `IsPlaying` | 无直接快照字段 | PMX 动作/粒子/水面是否播放。Python 用 `set_playing` 修改。 |
| `PlaybackSpeed` | 无直接快照字段 | PMX 动作或粒子模拟速度。 |
| `LoopMotion` | 无直接快照字段 | PMX 动作是否循环。Python 用 `set_loop_motion` 修改。 |
| `ResetPhysicsOnMotionLoop` | 无直接快照字段 | PMX 动作循环时是否重置物理。Python 用 `set_reset_physics_on_motion_loop` 修改。 |
| `EnableEdge` | 无直接快照字段 | PMX 是否绘制描边。Python 用 `set_edge_enabled` 修改。 |
| `EnableShadow` | 无直接快照字段 | PMX 是否参与阴影绘制。Python 用 `set_shadow_enabled` 修改。 |
| `EnableWaterInteraction` | `enable_water_interaction` | 粒子系统是否参与水体交互。Python 用 `set_enable_water_interaction` 修改。 |
| `KillOnWaterContact` | `kill_on_water_contact` | 粒子系统粒子接触水面后是否立即消失。Python 用 `set_kill_on_water_contact` 修改。 |
| `WaterInteractionEnabled` | `water_interaction_enabled` | 水面对象是否启用水体交互检测。Python 用 `set_water_interaction_enabled` 修改。 |
| `WaterInteractionRadius` | `water_interaction_radius` | 水面波纹半径。Python 用 `set_water_interaction_radius` 修改。 |
| `WaterInteractionStrength` | `water_interaction_strength` | 水面波纹强度。Python 用 `set_water_interaction_strength` 修改。 |
| `ParticleRippleMinIntervalSeconds` | `particle_ripple_min_interval_seconds` | 同一区域粒子触水的最小波纹间隔。Python 用 `set_particle_ripple_min_interval_seconds` 修改。 |
| `ParticleRippleMergeDistance` | `particle_ripple_merge_distance` | 粒子触水波纹的空间合并距离。Python 用 `set_particle_ripple_merge_distance` 修改。 |
| `RippleLifetimeSeconds` | 无 | 水面单个波纹的持续时间。 |
| `RippleWaveSpeed` | 无 | 水面波纹传播速度。 |
| `RippleFrequency` | 无 | 水面波纹频率。 |
| `RippleNormalStrength` | 无 | 水面波纹法线扰动强度。 |
| `DrawShadowInMainPass` | 无直接快照字段 | PMX 是否在主渲染通道直接绘制地面影子。Python 用 `set_draw_shadow_in_main_pass` 修改。 |
| `MaterialNames` | `material_names` | PMX 材质名称列表。 |
| `Colliders` | `colliders` | 碰撞体快照。 |

Transform：

```csharp
Entity.SetPosition(0, 1, 0);
Entity.Translate(0, 0, -1);
Entity.SetScale(0.2f, 0.2f, 0.2f);
Entity.RotateX(10);
Entity.RotateY(90);
Entity.RotateZ(5);
Entity.Visible = true;
Entity.IsPlaying = true;
Entity.PlaybackSpeed = 1.2f;
Entity.LoopMotion = true;
Entity.ResetPhysicsOnMotionLoop = true;
Entity.EnableEdge = true;
Entity.EnableShadow = true;
Entity.EnableWaterInteraction = true;
Entity.KillOnWaterContact = true;
Entity.WaterInteractionEnabled = true;
Entity.WaterInteractionRadius = 1.0f;
Entity.WaterInteractionStrength = 0.9f;
Entity.ParticleRippleMinIntervalSeconds = 0.08f;
Entity.ParticleRippleMergeDistance = 0.5f;
Entity.RippleLifetimeSeconds = 2.8f;
Entity.RippleWaveSpeed = 12.0f;
Entity.RippleFrequency = 16.0f;
Entity.RippleNormalStrength = 0.65f;
Entity.DrawShadowInMainPass = false;
```

```python
entity.set_position(0, 1, 0)
entity.translate(0, 0, -1)
entity.set_scale(0.2, 0.2, 0.2)
entity.rotate_x(10)
entity.rotate_y(90)
entity.rotate_z(5)
entity.set_visible(True)
entity.set_playing(True)
entity.set_playback_speed(1.2)
entity.set_loop_motion(True)
entity.set_reset_physics_on_motion_loop(True)
entity.set_edge_enabled(True)
entity.set_shadow_enabled(True)
entity.set_enable_water_interaction(True)
entity.set_kill_on_water_contact(True)
entity.set_water_interaction_enabled(True)
entity.set_water_interaction_radius(1.0)
entity.set_water_interaction_strength(0.9)
entity.set_particle_ripple_min_interval_seconds(0.08)
entity.set_particle_ripple_merge_distance(0.5)
entity.set_draw_shadow_in_main_pass(False)
```

C# 额外可读/可写属性：

| 属性 | 说明 |
| --- | --- |
| `IsPmxModel` | 当前实体是否有 PMX 运行时对象。 |
| `LoopMotion` | PMX 动作是否循环。 |
| `ResetPhysicsOnMotionLoop` | PMX 动作循环时是否重置物理。 |
| `EnableEdge` | PMX 是否绘制描边。 |
| `EnableShadow` | PMX 是否参与阴影绘制。 |
| `EnableWaterInteraction` | 粒子系统是否参与水体交互。 |
| `KillOnWaterContact` | 粒子系统粒子接触水面后是否立即消失。 |
| `WaterInteractionEnabled` | 水面对象是否启用水体交互检测。 |
| `WaterInteractionRadius` | 水面波纹半径。 |
| `WaterInteractionStrength` | 水面波纹强度。 |
| `ParticleRippleMinIntervalSeconds` | 同一区域粒子触水的最小波纹间隔。 |
| `ParticleRippleMergeDistance` | 粒子触水波纹的空间合并距离。 |
| `RippleLifetimeSeconds` | 水面单个波纹的持续时间。 |
| `RippleWaveSpeed` | 水面波纹传播速度。 |
| `RippleFrequency` | 水面波纹频率。 |
| `RippleNormalStrength` | 水面波纹法线扰动强度。 |
| `DrawShadowInMainPass` | PMX 是否在主渲染通道直接绘制地面影子。 |
| `RelationEnabled` | 是否启用 PMX 绑定关系。 |
| `RelationEntity` | 绑定目标实体名称或 Id。 |
| `RelationBindComponentTransform` | 是否绑定组件 Transform。 |
| `RelationBindLighting` | 是否绑定光照。 |
| `Collision` | 旧版单 Collider 配置对象。 |
| `Colliders` | 多 Collider 配置列表。 |
| `CollisionEnabled` | 所有有效 Collider 是否启用。 |
| `CollisionShape` | 主 Collider 形状。 |
| `CollisionCenter` | 主 Collider 本地中心。 |
| `CollisionRadius` | 主胶囊 Collider 半径。 |
| `CollisionHeight` | 主胶囊 Collider 高度。 |
| `CollisionAxis` | 主胶囊 Collider 轴向，`x`、`y`、`z`。 |

简单 WASD 移动：

```csharp
if (IsUpdate)
{
    float speed = Input.IsShiftDown ? 4.0f : 1.5f;
    float step = speed * (float)DeltaSeconds;

    if (Input.IsKeyDown("W")) Entity.Translate(0, 0, -step);
    if (Input.IsKeyDown("S")) Entity.Translate(0, 0, step);
    if (Input.IsKeyDown("A")) Entity.Translate(-step, 0, 0);
    if (Input.IsKeyDown("D")) Entity.Translate(step, 0, 0);
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    speed = 4.0 if input.shift_down else 1.5
    step = speed * delta_seconds

    if input.is_key_down("w"):
        entity.translate(0, 0, -step)
    if input.is_key_down("s"):
        entity.translate(0, 0, step)
    if input.is_key_down("a"):
        entity.translate(-step, 0, 0)
    if input.is_key_down("d"):
        entity.translate(step, 0, 0)
```
