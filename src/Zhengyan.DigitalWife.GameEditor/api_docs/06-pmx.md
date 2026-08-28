---
id: pmx
title: PMX 动作与材质
category: 对象
objects:
  - RuntimeEntity PMX
  - PmxNodeState
keywords:
  - pmx
  - morph
  - node
  - material
  - motion
  - physics
---

# PMX 动作与材质

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | PMX 动作与材质 |
| 分类 | 对象 |
| 主要对象 | ``RuntimeEntity PMX``, ``PmxNodeState`` |
| C# 入口 | `SetMorphWeight, SetNodeTranslate, ApplyMotionLayers` |
| Python 入口 | `set_morph_weight, set_node_translate, apply_motion_layers` |
| 说明 | PMX 动作层、Morph、骨骼、IK、材质贴图覆盖和示例。 |

## API 内容

动作路径一般放在 `assets/motions/*.vmd`。GameEditor 添加动作资源后保存，会把资源复制到工程目录。

```csharp
Entity.ApplyMotion("assets/motions/idle.vmd");
Entity.SetMotionLayers(new[]
{
    new MotionLayerDefinition("assets/motions/idle.vmd", 1.0f),
    new MotionLayerDefinition("assets/motions/wave.vmd", 0.0f)
});
Entity.AddMotionLayer("assets/motions/wave.vmd", weight: 0.5f);
Entity.SetMotionLayerWeight("assets/motions/wave.vmd", 1.0f);
Entity.SetMotionLayerResetPhysicsOnLoop("assets/motions/wave.vmd", true);
Entity.PlayMotion();
Entity.PauseMotion();
Entity.SeekMotionFrame(45);
Entity.SeekMotionTime(1.5f);
Entity.ResetPhysics();
Entity.ResetMotion();
Entity.StopMotion();
Entity.PlayMotionLayer("assets/motions/wave.vmd");
Entity.PauseMotionLayer("assets/motions/wave.vmd");
Entity.SetMotionLayerFrame("assets/motions/wave.vmd", 30);
Entity.SetMotionLayerTime("assets/motions/wave.vmd", 1.0f);
Entity.RemoveMotionLayer("assets/motions/wave.vmd");
Entity.ClearMotion();
```

```python
entity.apply_motion("assets/motions/idle.vmd")
entity.set_motion_layers([
    {"path": "assets/motions/idle.vmd", "weight": 1.0},
    {"path": "assets/motions/wave.vmd", "weight": 0.0},
])
entity.add_motion_layer("assets/motions/wave.vmd", weight=0.5)
entity.set_motion_layer_weight("assets/motions/wave.vmd", 1.0)
entity.set_motion_layer_reset_physics_on_loop("assets/motions/wave.vmd", True)
entity.play_motion()
entity.pause_motion()
entity.seek_motion_frame(45)
entity.seek_motion_time(1.5)
entity.reset_physics()
entity.reset_motion()
entity.stop_motion()
entity.play_motion_layer("assets/motions/wave.vmd")
entity.pause_motion_layer("assets/motions/wave.vmd")
entity.set_motion_layer_frame("assets/motions/wave.vmd", 30)
entity.set_motion_layer_time("assets/motions/wave.vmd", 1.0)
entity.remove_motion_layer("assets/motions/wave.vmd")
entity.clear_motion()
```

PMX 运行时渲染与播放控制：

```csharp
Entity.IsPlaying = true;
Entity.PlaybackSpeed = 1.0f;
Entity.LoopMotion = true;
Entity.ResetPhysicsOnMotionLoop = true;
Entity.PhysicsEnabled = true;
Entity.PhysicsGravity = new Vector3(0.0f, -49.0f, 0.0f);
Entity.PhysicsGravityDirection = Vector3.Normalize(new Vector3(1.0f, -1.0f, 0.0f));
Entity.PhysicsGravityMagnitude = 98.0f;

// 等价方法；传入的是完整重力加速度向量。
Entity.SetPhysicsGravity(0.0f, -98.0f, 0.0f);
Entity.SetPhysicsGravityDirection(0.0f, -1.0f, 0.0f);
Entity.SetPhysicsGravityMagnitude(49.0f);

Entity.EnableEdge = true;
Entity.EnableShadow = true;
Entity.ReceiveShadow = true;
Entity.ReceiveShadowMode = "toon"; // "smooth" or "toon"
Entity.DrawShadowInMainPass = false;
```

```python
entity.set_playing(True)
entity.set_playback_speed(1.0)
entity.set_loop_motion(True)
entity.set_reset_physics_on_motion_loop(True)
entity.set_physics_enabled(True)
entity.set_physics_gravity(0.0, -49.0, 0.0)
entity.set_physics_gravity_direction(1.0, -1.0, 0.0)
entity.set_physics_gravity_magnitude(98.0)

gravity = entity.physics_gravity
gravity_direction = entity.physics_gravity_direction
gravity_magnitude = entity.physics_gravity_magnitude

entity.set_edge_enabled(True)
entity.set_shadow_enabled(True)
entity.set_receive_shadow(True)
entity.set_receive_shadow_mode("toon")  # "smooth" or "toon"
entity.set_draw_shadow_in_main_pass(False)
```

说明：

- `IsPlaying` 只对当前实体的可播放能力生效。对 PMX 表示动作播放，对粒子/水面表示系统启停。
- `PlaybackSpeed` 对 PMX 表示动作倍速，对粒子系统表示模拟速度。
- `LoopMotion` 和 `ResetPhysicsOnMotionLoop` 仅对 PMX 有意义。
- GameEditor 在 PMX 实体面板中提供 `Physics` 复选框，并把设置保存到场景；该开关只控制 PMX 文件内置的刚体与关节，不影响实体下方配置的 `Colliders`。
- `PhysicsEnabled` / `physics_enabled` 表示 PMX 内置刚体与关节物理当前是否启用。C# 可直接读写 `Entity.PhysicsEnabled`；Python 读取 `entity.physics_enabled`，并用 `entity.set_physics_enabled(...)` 动态切换。
- 每个 PMX 拥有独立的重力向量。修改一个实体的重力不会影响场景中的其它 PMX，也不会影响实体 `Colliders`、粒子或水面。
- `PhysicsGravity` / `physics_gravity` 是完整重力加速度向量，向量方向表示重力方向，向量长度表示重力大小。默认值为 `(0, -98, 0)`；MMD 物理使用 10 倍单位缩放，因此该值对应通常的向下重力效果。
- `PhysicsGravityDirection` / `physics_gravity_direction` 是归一化方向；`PhysicsGravityMagnitude` / `physics_gravity_magnitude` 是非负大小。大小设为 `0` 可关闭该 PMX 的重力，但不会关闭刚体与关节模拟。
- C# 可以读写上述三个属性，或调用 `SetPhysicsGravity(...)`、`SetPhysicsGravityDirection(...)`、`SetPhysicsGravityMagnitude(...)`。Python 快照属性只读，运行时修改使用对应的 `set_physics_gravity*` 方法。
- 运行时修改重力会立即更新该 PMX 的 Bullet 物理世界并唤醒刚体，不会重建模型或重置当前物理姿态。
- 关闭 PMX 物理后，模型继续播放 VMD 和计算骨骼姿态，但不会执行刚体模拟。重新开启时会清除关闭前遗留的刚体速度，并从当前动画姿态重新开始模拟。
- `EnableWaterInteraction` 仅对粒子系统有意义。只有粒子实体和水面实体都开启水体交互时，系统才会按每个活跃粒子的位置和当前尺寸做近似球检测，并在接触水面时触发波纹。
- `KillOnWaterContact` 仅对粒子系统有意义。开启后，接触水面的粒子会在当前帧结束；关闭时粒子会穿过水面继续运动。
- `WaterInteractionEnabled`、`WaterInteractionRadius`、`WaterInteractionStrength`、`ParticleRippleMinIntervalSeconds`、`ParticleRippleMergeDistance`、`RippleLifetimeSeconds`、`RippleWaveSpeed`、`RippleFrequency`、`RippleNormalStrength` 仅对 `water_surface` 有意义。
- `EnableEdge`、`EnableShadow`、`ReceiveShadow`、`DrawShadowInMainPass` 仅对 PMX 有意义。
- GameEditor/GamePlayer 使用方向光 shadow map 和点光源/射灯共用的局部光阴影图集。`EnableShadow` 控制 PMX 是否写入这些 shadow map，`ReceiveShadow` 独立控制 PMX 主材质是否采样这些阴影；因此模型可以只投射、只接收、同时投射和接收，或者两者都关闭。
- shadow map 需要实际的接收面，例如场景 PMX 模型里的地面或开启 `Receive shadow` 的 3D 贴图矩形面。它不会像旧平面投影阴影那样自动投到固定高度平面。
- `DrawShadowInMainPass` 是兼容旧示例程序的平面投影阴影开关。GameEditor/GamePlayer 已经不依赖它，通常保持默认即可。
- `PlayMotion` / `PauseMotion` 只改变播放状态，不会清掉已加载动作层。
- `StopMotion` 会把动作停下并重置到起始状态，效果接近“停止并回到 0 帧”。
- `ResetMotion` 会重置动作与姿态；`ResetPhysics` 只重置物理，并从当前骨骼姿态重新同步刚体。旧名称 `ResetMotionPhysics` / `reset_motion_physics` 仍可继续使用。
- `SeekMotionFrame` / `SeekMotionTime` 会把当前 PMX 动作层一起跳到指定帧或秒。
- `PlayMotionLayer` / `PauseMotionLayer` / `SetMotionLayerFrame` / `SetMotionLayerTime` 只作用于指定动作层。
