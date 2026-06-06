---
id: scene
title: Scene API
category: 对象
objects:
  - RuntimeScene
  - scene
keywords:
  - scene
  - RuntimeScene
  - GetEntity
  - LoadScene
---

# Scene API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Scene API |
| 分类 | 对象 |
| 主要对象 | ``RuntimeScene``, ``scene`` |
| C# 入口 | `Scene.GetEntity/GetGuiControl/GetSprite` |
| Python 入口 | `scene.get_entity/get_gui_control/get_sprite` |
| 说明 | 场景对象、实体/GUI/Sprite 查找、子系统入口和场景切换。 |

## API 内容

`Scene` 是当前场景运行时入口。

C#：

```csharp
Console.WriteLine(Scene.Name);

RuntimeEntity? player = Scene.GetEntity("Player");
RuntimeGuiControl? button = Scene.GetGuiControl("StartButton");
RuntimeSpriteControl? icon = Scene.GetSprite("Icon");

foreach (RuntimeEntity item in Scene.Entities)
{
    Console.WriteLine(item.Name);
}

Scene.LoadScene("scenes/next.scene.json");
```

Python：

```python
print(scene.name)

player = scene.get_entity("Player")
button = scene.get_gui_control("StartButton")
icon = scene.get_sprite("Icon")

scene.load_scene("scenes/next.scene.json")
scene.flush()
```

属性和方法：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Name` | `scene.name` | 当前场景名。 |
| `Scene.Entities` | `scene.get_entity(...)` | C# 可枚举所有实体；Python 用查找函数获取实体。 |
| `Scene.GuiControls` | `scene.get_gui_control(...)` | C# 可枚举 GUI；Python 用查找函数。 |
| `Scene.Sprites` | `scene.get_sprite(...)` | C# 可枚举 2D 精灵；Python 用查找函数。 |
| `Scene.Window` | `scene.window` | 窗口控制。 |
| `Scene.Runtime` | `scene.runtime` | 运行时项目设置控制。 |
| `Scene.Camera` | `scene.camera` | 相机控制。 |
| `Scene.Debug` | `scene.debug` | 调试绘制。 |
| `Scene.Save` | `scene.save` | 存档读写。 |
| `Scene.Bubble` | `scene.bubble` | 运行时对话气泡 / 提示气泡系统。 |
| `Scene.Llm` | `scene.llm` | LLM / OpenAI-compatible 文本对话。 |
| `Scene.Asr` | `scene.asr` | 本地麦克风录音和 ASR 识别。 |
| `Scene.RealtimeVoice` | `scene.realtime_voice` | `RealtimeVoice` 远端语音服务调用。 |
| `Scene.Network` | `scene.network` | HTTP/HTTPS、TCP 和 UDP 网络通信。 |
| `Scene.Performance` | `scene.performance` | 性能指标快照，例如 FPS。 |
| `Scene.Fps` | `scene.fps` | 平滑后的当前 FPS 快捷属性。 |
| `Scene.RawFps` | `scene.raw_fps` | 当前帧瞬时 FPS。 |
| `Scene.RenderTexture(name)` | `scene.render_texture(name)` | 返回 `rt:name` 引用。 |
| `Scene.LoadScene(path)` | `scene.load_scene(path)` | 切换场景。 |
| 无 | `scene.flush()` | Python 专用，立即提交当前函数内已累计的引擎命令。 |

`path` 是工程相对的场景文件路径，通常来自 GameEditor 的 `Scenes` 面板，例如 `scenes/main.scene.json` 或 `scenes/battle.scene.json`。场景切换会显示同一套加载遮罩，并触发目标场景的加载入口脚本。
