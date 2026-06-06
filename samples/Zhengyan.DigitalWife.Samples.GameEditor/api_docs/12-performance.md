---
id: performance
title: Performance / FPS API
category: 调试
objects:
  - RuntimePerformance
  - scene.performance
keywords:
  - performance
  - fps
  - delta
---

# Performance / FPS API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Performance / FPS API |
| 分类 | 调试 |
| 主要对象 | ``RuntimePerformance``, ``scene.performance`` |
| C# 入口 | `Scene.Performance.Fps` |
| Python 入口 | `scene.performance.fps` |
| 说明 | FPS、帧时间、运行时性能状态和显示示例。 |

## API 内容

脚本可以实时读取当前帧率。`Fps` 是平滑后的 FPS，适合显示给玩家；`RawFps` 是当前帧根据 `DeltaSeconds` 算出的瞬时 FPS，波动更大，适合调试。

C#：

```csharp
if (IsUpdate)
{
    RuntimeGuiControl? fpsLabel = Scene.GetGuiControl("FPS Label");
    fpsLabel?.SetValue($"FPS: {Scene.Fps:F1}");

    if (Scene.Fps < 30.0)
    {
        Console.WriteLine($"Low FPS: {Scene.Fps:F1}, raw={Scene.RawFps:F1}");
    }
}
```

Python：

```python
def update(entity, scene, input, audio, delta_seconds):
    fps_label = scene.get_gui_control("FPS Label")
    if fps_label:
        fps_label.set_value(f"FPS: {scene.fps:.1f}")

    if scene.performance.fps < 30:
        print("Low FPS", scene.performance.fps, "raw", scene.performance.raw_fps)
```

API：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Fps` | `scene.fps` | 平滑后的 FPS。 |
| `Scene.RawFps` | `scene.raw_fps` | 当前帧瞬时 FPS。 |
| `Scene.DeltaSeconds` | `scene.delta_seconds` | 当前帧间隔秒数。 |
| `Scene.FrameCount` | `scene.frame_count` | 当前运行帧编号。 |
| `Scene.Performance.Fps` | `scene.performance.fps` | 同 `Scene.Fps`。 |
| `Scene.Performance.RawFps` | `scene.performance.raw_fps` | 同 `Scene.RawFps`。 |
| `Scene.Performance.DeltaSeconds` | `scene.performance.delta_seconds` | 同 `Scene.DeltaSeconds`。 |
| `Scene.Performance.TotalSeconds` | `scene.performance.total_seconds` | 游戏运行总秒数。 |
| `Scene.Performance.FrameCount` | `scene.performance.frame_count` | 同 `Scene.FrameCount`。 |
