---
id: graphics-quality
title: 运行时画质与 MSAA
category: 渲染
objects:
  - RuntimeGraphicsQuality
  - RuntimeShadowQuality
  - RuntimeEntity
keywords:
  - Graphics
  - MSAA
  - shadow
  - reflection
  - quality
  - 阴影
  - 反射
---

# 运行时画质与 MSAA

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 运行时画质与 MSAA |
| 分类 | 渲染 |
| 主要对象 | `RuntimeGraphicsQuality`, `RuntimeShadowQuality`, `RuntimeEntity` |
| C# 入口 | `Graphics`, `Scene.Graphics`, `Entity.ReflectionSamples` |
| Python 入口 | `scene.graphics`, `scene.graphics.shadows`, `entity.set_reflection_samples(...)` |
| 说明 | 在帧边界动态修改场景采样、阴影质量以及平面和水面反射 MSAA。 |

## 平台与生命周期

C# 的入口和参数语义在 PC OpenGL/Vulkan、Android OpenGL ES/Vulkan、WebGL2 上一致。
直接实现 `IWebGameScript` 的 Web 脚本可使用 `context.Graphics` 或 `context.Scene.Graphics`。
Python 使用 PC 端已有的脚本桥接，Android/Web 的 Python 支持范围仍遵循原有平台限制。

全局画质对象在一个项目运行期间跨场景保留，修改不会写回 `game.project.json`。
实体反射级别属于当前场景实体；重新加载实体后恢复该实体的场景配置。
如需长期保存玩家设置，可用 `Scene.Save` 保存数值，再在启动脚本中恢复。

## 全局场景采样

| C# | Python 快照 / 修改方法 | 说明 |
| --- | --- | --- |
| `Graphics.SceneSamples` | `scene.graphics.scene_samples` / `set_scene_samples(value)` | 请求的场景 MSAA：1、2、4、8、16。 |
| `Graphics.RequestedSceneSamples` | `scene.graphics.scene_samples` | 请求值的只读别名。 |
| `Graphics.ActualSceneSamples` | `scene.graphics.actual_scene_samples` | 最近一帧实际使用的采样数。 |
| `Graphics.Pending` | `scene.graphics.pending` | 全局修改尚未由渲染端应用／确认。 |

设置会在接下来的渲染帧边界应用，不会在正在绘制的通道中重建资源。
同一帧内的多次设置合并为最终配置；重复设置相同值不会反复重建。
设备不支持请求级别时，采用不超过请求值的最高可用级别，最低为 1。
Android 的明确 low / medium 档位仍分别限制场景采样到 1 / 2。
Web 的实际值在浏览器完成绘制并反馈后更新，可能比请求晚一个回调。

## 全局阴影质量

设置任一阴影参数会自动启用运行时全局阴影设置。
`UseGlobalSettings = false` 表示恢复平台默认质量，灯光和模型的阴影开关继续独立生效。

| C#（`Graphics.Shadows.` 后） | Python（`scene.graphics.shadows.` 后） | 范围／含义 |
| --- | --- | --- |
| `UseGlobalSettings` | `use_global_settings` / `set_use_global_settings(value)` | 使用这组全局质量参数。 |
| `DirectionalResolution` | `directional_resolution` / `set_directional_resolution(value)` | 256、512、1024、2048、4096、8192。 |
| `LocalLightResolution` | `local_light_resolution` / `set_local_light_resolution(value)` | 点光源每面和聚光灯图集区域：256、512、1024、2048。 |
| `FilterSamples` | `filter_samples` / `set_filter_samples(value)` | PCF 过滤位置数：1、4、9、16、25、49。 |
| `FilterRadius` | `filter_radius` / `set_filter_radius(value)` | 0～8 个阴影贴图像素。 |
| `ActualDirectionalResolution` | `actual_directional_resolution` | 当前方向光阴影实际边长；未生成时为 0。 |
| `ActualLocalLightResolution` | `actual_local_light_resolution` | 当前局部光图集每格的实际边长；未生成时为 0。 |

实际分辨率受设备纹理能力、可用帧缓冲和 Android 质量档位限制。
采样数表示过滤位置数，与场景 MSAA 无关。半径为 0 时过滤位置重合；一个过滤位置不受半径影响。
非标准分辨率和过滤位置数向下归入支持档位，MSAA 非标准值沿用引擎现有的归一化规则。
半径限制到 0～8，非有限值使用 1。

## 平面与水面反射

两种表面共用以下实体入口：

| C# | Python | 说明 |
| --- | --- | --- |
| `surface.ReflectionSamples` | `surface.reflection_samples` / `surface.set_reflection_samples(value)` | 请求的反射 MSAA：1、2、4、8、16。 |
| `surface.ActualReflectionSamples` | `surface.actual_reflection_samples` | 最近绘制的实际采样级别；关闭、不可见或超出反射预算时为 0。 |

反射实际级别不超过场景实际级别，并受设备颜色／深度附件能力限制。
场景采样下降时反射自动下降，原请求值保留；场景恢复后可以自动恢复反射级别。
仍需通过编辑器／原有反射开关启用表面反射。

## C# 示例

```csharp
if (IsStart)
{
    Graphics.SceneSamples = 4;
    Graphics.Shadows.DirectionalResolution = 2048;
    Graphics.Shadows.LocalLightResolution = 1024;
    Graphics.Shadows.FilterSamples = 25;
    Graphics.Shadows.FilterRadius = 2.0f;

    var mirror = Scene.GetEntity("Mirror");
    var water = Scene.GetEntity("Water");
    if (mirror != null) mirror.ReflectionSamples = 4;
    if (water != null) water.ReflectionSamples = 2;
}
```

请求值和实际值可以不同。后续事件中可读取：

```csharp
int requested = Graphics.SceneSamples;
int actual = Graphics.ActualSceneSamples;
bool waiting = Graphics.Pending;
```

可绑定到画质按钮的完整示例见 [graphics-quality.csx](../samples/graphics-quality.csx)。

## Python 示例

```python
def start(entity, scene, input, audio):
    scene.graphics.set_scene_samples(4)
    scene.graphics.shadows.set_directional_resolution(2048)
    scene.graphics.shadows.set_local_light_resolution(1024)
    scene.graphics.shadows.set_filter_samples(25)
    scene.graphics.shadows.set_filter_radius(2.0)
    mirror = scene.get_entity("Mirror")
    if mirror is not None:
        mirror.set_reflection_samples(4)
```

Python 属性是当前事件开始时的快照，修改必须调用上述方法；新值在后续事件快照中读取。
按钮示例见 [graphics-quality.py](../samples/graphics-quality.py)。

## 性能

画质配置快照是值类型，渲染端每帧消费一次；重复请求和稳定配置不会重复分配纹理。
OpenGL/GLES/Web 在实际场景采样与窗口采样相同时使用直接绘制，否则解析离屏场景并用缓存的全屏三角形显示。
Vulkan 复用按输出采样级别缓存的绘制管线，淘汰资源随对应 GPU 帧完成后释放。
Runtime RenderTexture 和水下捕获也会跟随显式设置的场景采样；水下捕获需要额外解析深度。

阴影过滤次数和半径只更新着色器数据；分辨率、场景或反射 MSAA 切换可能产生短暂的帧耗时增加。
建议在画质按钮、场景加载或有滞回阈值的自适应策略中修改，避免每帧来回切换级别。
