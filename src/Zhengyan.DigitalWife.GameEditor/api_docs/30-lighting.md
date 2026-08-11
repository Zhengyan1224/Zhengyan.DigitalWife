---
id: lighting
title: 光照 API
category: 渲染
objects:
  - RuntimeLighting
  - RuntimePointLightCollection
  - RuntimeSpotLightCollection
  - RuntimeEntity
keywords:
  - lighting
  - directional light
  - ambient light
  - point light
  - spot light
  - spotlight
  - intensity
  - range
---

# 光照 API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 光照 API |
| 分类 | 渲染 |
| 主要对象 | `RuntimeLighting`, `RuntimePointLightCollection`, `RuntimeSpotLightCollection`, `RuntimeEntity` |
| C# 入口 | `Scene.Lighting`, `Scene.PointLights`, `Scene.SpotLights` |
| Python 入口 | `scene.lighting`, `scene.point_lights`, `scene.spot_lights` |
| 说明 | 动态控制平行光、环境光、点光源和射灯。 |

## 平行光和环境光

`Scene.Lighting` / `scene.lighting` 控制当前场景的全局平行光和环境光。修改会立即同步到当前已经加载的 PMX 模型，不需要重新加载场景。

| C# 属性 | Python 快照 | Python 修改方法 | 说明 |
| --- | --- | --- | --- |
| `DirectionalColor` | `directional_color` | `set_directional_color(r, g, b)` | 平行光 RGB 颜色。 |
| `DirectionalDirection` | `directional_direction` | `set_directional_direction(x, y, z)` | 平行光照射方向，不能是零向量；运行时会归一化。 |
| `AmbientColor` | `ambient_color` | `set_ambient_color(r, g, b)` | 环境光 RGB 颜色。 |
| `AmbientStrength` | `ambient_strength` | `set_ambient_strength(value)` | 环境光强度，必须为非负数。 |

C# 同时提供 `LightColor` 和 `LightDirection` 兼容别名。颜色值会限制为非负数，向量和强度必须是有限数值。

C#：

```csharp
if (IsStart)
{
    Scene.Lighting.SetDirectionalColor(1.0f, 0.92f, 0.78f);
    Scene.Lighting.SetDirectionalDirection(-0.4f, -1.0f, -0.25f);
    Scene.Lighting.SetAmbientColor(0.55f, 0.62f, 0.75f);
    Scene.Lighting.AmbientStrength = 0.35f;
}

if (IsUpdate)
{
    float phase = (float)Scene.Performance.TotalSeconds * 0.2f;
    Scene.Lighting.DirectionalDirection =
        Vector3.Normalize(new Vector3(MathF.Cos(phase), -1.0f, MathF.Sin(phase)));
}
```

Python：

```python
def start(entity, scene, input, audio):
    scene.lighting.set_directional_color(1.0, 0.92, 0.78)
    scene.lighting.set_directional_direction(-0.4, -1.0, -0.25)
    scene.lighting.set_ambient_color(0.55, 0.62, 0.75)
    scene.lighting.set_ambient_strength(0.35)
```

Python 的 `directional_color`、`directional_direction`、`ambient_color` 和 `ambient_strength` 是当前事件开始时的快照。调用修改方法后，下一次事件快照会包含新值。

## 点光源编辑器配置

在 Assets 面板的 Lighting 区域点击 `Add Point Light`。点光源是 `point_light` 类型的普通场景实体，位置使用实体 `Transform.Position`，其余参数位于 Inspector 的 Point Light 区域。

| 参数 | C# 实体属性 | Python 快照 | 说明 |
| --- | --- | --- | --- |
| `Enabled` | `PointLightEnabled` | `point_light_enabled` | 是否参与照明。 |
| `Color` | `PointLightColor` | `point_light_color` | RGB 光色。 |
| `Intensity` | `PointLightIntensity` | `point_light_intensity` | 光照强度，必须为非负数。 |
| `Range` | `PointLightRange` | `point_light_range` | 世界空间作用半径，必须大于零。 |
| `Position` | `Position` | `position` | 点光源的世界空间位置。 |

编辑器预览会绘制灯泡线框，选中时还会显示作用范围。GamePlayer 不绘制这些辅助线。

## C# 点光源

```csharp
if (IsStart)
{
    RuntimeEntity lamp = Scene.PointLights.Add(
        "Desk Lamp",
        new Vector3(0.0f, 2.2f, 1.0f),
        new Vector3(1.0f, 0.55f, 0.25f),
        intensity: 2.0f,
        range: 6.0f);

    lamp.PointLightEnabled = true;
    lamp.PointLightIntensity = 2.5f;
    lamp.PointLightRange = 8.0f;
    lamp.SetPointLightColor(1.0f, 0.7f, 0.4f);
    lamp.SetPosition(0.0f, 2.5f, 1.0f);
}
```

查询和删除：

```csharp
RuntimeEntity? lamp = Scene.PointLights.Get("Desk Lamp");
if (lamp is not null)
{
    lamp.PointLightEnabled = false;
    Scene.PointLights.Remove(lamp.Id);
}

int count = Scene.PointLights.Count;
IEnumerable<RuntimeEntity> lights = Scene.PointLights.All;
Scene.PointLights.Clear();
```

## Python 点光源

```python
def start(entity, scene, input, audio):
    lamp = scene.point_lights.add(
        "Desk Lamp",
        position=(0.0, 2.2, 1.0),
        color=(1.0, 0.55, 0.25),
        intensity=2.0,
        range=6.0,
        enabled=True)

    lamp.set_point_light_intensity(2.5)
    lamp.set_point_light_range(8.0)
    lamp.set_point_light_color(1.0, 0.7, 0.4)
    lamp.set_position(0.0, 2.5, 1.0)
```

查询和删除：

```python
lamp = scene.point_lights.get("Desk Lamp")
if lamp is not None:
    lamp.set_point_light_enabled(False)
    scene.point_lights.remove(lamp)

count = scene.point_lights.count
lights = scene.point_lights.all
scene.point_lights.clear()
```

## 渲染限制与扩展

- OpenGL 与 Vulkan 使用相同的平行光、环境光和点光源参数。
- 当前每次 PMX 绘制最多使用前 16 个已启用且参数有效的点光源；场景可以保存更多光源，但超出的光源不会进入该次绘制。
- 当前点光源影响 PMX 主材质，不改变无光照粒子、UI、调试线和 2D Sprite。
- `CastShadows` / `PointLightCastsShadows` 已在数据和脚本契约中预留，但当前版本不生成点光源阴影贴图。
- 自定义 GLSL 可读取 `u_LightColor`、`u_LightDir`、`u_AmbientLightColor`、`u_AmbientLightStrength`、`u_PointLightCount`、`u_PointLightPositionRange[16]` 和 `u_PointLightColorIntensity[16]`。
- 自定义 Vulkan SPIR-V 可从 `PmxFrame` 读取相同的全局光照和点光源数据；点光源字段追加在原有字段末尾，旧字段偏移未改变。

## 射灯

在 Editor 的 Assets -> Lighting 中可以创建 `spot_light` 实体。实体的位置和旋转决定射灯原点与方向，局部 `-Z` 轴是射灯照射方向。Editor 预览窗口会绘制灯头标识和内外锥体，GamePlayer 不会绘制这些辅助图案。

| 参数 | C# RuntimeEntity | Python Entity | 说明 |
| --- | --- | --- | --- |
| 启用 | `SpotLightEnabled` | `spot_light_enabled`, `set_spot_light_enabled(value)` | 是否参与照明。 |
| 方向 | `SpotLightDirection`, `SetSpotLightDirection(x,y,z)` | `spot_light_direction`, `set_spot_light_direction(x,y,z)` | 非零的世界空间方向。 |
| 颜色 | `SpotLightColor`, `SetSpotLightColor(r,g,b)` | `spot_light_color`, `set_spot_light_color(r,g,b)` | 非负 RGB 光色。 |
| 强度 | `SpotLightIntensity` | `spot_light_intensity`, `set_spot_light_intensity(value)` | 必须为非负数。 |
| 范围 | `SpotLightRange` | `spot_light_range`, `set_spot_light_range(value)` | 必须大于零。 |
| 内锥角 | `SpotLightInnerConeAngleDegrees` | `spot_light_inner_cone_angle_degrees`, `set_spot_light_inner_cone_angle(value)` | 单侧半角，单位为度。 |
| 外锥角 | `SpotLightOuterConeAngleDegrees` | `spot_light_outer_cone_angle_degrees`, `set_spot_light_outer_cone_angle(value)` | 单侧半角，必须大于内锥角。 |
| 阴影 | `SpotLightCastsShadows` | `spot_light_casts_shadows` | 仅预留，当前不渲染射灯阴影。 |

### C# 射灯集合

```csharp
RuntimeEntity torch = Scene.SpotLights.Add(
    "Torch",
    new Vector3(0.0f, 2.0f, 1.0f),
    new Vector3(0.0f, -0.25f, -1.0f),
    new Vector3(1.0f, 0.75f, 0.35f),
    intensity: 4.0f,
    range: 14.0f,
    innerConeAngleDegrees: 10.0f,
    outerConeAngleDegrees: 22.0f);
torch.SpotLightEnabled = true;
torch.SetSpotLightDirection(0.0f, -0.4f, -1.0f);
```

`Scene.SpotLights.Get`、`Remove`、`Clear`、`Count` 和 `All` 的用法与点光源集合相同。

### Python 射灯集合

```python
torch = scene.spot_lights.add(
    "Torch",
    position=(0.0, 2.0, 1.0),
    direction=(0.0, -0.25, -1.0),
    color=(1.0, 0.75, 0.35),
    intensity=4.0,
    range=14.0,
    inner_cone_angle_degrees=10.0,
    outer_cone_angle_degrees=22.0)
torch.set_spot_light_direction(0.0, -0.4, -1.0)
```

`scene.spot_lights.get`、`remove`、`clear`、`count` 和 `all` 均可用。OpenGL 与 Vulkan 每次 PMX 绘制最多使用 16 个参数有效的射灯。当前实现包含漫反射、高光、距离衰减和锥角平滑衰减，但暂不生成射灯阴影贴图。

自定义 GLSL 可读取 `u_SpotLightCount`、`u_SpotLightPositionRange[16]`、`u_SpotLightDirectionOuterCosine[16]`、`u_SpotLightColorIntensity[16]` 和 `u_SpotLightConeParameters[16]`。自定义 Vulkan SPIR-V 可从 `PmxFrame` 中读取对应字段；这些字段追加在点光源字段之后，已有字段偏移保持不变，也为后续射灯 shadow-map pass 保留了扩展位置。
