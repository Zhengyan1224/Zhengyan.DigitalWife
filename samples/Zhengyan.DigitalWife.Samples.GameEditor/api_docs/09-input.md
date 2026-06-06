---
id: input
title: Input 输入
category: 输入
objects:
  - RuntimeInput
  - input
keywords:
  - input
  - keyboard
  - mouse
  - gamepad
  - clipboard
---

# Input 输入

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Input 输入 |
| 分类 | 输入 |
| 主要对象 | ``RuntimeInput``, ``input`` |
| C# 入口 | `Input.IsKeyDown, Input.SetClipboardText` |
| Python 入口 | `input.is_key_down, input.set_clipboard_text` |
| 说明 | 键盘、鼠标、手柄、剪贴板和鼠标射线输入。 |

## API 内容

`Input` 是当前帧输入状态快照。当前 API 提供的是“是否按住”，不是“按下瞬间”。如果需要单次触发，需要脚本自己保存上一帧状态。

C# 属性和方法：

```csharp
Input.MouseX;
Input.MouseY;
Input.MouseDeltaX;
Input.MouseDeltaY;
Input.ScrollX;
Input.ScrollY;
Input.IsAltDown;
Input.IsControlDown;
Input.IsShiftDown;
Input.HasGamepad;
Input.GamepadName;
Input.GamepadIndex;
Input.LeftStickX;
Input.LeftStickY;
Input.RightStickX;
Input.RightStickY;
Input.LeftTrigger;
Input.RightTrigger;
Input.IsGamepadButtonDown("A");
Input.IsGamepadButtonDown("LeftBumper");
Input.ClipboardText;
Input.HasClipboardText;
Input.TryGetClipboardText(out string clipboardText);
Input.SetClipboardText("copied text");
Input.TrySetClipboardText("copied text");
Input.IsKeyDown("W");
Input.IsKeyDown("Space");
Input.IsMouseButtonDown("left");
```

Python 属性和方法：

```python
input.mouse_x
input.mouse_y
input.mouse_delta_x
input.mouse_delta_y
input.scroll_x
input.scroll_y
input.alt_down
input.control_down
input.shift_down
input.has_gamepad
input.gamepad_name
input.gamepad_index
input.left_stick_x
input.left_stick_y
input.right_stick_x
input.right_stick_y
input.left_trigger
input.right_trigger
input.is_gamepad_button_down("A")
input.is_gamepad_button_down("LeftBumper")
input.clipboard_text
input.has_clipboard_text
input.set_clipboard_text("copied text")
input.is_key_down("w")
input.is_key_down("Space")
input.is_mouse_button_down("left")
```

`Input.ClipboardText` 会在 C# 脚本访问时读取当前原始剪贴板文本，也支持直接赋值；`input.clipboard_text` 是 Python 事件分发时附带的剪贴板快照。读取失败或当前为空时返回空字符串。C# 还可以用 `TryGetClipboardText(...)` 区分“空文本”和“不可用”，用 `SetClipboardText(...)` / `TrySetClipboardText(...)` 写回系统剪贴板；Python 使用 `input.set_clipboard_text(...)`。

手柄状态以“当前主手柄快照”的形式暴露。`HasGamepad` / `input.has_gamepad` 为 `true` 时，脚本可以读取手柄名、索引、左右摇杆 XY、左右扳机值，并通过 `IsGamepadButtonDown(...)` / `input.is_gamepad_button_down(...)` 判断按钮状态。

常用键名：

- 字母：`A` 到 `Z`，例如 `W`、`A`、`S`、`D`。
- 数字：`Number0` 到 `Number9`，也支持 `D0` 到 `D9`。
- 方向键：`Up`、`Down`、`Left`、`Right`。
- 功能键：`F1` 到 `F12`。
- 控制键：`Space`、`Enter`、`Escape`、`Tab`、`Backspace`、`Delete`。

常用手柄按钮名：

- 面键：`A`、`B`、`X`、`Y`
- 肩键：`LeftBumper` / `lb` / `l1`，`RightBumper` / `rb` / `r1`
- 中键：`Back` / `select`，`Start`
- 摇杆按压：`LeftStick` / `ls` / `l3`，`RightStick` / `rs` / `r3`
- 方向键：`DPadUp`、`DPadRight`、`DPadDown`、`DPadLeft`

手柄输入示例：

```csharp
if (IsUpdate && Input.HasGamepad)
{
    float moveX = Input.LeftStickX;
    float moveZ = -Input.LeftStickY;
    Entity.Translate(moveX * 3.0f * (float)DeltaSeconds, 0.0f, moveZ * 3.0f * (float)DeltaSeconds);

    if (Input.IsGamepadButtonDown("A"))
    {
        Entity.Speak("手柄 A 被按住");
    }
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    if not input.has_gamepad:
        return

    entity.translate(input.left_stick_x * 3.0 * delta_seconds, 0.0, -input.left_stick_y * 3.0 * delta_seconds)
    if input.is_gamepad_button_down("A"):
        entity.speak("手柄 A 被按住")
```
- 修饰键：`ShiftLeft`、`ShiftRight`、`ControlLeft`、`ControlRight`、`AltLeft`、`AltRight`。
- 鼠标：`left`、`right`、`middle`，也支持 `button0`、`button1`、`button2`。

Python 当前默认探测的键位包括 `W A S D Q E R F Z X C V Space Enter Escape Tab Backspace Delete Up Down Left Right Number0-Number9 D0-D9 F1-F12 ShiftLeft ShiftRight ControlLeft ControlRight AltLeft AltRight`。如果需要更多 Python 键位，需要在 `PythonScriptInstance.ProbedKeys` 中增加。

鼠标点击射线：

```csharp
if (IsUpdate && Input.IsMouseButtonDown("left"))
{
    RuntimeRay ray = Scene.Camera.MousePointToRay(Input);
    Scene.Debug.DrawRay(ray.Origin, ray.Direction, 20.0f, durationSeconds: 0.05f);
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    if input.is_mouse_button_down("left"):
        ray = scene.camera.mouse_point_to_ray(input)
        scene.debug.draw_ray(ray.origin, ray.direction, length=20.0, duration=0.05)
```
