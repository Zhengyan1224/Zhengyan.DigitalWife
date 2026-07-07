---
id: input
title: Input 输入
category: 输入
objects:
  - RuntimeInput
  - input
  - TouchPoint
keywords:
  - input
  - keyboard
  - mouse
  - touch
  - gamepad
  - clipboard
  - cursor
  - ray
---

# Input 输入

## 结构化索引
| 项 | 内容 |
| --- | --- |
| 模块 | Input 输入 |
| 分类 | 输入 |
| 主要对象 | ``RuntimeInput``, ``input``, ``TouchPoint`` |
| C# 入口 | `Input.MouseX, Input.IsKeyDown, Input.SetCursorVisible, Scene.Camera.MousePointToRay` |
| Python 入口 | `input.mouse_x, input.is_key_down, input.set_cursor_visible, scene.camera.mouse_point_to_ray` |
| 说明 | 键盘、鼠标、鼠标光标显示、触屏、手柄、剪贴板，以及鼠标/触点生成射线。 |

## API 内容

`Input` 是当前帧输入状态快照。C# 脚本通过全局 `Input` 访问 `RuntimeInput`；Python 脚本通过事件参数 `input` 访问同一份快照。鼠标位置、触点位置、GUI/Sprite 命中测试和相机射线都使用窗口像素坐标：左上角为 `(0, 0)`，`X` 向右，`Y` 向下。

按键、鼠标按钮、手柄按钮和触点状态表示“当前这一帧是否处于某状态”。如果脚本需要“刚按下/刚松开”这样的单次触发，可以保存上一帧状态自行比较。触屏相关 API 在所有平台上名称一致：Windows 使用 `WM_POINTER`，Linux/X11 使用 XInput2 touch events，macOS 使用 Cocoa touch events；Wayland 暂时返回 `IsTouchAvailable == false` / `input.is_touch_available == False`。

### C# 快速总览
```csharp
Input.MouseX;
Input.MouseY;
Input.MouseDeltaX;
Input.MouseDeltaY;
Input.ScrollX;
Input.ScrollY;
Input.IsCursorVisible;
Input.CursorVisible = false;
Input.SetCursorVisible(true);
Input.ShowCursor();
Input.HideCursor();

Input.IsAltDown;
Input.IsControlDown;
Input.IsShiftDown;
Input.IsKeyDown("W");
Input.IsMouseButtonDown("left");

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

Input.IsTouchAvailable;
Input.HasTouch;
Input.TouchCount;
Input.ActiveTouchCount;
Input.IsTouchDown;
Input.IsTouchStarted;
Input.IsTouchEnded;
Input.PrimaryTouch;
Input.Touches;
Input.TryGetTouch(1, out TouchPoint touch);

Input.ClipboardText;
Input.HasClipboardText;
Input.TryGetClipboardText(out string clipboardText);
Input.SetClipboardText("copied text");
Input.TrySetClipboardText("copied text");
```

### Python 快速总览
```python
input.mouse_x
input.mouse_y
input.mouse_delta_x
input.mouse_delta_y
input.scroll_x
input.scroll_y
input.is_cursor_visible
input.cursor_visible
input.set_cursor_visible(False)
input.show_cursor()
input.hide_cursor()

input.alt_down
input.control_down
input.shift_down
input.is_key_down("w")
input.is_mouse_button_down("left")

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

input.is_touch_available
input.has_touch
input.touch_count
input.active_touch_count
input.is_touch_down
input.is_touch_started
input.is_touch_ended
input.primary_touch
input.touches
input.get_touch(touch_id)

input.clipboard_text
input.has_clipboard_text
input.set_clipboard_text("copied text")
```

## 鼠标与滚轮

| C# | Python | 类型 | 说明 |
| --- | --- | --- | --- |
| `Input.MouseX` | `input.mouse_x` | `float` | 鼠标当前窗口坐标 X。 |
| `Input.MouseY` | `input.mouse_y` | `float` | 鼠标当前窗口坐标 Y。 |
| `Input.MouseDeltaX` | `input.mouse_delta_x` | `float` | 当前帧与上一帧的鼠标 X 位移。 |
| `Input.MouseDeltaY` | `input.mouse_delta_y` | `float` | 当前帧与上一帧的鼠标 Y 位移。 |
| `Input.ScrollX` | `input.scroll_x` | `float` | 当前帧横向滚轮增量。 |
| `Input.ScrollY` | `input.scroll_y` | `float` | 当前帧纵向滚轮增量。 |
| `Input.IsCursorVisible` | `input.is_cursor_visible` / `input.cursor_visible` | `bool` | 鼠标光标当前是否显示。 |
| `Input.CursorVisible = value` | - | `bool` | C# 以属性形式显示或隐藏鼠标光标。 |
| `Input.SetCursorVisible(value)` | `input.set_cursor_visible(value)` | `void` | 显示或隐藏鼠标光标。 |
| `Input.TrySetCursorVisible(value)` | - | `bool` | C# 设置鼠标光标显示状态并返回是否成功。 |
| `Input.ShowCursor()` / `Input.HideCursor()` | `input.show_cursor()` / `input.hide_cursor()` | `void` | 快捷显示或隐藏鼠标光标。 |
| `Input.IsMouseButtonDown("left")` | `input.is_mouse_button_down("left")` | `bool` | 判断鼠标按钮是否按住。 |

常用鼠标按钮名：

- `left` / `mouseleft` / `button0` / `0`
- `right` / `mouseright` / `button1` / `1`
- `middle` / `mousemiddle` / `button2` / `2`

C# 也可以传入 Silk.NET `MouseButton` 枚举名称；Python 当前快照默认暴露 `left`、`right`、`middle`。

鼠标光标显示控制会影响 GamePlayer 窗口内的系统鼠标光标，不改变鼠标坐标、按钮状态或 GUI 命中测试。Python 调用会提交运行时命令，并同步更新本次脚本对象中的 `input.is_cursor_visible` / `input.cursor_visible` 快照字段。

### 鼠标光标显示示例
```csharp
if (IsStart)
{
    Input.HideCursor();
}

if (IsUpdate && Input.IsKeyDown("Escape"))
{
    Input.ShowCursor();
}
```

```python
def start(entity, scene, input, audio):
    input.hide_cursor()

def update(entity, scene, input, audio, delta_seconds):
    if input.is_key_down("escape"):
        input.show_cursor()
```

### 鼠标射线示例
```csharp
if (IsUpdate && Input.IsMouseButtonDown("left"))
{
    RuntimeRay ray = Scene.Camera.MousePointToRay(Input);
    Scene.Debug.DrawRay(ray.Origin, ray.Direction, 20.0f, durationSeconds: 0.05f);

    if (Scene.Camera.RaycastEntity(ray, out RuntimeRaycastHit hit))
    {
        hit.Entity.Speak("点中了 " + hit.Entity.Name);
    }
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    if not input.is_mouse_button_down("left"):
        return

    ray = scene.camera.mouse_point_to_ray(input)
    scene.debug.draw_ray(ray.origin, ray.direction, length=20.0, duration=0.05)
```

## 键盘

| C# | Python | 类型 | 说明 |
| --- | --- | --- | --- |
| `Input.IsKeyDown("W")` | `input.is_key_down("w")` | `bool` | 判断键位是否按住。 |
| `Input.IsAltDown` | `input.alt_down` | `bool` | 左/右 Alt 任意一个按住时为 `true`。 |
| `Input.IsControlDown` | `input.control_down` | `bool` | 左/右 Control 任意一个按住时为 `true`。 |
| `Input.IsShiftDown` | `input.shift_down` | `bool` | 左/右 Shift 任意一个按住时为 `true`。 |

常用键名：

- 字母：`A` 到 `Z`，例如 `W`、`A`、`S`、`D`
- 数字：`Number0` 到 `Number9`，也支持 `D0` 到 `D9`
- 方向键：`Up`、`Down`、`Left`、`Right`
- 功能键：`F1` 到 `F12`
- 控制键：`Space`、`Enter`、`Escape`、`Tab`、`Backspace`、`Delete`
- 修饰键：`ShiftLeft`、`ShiftRight`、`ControlLeft`、`ControlRight`、`AltLeft`、`AltRight`

C# 会按 Silk.NET `Key` 枚举名称解析传入字符串。Python 为了让跨进程事件快照更轻，当前默认探测这些常用键位；如果需要更多 Python 键位，需要在 `PythonScriptInstance.ProbedKeys` 中增加。

### 键盘移动示例
```csharp
if (IsUpdate)
{
    float speed = Input.IsShiftDown ? 6.0f : 3.0f;
    float dx = 0.0f;
    float dz = 0.0f;

    if (Input.IsKeyDown("A")) dx -= 1.0f;
    if (Input.IsKeyDown("D")) dx += 1.0f;
    if (Input.IsKeyDown("W")) dz -= 1.0f;
    if (Input.IsKeyDown("S")) dz += 1.0f;

    Entity.Translate(dx * speed * (float)DeltaSeconds, 0.0f, dz * speed * (float)DeltaSeconds);
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    speed = 6.0 if input.shift_down else 3.0
    dx = 0.0
    dz = 0.0

    if input.is_key_down("a"):
        dx -= 1.0
    if input.is_key_down("d"):
        dx += 1.0
    if input.is_key_down("w"):
        dz -= 1.0
    if input.is_key_down("s"):
        dz += 1.0

    entity.translate(dx * speed * delta_seconds, 0.0, dz * speed * delta_seconds)
```

## 手柄

| C# | Python | 类型 | 说明 |
| --- | --- | --- | --- |
| `Input.HasGamepad` | `input.has_gamepad` | `bool` | 当前是否有可用手柄。 |
| `Input.GamepadName` | `input.gamepad_name` | `string` | 当前主手柄名称；无手柄时为空字符串。 |
| `Input.GamepadIndex` | `input.gamepad_index` | `int` | 当前主手柄索引；无手柄时为 `-1`。 |
| `Input.LeftStickX` | `input.left_stick_x` | `float` | 左摇杆 X，通常范围为 `-1` 到 `1`。 |
| `Input.LeftStickY` | `input.left_stick_y` | `float` | 左摇杆 Y，通常范围为 `-1` 到 `1`。 |
| `Input.RightStickX` | `input.right_stick_x` | `float` | 右摇杆 X。 |
| `Input.RightStickY` | `input.right_stick_y` | `float` | 右摇杆 Y。 |
| `Input.LeftTrigger` | `input.left_trigger` | `float` | 左扳机值，通常范围为 `0` 到 `1`。 |
| `Input.RightTrigger` | `input.right_trigger` | `float` | 右扳机值，通常范围为 `0` 到 `1`。 |
| `Input.IsGamepadButtonDown("A")` | `input.is_gamepad_button_down("A")` | `bool` | 判断手柄按钮是否按住。 |

常用手柄按钮名：

- 面键：`A`、`B`、`X`、`Y`
- 肩键：`LeftBumper` / `lb` / `l1`，`RightBumper` / `rb` / `r1`
- 中央键：`Back` / `select`，`Start` / `options`，`Home` / `guide`
- 摇杆按压：`LeftStick` / `ls` / `l3`，`RightStick` / `rs` / `r3`
- 方向键：`DPadUp`、`DPadRight`、`DPadDown`、`DPadLeft`

### 手柄移动示例
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

    move_x = input.left_stick_x
    move_z = -input.left_stick_y
    entity.translate(move_x * 3.0 * delta_seconds, 0.0, move_z * 3.0 * delta_seconds)

    if input.is_gamepad_button_down("A"):
        entity.speak("手柄 A 被按住")
```

## 触屏 Touch

触屏 API 和鼠标一样使用窗口像素坐标。`Touches` / `input.touches` 包含当前帧的触点快照，并且会把 `Ended` / `Cancelled` 的触点保留一帧，方便脚本捕捉松手或取消。`PrimaryTouch` / `input.primary_touch` 优先返回仍处于活动状态的触点，再按触点 id 排序。

| C# | Python | 类型 | 说明 |
| --- | --- | --- | --- |
| `Input.IsTouchAvailable` | `input.is_touch_available` | `bool` | 当前平台和窗口后端是否提供真实触屏输入。 |
| `Input.HasTouch` | `input.has_touch` | `bool` | 当前帧是否有触点快照，包括保留一帧的结束/取消触点。 |
| `Input.TouchCount` | `input.touch_count` | `int` | 当前帧触点快照数量。 |
| `Input.ActiveTouchCount` | `input.active_touch_count` | `int` | 当前仍处于按下/移动/静止状态的触点数量。 |
| `Input.IsTouchDown` | `input.is_touch_down` | `bool` | `ActiveTouchCount > 0`。 |
| `Input.IsTouchStarted` | `input.is_touch_started` | `bool` | 当前帧是否有新开始的触点。 |
| `Input.IsTouchEnded` | `input.is_touch_ended` | `bool` | 当前帧是否有结束或取消的触点。 |
| `Input.PrimaryTouch` | `input.primary_touch` | `TouchPoint?` | 当前主触点；没有触点时为 `null` / `None`。 |
| `Input.Touches` | `input.touches` | `IReadOnlyList<TouchPoint>` / `list[TouchPoint]` | 当前帧所有触点快照。 |
| `Input.TryGetTouch(id, out touch)` | `input.get_touch(touch_id)` | `bool` / `TouchPoint or None` | 按触点 id 查找当前帧触点。 |

`TouchPoint` 字段：

| C# | Python | 类型 | 说明 |
| --- | --- | --- | --- |
| `touch.Id` | `touch.id` | `int` | 同一个手指按下期间保持稳定的触点 id。 |
| `touch.X` | `touch.x` | `float` | 触点窗口坐标 X。 |
| `touch.Y` | `touch.y` | `float` | 触点窗口坐标 Y。 |
| `touch.DeltaX` | `touch.delta_x` | `float` | 当前帧触点 X 位移。 |
| `touch.DeltaY` | `touch.delta_y` | `float` | 当前帧触点 Y 位移。 |
| `touch.Phase` | `touch.phase` | `TouchPhase` / `str` | `Started`、`Moved`、`Stationary`、`Ended`、`Cancelled`。Python 中是小写字符串。 |
| `touch.Kind` | `touch.kind` | `TouchInputKind` / `str` | `Touch`、`Pen`、`Unknown`。Python 中是小写字符串。 |
| `touch.Pressure` | `touch.pressure` | `float` | 压力值，当前后端通常使用 `1.0` 表示按下、`0.0` 表示结束。 |
| `touch.IsActive` | `touch.is_active` | `bool` | `Started` / `Moved` / `Stationary` 时为 `true`。 |
| `touch.IsEnded` | `touch.is_ended` | `bool` | `Ended` / `Cancelled` 时为 `true`。 |

触点阶段含义：

- `Started`：触点刚按下。
- `Moved`：触点本帧发生移动。
- `Stationary`：触点仍按住，但本帧没有明显移动。
- `Ended`：触点正常抬起，只保留当前帧。
- `Cancelled`：窗口失焦或系统取消触点，只保留当前帧。

### 触屏射线与 Sprite 示例
```csharp
if (IsUpdate && Input.IsTouchAvailable && Input.PrimaryTouch is { IsActive: true } touch)
{
    RuntimeSpriteControl? button = Scene.GetSprite("JumpButton");
    if (button is not null && button.ContainsTouch(Input))
    {
        Entity.Speak("触摸了跳跃按钮");
    }

    RuntimeRay ray = Scene.Camera.TouchPointToRay(touch);
    Scene.Debug.DrawRay(ray.Origin, ray.Direction, 12.0f, durationSeconds: 0.05f);
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    if not input.is_touch_available or input.primary_touch is None:
        return

    touch = input.primary_touch
    if not touch.is_active:
        return

    button = scene.get_sprite("JumpButton")
    if button is not None and button.contains_touch(input):
        entity.speak("触摸了跳跃按钮")

    ray = scene.camera.touch_point_to_ray(touch)
    scene.debug.draw_ray(ray.origin, ray.direction, length=12.0, duration=0.05)
```

### 多点触控示例
```csharp
if (IsUpdate && Input.ActiveTouchCount >= 2)
{
    TouchPoint first = Input.Touches.First(touch => touch.IsActive);
    TouchPoint second = Input.Touches.Where(touch => touch.IsActive).Skip(1).First();

    float distance = MathF.Sqrt(
        MathF.Pow(first.X - second.X, 2.0f) +
        MathF.Pow(first.Y - second.Y, 2.0f));

    Entity.Speak("双指距离 " + distance.ToString("0"));
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    active = [touch for touch in input.touches if touch.is_active]
    if len(active) < 2:
        return

    first = active[0]
    second = active[1]
    distance = ((first.x - second.x) ** 2 + (first.y - second.y) ** 2) ** 0.5
    entity.speak(f"双指距离 {distance:.0f}")
```

## 剪贴板

| C# | Python | 类型 | 说明 |
| --- | --- | --- | --- |
| `Input.ClipboardText` | `input.clipboard_text` | `string` | C# 访问时读取当前系统剪贴板文本；Python 是事件分发时的快照。 |
| `Input.HasClipboardText` | `input.has_clipboard_text` | `bool` | 当前是否读取到非空剪贴板文本。 |
| `Input.TryGetClipboardText(out text)` | - | `bool` | C# 区分“剪贴板为空”和“读取失败”。 |
| `Input.SetClipboardText(text)` | `input.set_clipboard_text(text)` | `void` | 写入系统剪贴板。Python 会提交写入命令，并同步更新本次脚本对象中的快照字段。 |
| `Input.TrySetClipboardText(text)` | - | `bool` | C# 写入剪贴板并返回是否成功。 |

### 剪贴板示例
```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    RuntimeGuiControl? textbox = Scene.GetGuiControl("PromptInput");
    if (textbox is not null && textbox.HasSelection)
    {
        Input.SetClipboardText(textbox.SelectedText);
    }
}

if (IsUpdate && Input.IsKeyDown("V") && Input.IsControlDown)
{
    RuntimeGuiControl? textbox = Scene.GetGuiControl("PromptInput");
    textbox?.ReplaceSelection(Input.ClipboardText);
}
```

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    textbox = scene.get_gui_control("PromptInput")
    if textbox is not None and textbox.has_selection:
        input.set_clipboard_text(textbox.selected_text)

def update(entity, scene, input, audio, delta_seconds):
    if input.control_down and input.is_key_down("v"):
        textbox = scene.get_gui_control("PromptInput")
        if textbox is not None:
            textbox.replace_selection(input.clipboard_text)
```

## 常见组合

### 鼠标优先，触屏兜底
```csharp
if (IsUpdate)
{
    RuntimeRay? ray = null;

    if (Input.IsMouseButtonDown("left"))
    {
        ray = Scene.Camera.MousePointToRay(Input);
    }
    else if (Input.PrimaryTouch is { IsActive: true } touch)
    {
        ray = Scene.Camera.TouchPointToRay(touch);
    }

    if (ray is RuntimeRay value)
    {
        Scene.Debug.DrawRay(value.Origin, value.Direction, 16.0f, durationSeconds: 0.05f);
    }
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    ray = None

    if input.is_mouse_button_down("left"):
        ray = scene.camera.mouse_point_to_ray(input)
    elif input.primary_touch is not None and input.primary_touch.is_active:
        ray = scene.camera.touch_point_to_ray(input.primary_touch)

    if ray is not None:
        scene.debug.draw_ray(ray.origin, ray.direction, length=16.0, duration=0.05)
```

### 输入方式自适应移动
```csharp
if (IsUpdate)
{
    float moveX = 0.0f;
    float moveZ = 0.0f;

    if (Input.HasGamepad)
    {
        moveX = Input.LeftStickX;
        moveZ = -Input.LeftStickY;
    }
    else
    {
        if (Input.IsKeyDown("A")) moveX -= 1.0f;
        if (Input.IsKeyDown("D")) moveX += 1.0f;
        if (Input.IsKeyDown("W")) moveZ -= 1.0f;
        if (Input.IsKeyDown("S")) moveZ += 1.0f;
    }

    Entity.Translate(moveX * 4.0f * (float)DeltaSeconds, 0.0f, moveZ * 4.0f * (float)DeltaSeconds);
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    move_x = 0.0
    move_z = 0.0

    if input.has_gamepad:
        move_x = input.left_stick_x
        move_z = -input.left_stick_y
    else:
        if input.is_key_down("a"):
            move_x -= 1.0
        if input.is_key_down("d"):
            move_x += 1.0
        if input.is_key_down("w"):
            move_z -= 1.0
        if input.is_key_down("s"):
            move_z += 1.0

    entity.translate(move_x * 4.0 * delta_seconds, 0.0, move_z * 4.0 * delta_seconds)
```
