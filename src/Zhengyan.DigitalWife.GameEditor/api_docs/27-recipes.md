---
id: recipes
title: 常见脚本组合示例
category: 示例
objects:
  - Recipes
keywords:
  - recipe
  - example
  - gui
  - pick
---

# 常见脚本组合示例

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 常见脚本组合示例 |
| 分类 | 示例 |
| 主要对象 | ``Recipes`` |
| C# 入口 | `multiple APIs` |
| Python 入口 | `multiple APIs` |
| 说明 | 点击 GUI、拾取对象、下拉框切换动作等常见组合示例。 |

## API 内容

点击 GUI 后说话，结束后切换场景：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    Entity.SpeakWithCallback("准备切换场景", "go_next");
}

if (IsSpeechEvent && SpeechCallbackName == "go_next")
{
    Scene.LoadScene("scenes/next.scene.json");
}
```

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name == "clicked":
        entity.speak("准备切换场景", on_completed="go_next")

def go_next(entity, scene, input, audio):
    scene.load_scene("scenes/next.scene.json")
```

鼠标拾取实体并显示命中射线：

```csharp
if (IsUpdate && Input.IsMouseButtonDown("left"))
{
    RuntimeRay ray = Scene.Camera.MousePointToRay(Input);
    Scene.Debug.DrawRay(ray.Origin, ray.Direction, 30.0f, durationSeconds: 0.05f);

    if (Scene.Camera.RaycastEntity(ray, out RuntimeRaycastHit hit))
    {
        Console.WriteLine($"hit {hit.Entity.Name}");
    }
}
```

```python
left_was_down = False

def update(entity, scene, input, audio, delta_seconds):
    global left_was_down
    left = input.is_mouse_button_down("left")
    if left and not left_was_down:
        ray = scene.camera.mouse_point_to_ray(input)
        scene.debug.draw_ray(ray.origin, ray.direction, length=30, duration=0.5)
        hit = entity.raycast(ray)
        if hit is not None:
            print("hit current entity", hit)
    left_was_down = left
```

根据下拉框选择切换动作：

```csharp
if (IsGuiEvent && GuiEventName == "changed")
{
    RuntimeGuiControl? motion = Scene.GetGuiControl(GuiControlId);
    if (motion?.SelectedItem == "Wave")
    {
        Entity.ApplyMotion("assets/motions/wave.vmd");
    }
    else if (motion?.SelectedItem == "Idle")
    {
        Entity.ApplyMotion("assets/motions/idle.vmd");
    }
}
```

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "changed":
        return

    control = scene.get_gui_control(control_id)
    if control is None:
        return

    if control.selected_index == 0:
        entity.apply_motion("assets/motions/idle.vmd")
    elif control.selected_index == 1:
        entity.apply_motion("assets/motions/wave.vmd")
```
