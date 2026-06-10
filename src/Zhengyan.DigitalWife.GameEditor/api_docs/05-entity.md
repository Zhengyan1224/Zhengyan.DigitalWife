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
| `textured_plane` | 3D 矩形面，可设置图片纹理、Billboard、接收 shadow map 阴影和镜面反射。 |
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
| `EnableShadow` | 无直接快照字段 | PMX 是否参与 shadow map 阴影。开启时会投射并接收阴影；关闭时不参与。Python 用 `set_shadow_enabled` 修改。 |
| `EnableWaterInteraction` | `enable_water_interaction` | 粒子系统是否参与水体交互。Python 用 `set_enable_water_interaction` 修改。 |
| `KillOnWaterContact` | `kill_on_water_contact` | 粒子系统粒子接触水面后是否立即消失。Python 用 `set_kill_on_water_contact` 修改。 |
| `WaterInteractionEnabled` | `water_interaction_enabled` | 水面对象是否启用水体交互检测。Python 用 `set_water_interaction_enabled` 修改。 |
| `WaterInteractionRadius` | `water_interaction_radius` | 水面波纹半径。Python 用 `set_water_interaction_radius` 修改。 |
| `WaterInteractionStrength` | `water_interaction_strength` | 水面波纹强度。Python 用 `set_water_interaction_strength` 修改。 |
| `ParticleRippleMinIntervalSeconds` | `particle_ripple_min_interval_seconds` | 同一区域粒子触水的最小波纹间隔。Python 用 `set_particle_ripple_min_interval_seconds` 修改。 |
| `ParticleRippleMergeDistance` | `particle_ripple_merge_distance` | 粒子触水波纹的空间合并距离。Python 用 `set_particle_ripple_merge_distance` 修改。 |
| `MirrorReflectionEnabled` | `mirror_reflection_enabled` | 水面是否启用平面镜面反射。开启时会用镜像相机额外渲染一次场景供水面采样。Python 用 `set_mirror_reflection_enabled` 修改。 |
| `RippleLifetimeSeconds` | 无 | 水面单个波纹的持续时间。 |
| `RippleWaveSpeed` | 无 | 水面波纹传播速度。 |
| `RippleFrequency` | 无 | 水面波纹频率。 |
| `RippleNormalStrength` | 无 | 水面波纹法线扰动强度。 |
| `PlaneMirrorReflectionEnabled` | `plane_mirror_reflection_enabled` | 3D 贴图矩形面是否启用平面镜面反射。Python 用 `set_plane_mirror_reflection_enabled` 修改。 |
| `PlaneMirrorReflectionStrength` | `plane_mirror_reflection_strength` | 3D 贴图矩形面镜面反射强度，范围 `0 - 1`。Python 用 `set_plane_mirror_reflection_strength` 修改。 |
| `DrawShadowInMainPass` | 无直接快照字段 | 兼容旧 PMX 平面投影阴影的开关。GameEditor/GamePlayer 使用 shadow map 后通常不需要修改。Python 用 `set_draw_shadow_in_main_pass` 修改。 |
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
Entity.LookAt(0.0f, Entity.Position.Y, -1.0f);
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
Entity.MirrorReflectionEnabled = true;
Entity.PlaneMirrorReflectionEnabled = true;
Entity.PlaneMirrorReflectionStrength = 0.85f;
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
entity.set_mirror_reflection_enabled(True)
entity.set_plane_mirror_reflection_enabled(True)
entity.set_plane_mirror_reflection_strength(0.85)
entity.set_draw_shadow_in_main_pass(False)
```

C# 额外可读/可写属性：

| 属性 | 说明 |
| --- | --- |
| `IsPmxModel` | 当前实体是否有 PMX 运行时对象。 |
| `LoopMotion` | PMX 动作是否循环。 |
| `ResetPhysicsOnMotionLoop` | PMX 动作循环时是否重置物理。 |
| `EnableEdge` | PMX 是否绘制描边。 |
| `EnableShadow` | PMX 是否参与 shadow map 阴影。 |
| `EnableWaterInteraction` | 粒子系统是否参与水体交互。 |
| `KillOnWaterContact` | 粒子系统粒子接触水面后是否立即消失。 |
| `WaterInteractionEnabled` | 水面对象是否启用水体交互检测。 |
| `WaterInteractionRadius` | 水面波纹半径。 |
| `WaterInteractionStrength` | 水面波纹强度。 |
| `ParticleRippleMinIntervalSeconds` | 同一区域粒子触水的最小波纹间隔。 |
| `ParticleRippleMergeDistance` | 粒子触水波纹的空间合并距离。 |
| `MirrorReflectionEnabled` | 水面是否启用平面镜面反射。 |
| `RippleLifetimeSeconds` | 水面单个波纹的持续时间。 |
| `RippleWaveSpeed` | 水面波纹传播速度。 |
| `RippleFrequency` | 水面波纹频率。 |
| `RippleNormalStrength` | 水面波纹法线扰动强度。 |
| `PlaneMirrorReflectionEnabled` | 3D 贴图矩形面是否启用平面镜面反射。 |
| `PlaneMirrorReflectionStrength` | 3D 贴图矩形面镜面反射强度，范围 `0 - 1`。 |
| `DrawShadowInMainPass` | 兼容旧 PMX 平面投影阴影的开关。GameEditor/GamePlayer 使用 shadow map 后通常不需要修改。 |
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

## 自定义 Shader

脚本层可以给 `pmx_model` 和 `textured_plane` 绑定用户自定义 shader。shader 文件建议放在工程目录 `assets/shaders` 下，例如 `assets/shaders/outline.vert` 和 `assets/shaders/outline.frag`。路径支持工程相对路径、`project:`、`app:` 和绝对路径；不支持 `rt:`，因为 `rt:` 是运行时贴图引用，不是 shader 文件。

自定义 shader 会替换对象的主渲染 pass。PMX 的 shadow map 深度 pass、旧平面投影阴影 pass 和描边 pass 仍使用内置逻辑；如果自定义 PMX shader 不希望叠加描边，可以同时设置 `Entity.EnableEdge = false` 或 Python `entity.set_edge_enabled(False)`。

### 方法

| C# 方法 | Python 方法 | 参数 | 说明 |
| --- | --- | --- | --- |
| `SetCustomShader(vertexShaderPath, fragmentShaderPath)` | `set_custom_shader(vertex_shader, fragment_shader)` | vertex/fragment shader 文件路径 | 编译并启用自定义 shader。 |
| `ClearCustomShader()` | `clear_custom_shader()` | 无 | 恢复内置 shader，并清空自定义 uniform。 |
| `SetCustomShaderFloat(name, value)` | `set_custom_shader_float(name, value)` | `name`: uniform 名称；`value`: float | 设置 `float` uniform。 |
| `SetCustomShaderInt(name, value)` | `set_custom_shader_int(name, value)` | `name`: uniform 名称；`value`: int | 设置 `int` uniform。 |
| `SetCustomShaderVector2(name, x, y)` | `set_custom_shader_vector2(name, x, y)` | `name`, `x`, `y` | 设置 `vec2` uniform。 |
| `SetCustomShaderVector3(name, x, y, z)` | `set_custom_shader_vector3(name, x, y, z)` | `name`, `x`, `y`, `z` | 设置 `vec3` uniform。 |
| `SetCustomShaderVector4(name, x, y, z, w)` | `set_custom_shader_vector4(name, x, y, z, w)` | `name`, `x`, `y`, `z`, `w` | 设置 `vec4` uniform。 |
| `SetCustomShaderColor(name, r, g, b, a)` | `set_custom_shader_color(name, r, g, b, a=1.0)` | `name`, RGBA | 设置颜色型 `vec4` uniform。 |
| `ClearCustomShaderUniform(name)` | `clear_custom_shader_uniform(name)` | `name`: uniform 名称 | 删除单个脚本设置的 uniform。 |
| `ClearCustomShaderUniforms()` | `clear_custom_shader_uniforms()` | 无 | 删除所有脚本设置的 uniform。 |

### 顶点属性

| 对象 | 属性名 | 类型 | 说明 |
| --- | --- | --- | --- |
| `textured_plane` | `in_Pos` | `vec3` | 本地坐标位置。 |
| `textured_plane` | `in_Uv` 或 `in_UV` | `vec2` | 纹理坐标。 |
| `pmx_model` | `in_Pos` | `vec3` | 动画/物理更新后的模型顶点位置。 |
| `pmx_model` | `in_Nor` | `vec3` | 动画/物理更新后的模型法线。 |
| `pmx_model` | `in_UV` 或 `in_Uv` | `vec2` | 模型 UV。 |

### 内置 Uniform

| Uniform | 类型 | 说明 |
| --- | --- | --- |
| `u_World` | `mat4` | 对象世界矩阵。 |
| `u_View` | `mat4` | 当前相机 View 矩阵。 |
| `u_Projection` | `mat4` | 当前相机 Projection 矩阵。 |
| `u_WV` | `mat4` | `World * View`。 |
| `u_WVP` | `mat4` | `World * View * Projection`。 |
| `u_Time` | `float` | 游戏运行总秒数。 |
| `u_DeltaTime` | `float` | 当前帧间隔秒数。 |
| `u_FrameCount` | `int` | 当前帧序号。 |
| `u_Texture` / `u_Tex` | `sampler2D` | 主贴图，绑定在 texture unit 0。PMX 会优先使用脚本材质覆盖贴图。 |
| `u_Tint` | `vec4` | `textured_plane` 的颜色叠加。 |
| `u_Opacity` | `float` | `textured_plane` 的透明度。 |
| `u_FlipV` | `int` | `textured_plane` 使用 Render Texture 时为 `1`，可用于翻转 V 坐标。 |
| `u_PlanarReflectionTex` | `sampler2D` | `textured_plane` 的平面反射贴图，绑定在 texture unit 2。 |
| `u_PlanarReflectionEnabled` | `float` | `textured_plane` 是否启用反射。 |
| `u_ReflectionViewProjection` | `mat4` | 反射相机 ViewProjection。 |
| `u_ShadowMapEnabled` | `int` | shadow map 是否可用。 |
| `u_ShadowMap` | `sampler2DShadow` | `textured_plane` 的阴影贴图，绑定在 texture unit 1。 |
| `u_LightViewProjection` | `mat4` | 平面接收阴影时使用的方向光矩阵。 |
| `u_ShadowMapStrength` | `float` | 阴影强度。 |
| `u_ShadowMapBias` | `float` | 阴影深度偏移。 |
| `u_MaterialIndex` | `int` | PMX 当前材质索引。 |
| `u_Ambient` / `u_MaterialAmbient` | `vec3` | PMX 当前材质环境光。 |
| `u_Diffuse` / `u_MaterialDiffuse` | `vec3` | PMX 当前材质漫反射。 |
| `u_Specular` / `u_MaterialSpecular` | `vec3` | PMX 当前材质高光颜色。 |
| `u_SpecularPower` / `u_MaterialSpecularPower` | `float` | PMX 当前材质高光强度。 |
| `u_Alpha` / `u_MaterialAlpha` | `float` | PMX 当前材质透明度。 |
| `u_LightColor` | `vec3` | PMX 当前方向光颜色。 |
| `u_LightDir` | `vec3` | PMX 当前方向光方向，已转换到视图空间。 |
| `u_AmbientLightColor` | `vec3` | PMX 环境光颜色。 |
| `u_AmbientLightStrength` | `float` | PMX 环境光强度。 |

没有被 shader 声明的 uniform 会被跳过，不需要全部写出来。shader 编译或链接失败会抛出错误并显示在 GamePlayer 控制台日志中。

### textured_plane 示例

`assets/shaders/wave_plane.vert`：

```glsl
#version 300 es

in vec3 in_Pos;
in vec2 in_Uv;

uniform mat4 u_WVP;
uniform float u_Time;
uniform float u_WaveStrength;

out vec2 v_Uv;

void main()
{
    v_Uv = in_Uv;
    vec3 p = in_Pos;
    p.z += sin((p.x + u_Time) * 10.0) * u_WaveStrength;
    gl_Position = u_WVP * vec4(p, 1.0);
}
```

`assets/shaders/wave_plane.frag`：

```glsl
#version 300 es

precision highp float;

in vec2 v_Uv;

uniform sampler2D u_Texture;
uniform int u_FlipV;
uniform vec4 u_Tint;
uniform vec4 u_ColorBoost;

out vec4 out_Color;

void main()
{
    vec2 uv = v_Uv;
    if (u_FlipV != 0)
    {
        uv.y = 1.0 - uv.y;
    }

    vec4 baseColor = texture(u_Texture, uv) * u_Tint;
    out_Color = vec4(baseColor.rgb * u_ColorBoost.rgb, baseColor.a * u_ColorBoost.a);
}
```

C#：

```csharp
if (IsStart)
{
    Entity.SetCustomShader("assets/shaders/wave_plane.vert", "assets/shaders/wave_plane.frag");
    Entity.SetCustomShaderColor("u_ColorBoost", 0.6f, 1.0f, 1.4f, 1.0f);
}

if (IsUpdate)
{
    float strength = 0.03f + 0.02f * MathF.Sin((float)Scene.Performance.TotalSeconds * 2.0f);
    Entity.SetCustomShaderFloat("u_WaveStrength", strength);
}
```

Python：

```python
def start(entity, scene, input, audio):
    entity.set_custom_shader("assets/shaders/wave_plane.vert", "assets/shaders/wave_plane.frag")
    entity.set_custom_shader_color("u_ColorBoost", 0.6, 1.0, 1.4, 1.0)

def update(entity, scene, input, audio, delta_seconds):
    strength = 0.03 + 0.02 * math.sin(scene.performance.total_seconds * 2.0)
    entity.set_custom_shader_float("u_WaveStrength", strength)
```

### PMX 示例

`assets/shaders/pmx_tint.vert`：

```glsl
#version 300 es

in vec3 in_Pos;
in vec3 in_Nor;
in vec2 in_UV;

uniform mat4 u_WVP;

out vec3 v_Normal;
out vec2 v_Uv;

void main()
{
    v_Normal = normalize(in_Nor);
    v_Uv = in_UV;
    gl_Position = u_WVP * vec4(in_Pos, 1.0);
}
```

`assets/shaders/pmx_tint.frag`：

```glsl
#version 300 es

precision highp float;

in vec3 v_Normal;
in vec2 v_Uv;

uniform sampler2D u_Texture;
uniform vec3 u_MaterialDiffuse;
uniform float u_MaterialAlpha;
uniform vec4 u_TintColor;

out vec4 out_Color;

void main()
{
    vec4 tex = texture(u_Texture, v_Uv);
    float light = 0.35 + 0.65 * max(dot(normalize(v_Normal), normalize(vec3(0.3, 0.8, 0.5))), 0.0);
    out_Color = vec4(tex.rgb * u_MaterialDiffuse * u_TintColor.rgb * light, tex.a * u_MaterialAlpha * u_TintColor.a);
    if (out_Color.a <= 0.001)
    {
        discard;
    }
}
```

C#：

```csharp
if (IsStart)
{
    Entity.EnableEdge = false;
    Entity.SetCustomShader("assets/shaders/pmx_tint.vert", "assets/shaders/pmx_tint.frag");
    Entity.SetCustomShaderColor("u_TintColor", 1.0f, 0.75f, 0.55f, 1.0f);
}
```

Python：

```python
def start(entity, scene, input, audio):
    entity.set_edge_enabled(False)
    entity.set_custom_shader("assets/shaders/pmx_tint.vert", "assets/shaders/pmx_tint.frag")
    entity.set_custom_shader_color("u_TintColor", 1.0, 0.75, 0.55, 1.0)
```
