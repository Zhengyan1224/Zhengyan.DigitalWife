---
id: sprite
title: 2D Sprite API
category: GUI
objects:
  - RuntimeSpriteControl
  - SpriteSettings
keywords:
  - sprite
  - 2d
  - texture
  - render texture
  - clicked
---

# 2D Sprite API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 2D Sprite API |
| 分类 | GUI |
| 主要对象 | ``RuntimeSpriteControl``, ``SpriteSettings`` |
| C# 入口 | `Scene.GetSprite, RuntimeSpriteControl` |
| Python 入口 | `scene.get_sprite` |
| 说明 | 2D Sprite 属性、布局、贴图、Render Texture 引用和自动指针事件。 |

## API 内容

2D Sprite 支持场景背景和 GUI 前景两种绘制阶段，适合背景图、HUD 图标、头像、提示图和 Render Texture 预览。

属性：

| C# 属性 | Python 快照 | 说明 |
| --- | --- | --- |
| `Id` | `id` | 精灵 Id。 |
| `Name` | `name` | 精灵名称。 |
| `Path` / `Texture` | `path` / `texture` | 图片路径或 `rt:<name>`。 |
| `X` / `Y` | `x` / `y` | 屏幕像素坐标。 |
| `Width` / `Height` | `width` / `height` | 尺寸。无论绝对还是相对布局都沿用这两个字段；当 `layout_mode = "relative"` 时，它们会和 `X / Y` 一起按项目窗口参考分辨率缩放。 |
| `LayoutMode` | `layout_mode` | `absolute` 或 `relative`。相对布局时会像 GUI 控件一样基于项目窗口参考分辨率缩放位置和尺寸。 |
| `RotationDegrees` | `rotation_degrees` | 旋转角度。Python 当前主要用于读取和命中判断。 |
| `Opacity` | `opacity` | 透明度。 |
| `DrawOrder` | 无 | 绘制顺序。小于 `0` 时在 3D 场景之前绘制，可作为背景图；大于等于 `0` 时在 3D 场景之后绘制。相同阶段内数值越大越靠前。 |
| `Visible` | `visible` | 是否显示。 |
| `SetLayoutMode(value)` | `set_layout_mode(value)` | 切换布局模式。 |
| `GetScreenRect()` | 无 | C# 获取当前已经换算到屏幕像素空间的矩形。 |
| `ContainsPoint(x, y)` | `contains_point(x, y)` | 判断屏幕坐标点是否落在 Sprite 内，支持旋转后的矩形命中。 |
| `ContainsMouse(Input)` | `contains_mouse(input)` | 判断当前鼠标是否在 Sprite 内。 |

C#：

```csharp
RuntimeSpriteControl? portrait = Scene.GetSprite("Portrait");
if (portrait is not null)
{
    portrait.SetPosition(24, 420);
    portrait.SetSize(160, 160);
    portrait.SetLayoutMode("relative");
    portrait.Opacity = 0.9f;
    portrait.Texture = "assets/textures/portrait.png";
    portrait.Show();

    if (portrait.ContainsMouse(Input) && Input.IsMouseButtonDown("left"))
    {
        Console.WriteLine("Portrait clicked.");
    }
}
```

Python：

```python
portrait = scene.get_sprite("Portrait")
if portrait is not None:
    portrait.set_position(24, 420)
    portrait.set_size(160, 160)
    portrait.set_layout_mode("relative")
    portrait.set_opacity(0.9)
    portrait.set_texture("assets/textures/portrait.png")
    portrait.show()
    if portrait.contains_mouse(input) and input.is_mouse_button_down("left"):
        print("Portrait clicked")
```

使用 Render Texture：

```csharp
Scene.Camera.BindRenderTextureCamera("MiniMapRT", "MiniMap Camera");
Scene.GetSprite("MiniMap")?.SetRenderTexture("MiniMapRT");
```

```python
scene.camera.bind_render_texture_camera("MiniMapRT", "MiniMap Camera")
mini_map = scene.get_sprite("MiniMap")
if mini_map is not None:
    mini_map.set_render_texture("MiniMapRT")
```

说明：

- `absolute`：`X / Y / Width / Height` 直接按当前屏幕像素使用。
- `relative`：`X / Y / Width / Height` 会按 `Project.Window.Width / Height` 作为参考分辨率缩放，行为和 GUI 控件一致。
- `DrawOrder < 0`：Sprite 写入场景颜色缓冲，绘制在天空盒之后、其他 3D 场景内容之前；它也会参与水下后处理，并按整张游戏画面布局后裁剪到各个相机视口。
- `DrawOrder >= 0`：Sprite 保持原有 GUI 前景行为，显示在 3D 模型上方。
- `ContainsPoint(...)` / `contains_point(...)` 会把布局模式和旋转角度一起考虑进去，不是简单的未旋转矩形判断。

自动 Sprite 事件：

- 如果你在 GameEditor 的 Sprite 面板里给某个 Sprite 设置了 `Target entity`，GamePlayer 会自动把这个 Sprite 的指针事件派发到目标实体脚本。
- 当前内建事件名：`entered`、`exited`、`pressed`、`released`、`clicked`。
- 事件只会发给当前鼠标命中的最上层 Sprite，叠放时会优先 `DrawOrder` 更高的那一个。
- 自动事件主要面向鼠标左键；如果你需要更复杂的交互，可以继续配合 `ContainsMouse(...)` / `contains_mouse(...)` 自己写。

```csharp
if (IsSpriteEvent && SpriteName == "Portrait")
{
    if (SpriteEventName == "entered")
    {
        Console.WriteLine("Mouse entered portrait");
    }
    else if (SpriteEventName == "exited")
    {
        Console.WriteLine("Mouse exited portrait");
    }
    else if (SpriteEventName == "pressed")
    {
        Entity.Speak("按下头像");
    }
    else if (SpriteEventName == "clicked")
    {
        Entity.Speak("点击头像");
    }
}
```

```python
def sprite_event(entity, scene, input, audio, sprite_id, sprite_name, event_name):
    if sprite_name != "Portrait":
        return

    if event_name == "entered":
        print("Mouse entered portrait")
    elif event_name == "exited":
        print("Mouse exited portrait")
    elif event_name == "pressed":
        entity.speak("按下头像")
    elif event_name == "clicked":
        entity.speak("点击头像")
```
