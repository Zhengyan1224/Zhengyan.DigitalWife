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
Entity.ResetMotionPhysics();
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
entity.reset_motion_physics()
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

Entity.EnableEdge = true;
Entity.EnableShadow = true;
Entity.DrawShadowInMainPass = false;
```

```python
entity.set_playing(True)
entity.set_playback_speed(1.0)
entity.set_loop_motion(True)
entity.set_reset_physics_on_motion_loop(True)

entity.set_edge_enabled(True)
entity.set_shadow_enabled(True)
entity.set_draw_shadow_in_main_pass(False)
```

说明：

- `IsPlaying` 只对当前实体的可播放能力生效。对 PMX 表示动作播放，对粒子/水面表示系统启停。
- `PlaybackSpeed` 对 PMX 表示动作倍速，对粒子系统表示模拟速度。
- `LoopMotion` 和 `ResetPhysicsOnMotionLoop` 仅对 PMX 有意义。
- `EnableWaterInteraction` 仅对粒子系统有意义。只有粒子实体和水面实体都开启水体交互时，系统才会按每个活跃粒子的位置和当前尺寸做近似球检测，并在接触水面时触发波纹。
- `KillOnWaterContact` 仅对粒子系统有意义。开启后，接触水面的粒子会在当前帧结束；关闭时粒子会穿过水面继续运动。
- `WaterInteractionEnabled`、`WaterInteractionRadius`、`WaterInteractionStrength`、`ParticleRippleMinIntervalSeconds`、`ParticleRippleMergeDistance`、`RippleLifetimeSeconds`、`RippleWaveSpeed`、`RippleFrequency`、`RippleNormalStrength` 仅对 `water_surface` 有意义。
- `EnableEdge`、`EnableShadow`、`DrawShadowInMainPass` 仅对 PMX 有意义。
- `DrawShadowInMainPass = false` 时，地面影子会交给独立的地面阴影 pass 处理；`true` 时在 PMX 主绘制阶段直接绘制。
- `PlayMotion` / `PauseMotion` 只改变播放状态，不会清掉已加载动作层。
- `StopMotion` 会把动作停下并重置到起始状态，效果接近“停止并回到 0 帧”。
- `ResetMotion` 会重置动作与姿态；`ResetMotionPhysics` 只重置物理。
- `SeekMotionFrame` / `SeekMotionTime` 会把当前 PMX 动作层一起跳到指定帧或秒。
- `PlayMotionLayer` / `PauseMotionLayer` / `SetMotionLayerFrame` / `SetMotionLayerTime` 只作用于指定动作层。
