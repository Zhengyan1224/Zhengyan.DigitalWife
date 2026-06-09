---
id: water-particles
title: 水面与粒子触水
category: 对象
objects:
  - RuntimeEntity water_surface
  - RuntimeEntity particle_system
keywords:
  - water
  - particle
  - ripple
---

# 水面与粒子触水

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 水面与粒子触水 |
| 分类 | 对象 |
| 主要对象 | ``RuntimeEntity water_surface``, ``RuntimeEntity particle_system`` |
| C# 入口 | `EnableWaterInteraction, AddWaterRipple` |
| Python 入口 | `set_water_interaction_enabled, add_water_ripple` |
| 说明 | 粒子触水、水面波纹、交互开关和调试示例。 |

## API 内容

水体交互涉及两类实体：

- `particle_system`
  - `EnableWaterInteraction`
  - `KillOnWaterContact`
- `water_surface`
  - `WaterInteractionEnabled`
  - `WaterInteractionRadius`
  - `WaterInteractionStrength`
  - `ParticleRippleMinIntervalSeconds`
  - `ParticleRippleMergeDistance`
  - `MirrorReflectionEnabled`
  - `RippleLifetimeSeconds`
  - `RippleWaveSpeed`
  - `RippleFrequency`
  - `RippleNormalStrength`

只有粒子系统和水面双方都开启交互时，粒子触水才会出波纹。

C#：

```csharp
RuntimeEntity? rain = Scene.GetEntity("Rain FX");
RuntimeEntity? pond = Scene.GetEntity("Pond");

if (rain is not null && pond is not null)
{
    rain.EnableWaterInteraction = true;
    rain.KillOnWaterContact = true;

    pond.WaterInteractionEnabled = true;
    pond.WaterInteractionRadius = 0.9f;
    pond.WaterInteractionStrength = 0.75f;
    pond.ParticleRippleMinIntervalSeconds = 0.08f;
    pond.ParticleRippleMergeDistance = 0.45f;
    pond.MirrorReflectionEnabled = true;
    pond.RippleLifetimeSeconds = 2.8f;
    pond.RippleWaveSpeed = 12.0f;
    pond.RippleFrequency = 16.0f;
    pond.RippleNormalStrength = 0.65f;
}
```

Python：

```python
rain = scene.get_entity("Rain FX")
pond = scene.get_entity("Pond")

if rain is not None and pond is not None:
    rain.set_enable_water_interaction(True)
    rain.set_kill_on_water_contact(True)

    pond.set_water_interaction_enabled(True)
    pond.set_water_interaction_radius(0.9)
    pond.set_water_interaction_strength(0.75)
    pond.set_particle_ripple_min_interval_seconds(0.08)
    pond.set_particle_ripple_merge_distance(0.45)
    pond.set_mirror_reflection_enabled(True)
    pond.set_ripple_lifetime_seconds(2.8)
    pond.set_ripple_wave_speed(12.0)
    pond.set_ripple_frequency(16.0)
    pond.set_ripple_normal_strength(0.65)
```

调参建议：

- 雨：`ParticleRippleMinIntervalSeconds` 取 `0.05 - 0.12`，`ParticleRippleMergeDistance` 取 `0.3 - 0.6`。
- 瀑布：`ParticleRippleMinIntervalSeconds` 取 `0.02 - 0.08`，`ParticleRippleMergeDistance` 取 `0.6 - 1.2`。
- 平静水面的小粒子点缀：`KillOnWaterContact = false`，让粒子穿过水面但仍留下波纹。
- `ParticleRippleMergeDistance = 0` 表示不按空间网格合并触水点，只按单个粒子做节流；数值越大，相邻粒子越容易合并成同一个波纹区域。
- 水面 shader 当前最多同时显示 48 个活动波纹；超过后会替换最旧的波纹。
- `MirrorReflectionEnabled` / `set_mirror_reflection_enabled` 用于切换水面的平面镜面反射。开启时 GameEditor/GamePlayer 会用镜像相机把场景额外渲染到离屏纹理，水面 shader 再采样这张反射纹理并叠加法线扰动和 Fresnel；关闭时水面更接近普通水色/环境渐变。
- 性能注意：每个启用镜面反射的水面至少会多一次场景渲染，当前反射纹理默认使用当前视口 1/2 分辨率。复杂场景、多个水面或多个相机视口会显著增加 GPU 开销。当前实现优先支持水平水面。

C# 额外动作查询：

```csharp
float currentTime = Entity.AnimationTimeSeconds;
int layerCount = Entity.MotionLayerCount;
IReadOnlyList<MotionLayerInfo> layers = Entity.GetMotionLayers();
MotionLayerInfo? wave = Entity.GetMotionLayer("assets/motions/wave.vmd");
```

`MotionLayerInfo` 包含：

- `MotionPath`
- `Weight`
- `TimeSeconds`
- `DurationFrames`
- `ResetPhysicsOnLoop`
- `IsPlaying`

PMX Morph 控制：

```csharp
// 列出模型中的 Morph 名称，名称必须和 PMX 文件里的 MMDMorph.Name 一致。
foreach (string morphName in Entity.MorphNames)
{
    Console.WriteLine(morphName);
}

float smile = Entity.GetMorphWeight("笑い");
Entity.SetMorphWeight("笑い", 1.0f);

// 如果希望当前 Morph 权重作为动作混合基准保存下来。
Entity.SaveMorphAnimWeight("笑い");
Entity.SaveAnimWeight("笑い"); // SaveMorphAnimWeight 的别名，更贴近底层 MMDMorph.SaveAnimWeight 命名。
float savedSmile = Entity.GetMorphSaveAnimWeight("笑い");
Entity.SetMorphSaveAnimWeight("笑い", 0.5f);
Entity.LoadMorphAnimWeight("笑い");
Entity.ClearMorphAnimWeight("笑い");

// 对整个 PMX 的骨骼、Morph、IK 保存/恢复/清空基准动画。
Entity.SaveBaseAnimation();
Entity.LoadBaseAnimation();
Entity.ClearBaseAnimation();

// 清除脚本对 Morph 的持续覆盖，让动作层重新完全接管这个 Morph。
Entity.ClearMorphWeightOverride("笑い");
Entity.ClearMorphWeightOverrides();
```

```python
for morph_name in entity.morph_names:
    print(morph_name)

smile = entity.get_morph_weight("笑い")
entity.set_morph_weight("笑い", 1.0)

# 如果希望当前 Morph 权重作为动作混合基准保存下来。
entity.save_morph_anim_weight("笑い")
entity.save_anim_weight("笑い")  # save_morph_anim_weight 的别名。
saved_smile = entity.get_morph_save_anim_weight("笑い")
entity.set_morph_save_anim_weight("笑い", 0.5)
entity.load_morph_anim_weight("笑い")
entity.clear_morph_anim_weight("笑い")

# 对整个 PMX 的骨骼、Morph、IK 保存/恢复/清空基准动画。
entity.save_base_animation()
entity.load_base_animation()
entity.clear_base_animation()

# 清除脚本对 Morph 的持续覆盖，让动作层重新完全接管这个 Morph。
entity.clear_morph_weight_override("笑い")
entity.clear_morph_weight_overrides()
```

Morph 说明：

- `SetMorphWeight(name, weight)` / `entity.set_morph_weight(name, weight)` 默认会持续覆盖同名 Morph，即使当前正在播放 VMD 动作层，也会在动作采样后重新写入该权重。
- 如果只想改一次当前帧权重，不想持续覆盖动作层，C# 可调用 `SetMorphWeight(name, weight, overrideAnimation: false)`，Python 可调用 `set_morph_weight(name, weight, override_animation=False)`。
- `SaveMorphAnimWeight(name)` / `SaveAnimWeight(name)` 对应底层 `MMDMorph.SaveBaseAnimation()`，会把当前 `Weight` 保存到该 Morph 的 `SaveAnimWeight`。
- `SaveBaseAnimation()`、`LoadBaseAnimation()`、`ClearBaseAnimation()` 作用于整个 PMX 模型的骨骼、Morph 和 IK 基准动画。
- Morph 名称区分 PMX 文件内容，常见日文模型可能是 `笑い`、`まばたき`、`あ`、`い` 等；如果名称不存在，C# 抛出异常或 `Try*` 返回 `false`，Python 命令会由运行时忽略或在控制台输出脚本错误。

PMX 骨骼 / MMDNode 控制：

```csharp
// 列出 PMX 骨骼名称，名称必须和 PMX 文件里的 MMDNode.Name 一致。
foreach (string nodeName in Entity.NodeNames)
{
    Console.WriteLine(nodeName);
}

PmxNodeState head = Entity.GetNodeState("頭");
Vector3 headTranslate = head.Translate;
Quaternion headRotate = head.Rotate;
Vector3 headScale = head.Scale;
Vector3 headAnimTranslate = head.AnimTranslate;
Quaternion headAnimRotate = head.AnimRotate;

// 基础骨骼 TRS：影响 MMDNode.Translate / Rotate / Scale。
Entity.SetNodeTranslate("頭", 0.0f, 0.05f, 0.0f);
Entity.SetNodeRotateEuler("頭", 0.0f, 25.0f, 0.0f);
Entity.SetNodeScale("頭", 1.05f, 1.05f, 1.05f);

// 动画偏移：影响 MMDNode.AnimTranslate / AnimRotate，适合在动作层基础上叠加姿态。
Entity.SetNodeAnimTranslate("右腕", 0.0f, 0.0f, 0.02f);
Entity.SetNodeAnimRotateEuler("右腕", 0.0f, 0.0f, -20.0f);

// 单个骨骼基准动画：保存/恢复/清空 AnimTranslate 与 AnimRotate。
Entity.SaveNodeBaseAnimation("右腕");
Entity.LoadNodeBaseAnimation("右腕");
Entity.ClearNodeBaseAnimation("右腕");

// 清除脚本对骨骼的持续覆盖，让 PMX 初始姿态和 VMD 动作重新接管。
Entity.ClearNodeOverrides("右腕");
Entity.ClearAllNodeOverrides();
```

```python
for node_name in entity.node_names:
    print(node_name)

head = entity.get_node_state("頭")
if head is not None:
    print(head["translate"], head["rotate"], head["scale"])

# 基础骨骼 TRS：影响 MMDNode.Translate / Rotate / Scale。
entity.set_node_translate("頭", 0.0, 0.05, 0.0)
entity.set_node_rotate_euler("頭", 0.0, 25.0, 0.0)
entity.set_node_scale("頭", 1.05, 1.05, 1.05)

# 动画偏移：影响 MMDNode.AnimTranslate / AnimRotate，适合在动作层基础上叠加姿态。
entity.set_node_anim_translate("右腕", 0.0, 0.0, 0.02)
entity.set_node_anim_rotate_euler("右腕", 0.0, 0.0, -20.0)

# 单个骨骼基准动画：保存/恢复/清空 AnimTranslate 与 AnimRotate。
entity.save_node_base_animation("右腕")
entity.load_node_base_animation("右腕")
entity.clear_node_base_animation("右腕")

# 清除脚本对骨骼的持续覆盖，让 PMX 初始姿态和 VMD 动作重新接管。
entity.clear_node_overrides("右腕")
entity.clear_all_node_overrides()
```

骨骼控制说明：

- `SetNodeTranslate/Rotate/Scale` 控制的是骨骼基础 TRS，即底层 `MMDNode.Translate`、`MMDNode.Rotate`、`MMDNode.Scale`。它会改变骨骼的本地基础姿态，适合做模型校正、挂点调整或非动作层姿态修改。
- `SetNodeAnimTranslate/AnimRotate` 控制的是动作偏移，即底层 `MMDNode.AnimTranslate`、`MMDNode.AnimRotate`。它会在 VMD 动作层采样后覆盖同名骨骼，适合做运行时看向、手臂微调、程序化姿态叠加。
- 上面这些设置默认会持续覆盖后续帧。C# 可传 `overrideAnimation: false`，Python 可传 `override_animation=False`，表示只写入当前值，不登记持续覆盖。
- 旋转同时支持四元数和欧拉角。C# 可用 `SetNodeRotate(name, Quaternion)` / `SetNodeRotateEuler(name, xDeg, yDeg, zDeg)`；Python 可用 `set_node_rotate(name, x, y, z, w)` / `set_node_rotate_euler(name, x_deg, y_deg, z_deg)`。
- `SaveNodeBaseAnimation(name)` 对应底层 `MMDNode.SaveBaseAnimation()`，保存当前 `AnimTranslate` 与 `AnimRotate` 到 `BaseAnimTranslate` / `BaseAnimRotate`。
- 骨骼名称来自 PMX 文件，常见日文名如 `頭`、`首`、`上半身`、`右腕`、`左腕`。名称不存在时 C# 抛出异常或 `Try*` 返回 `false`，Python 命令会由运行时忽略或在控制台输出脚本错误。

材质贴图覆盖：

```csharp
// 按材质下标。
Entity.SetMaterialTexture(0, "assets/textures/body_alt.png");
Entity.SetMaterialRenderTexture(0, "MiniMapRT");
Entity.ClearMaterialTextureOverride(0);

// 按材质名称。
Entity.SetMaterialTexture("Body", "project:assets/textures/body_alt.png");
Entity.SetMaterialRenderTexture("Screen", "CameraRT");
Entity.ClearMaterialTextureOverrides();
```

```python
entity.set_material_texture(0, "assets/textures/body_alt.png")
entity.set_material_render_texture(0, "MiniMapRT")
entity.clear_material_texture_override(0)

entity.set_material_texture("Body", "project:assets/textures/body_alt.png")
entity.set_material_render_texture("Screen", "CameraRT")
entity.clear_material_texture_overrides()
```

PMX 绑定关系：

```csharp
Entity.BindRelation("TargetPmx", bindComponentTransform: true, bindLighting: false);
Entity.ClearRelationBinding();
```

```python
entity.bind_relation("TargetPmx", bind_component_transform=True, bind_lighting=False)
entity.clear_relation()
```

`BindRelation` 只对 PMX 模型有效；目标也必须是 PMX 模型。
