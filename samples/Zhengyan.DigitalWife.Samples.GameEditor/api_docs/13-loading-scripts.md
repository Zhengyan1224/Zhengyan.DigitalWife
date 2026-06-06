---
id: loading
title: 场景加载入口脚本
category: 事件
objects:
  - Loading scripts
keywords:
  - loading
  - LoadingProgress
  - loading_started
---

# 场景加载入口脚本

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 场景加载入口脚本 |
| 分类 | 事件 |
| 主要对象 | ``Loading scripts`` |
| C# 入口 | `IsLoadingEvent, LoadingProgress` |
| Python 入口 | `loading_started/progress/completed` |
| 说明 | 场景加载脚本生命周期、进度事件和加载遮罩交互。 |

## API 内容

每个场景可以在 GameEditor 的 `Scene Loading Scripts` 里绑定加载脚本。它们在加载遮罩期间执行，生命周期分三段：

- `loading_started`：场景 JSON 已读取、运行时加载脚本已准备好。
- `loading_progress`：每个加载步骤后触发，包含进度和消息。
- `loading_completed`：加载队列执行完成，遮罩即将移除。

C#：

```csharp
if (IsLoadingEvent && LoadingEventName == "loading_started")
{
    Console.WriteLine($"开始加载 {Scene.Name}");
}

if (IsLoadingEvent && LoadingEventName == "loading_progress")
{
    Console.WriteLine($"{LoadingProgress:P0} {LoadingMessage}");
}

if (IsLoadingEvent && LoadingEventName == "loading_completed")
{
    Console.WriteLine("加载完成");
}
```

Python：

```python
def loading_started(entity, scene, input, audio, progress, message):
    print("开始加载", scene.name)

def loading_progress(entity, scene, input, audio, progress, message):
    print(progress, message)

def loading_completed(entity, scene, input, audio, progress, message):
    print("加载完成")
```

加载界面背景色、背景图、背景图透明度和加载进度条在 GameEditor 的场景属性里配置。加载进度条支持：

- `Visible`：是否显示。
- `Layout mode`：`relative` 或 `absolute`。`relative` 会以 1280x720 的默认加载界面基准做缩放，适配实际窗口尺寸。
- `Position` / `Size`：进度条位置和大小。
- `Background` / `Track` / `Fill` / `Border`：外框、轨道、填充和边框颜色。
- `Padding` / `Border thickness`：内边距和边框粗细。

场景加载进度由 GamePlayer 的资源加载流程自动更新；加载入口脚本可以读取 `LoadingProgress` 和 `LoadingMessage`，但不需要手动设置系统加载进度条。
