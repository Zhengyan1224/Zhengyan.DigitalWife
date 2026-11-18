---
id: gui
title: GUI API
category: GUI
objects:
  - RuntimeGuiControl
  - GuiControlSettings
  - ContextMenuSettings
  - ContextMenuItemSettings
  - ContextMenuTargetSettings
  - ContextMenuTargetInfo
keywords:
  - gui
  - button
  - textbox
  - SelectedText
  - ReplaceSelection
  - context menu
  - right click
---

# GUI API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | GUI API |
| 分类 | GUI |
| 主要对象 | ``RuntimeGuiControl``, ``GuiControlSettings``, ``ContextMenuSettings``, ``ContextMenuItemSettings`` |
| C# 入口 | `Scene.GetGuiControl, RuntimeGuiControl` |
| Python 入口 | `scene.get_gui_control` |
| 说明 | GUI 控件类型、属性、样式、文本选区、修改方法和事件。 |

## API 内容

GUI 控件类型：

| 类型 | 默认事件 | 说明 |
| --- | --- | --- |
| `button` | `clicked` | 按钮。 |
| `label` | 无 | 文本标签。支持自动换行。 |
| `checkbox` | `changed` | 复选框。 |
| `dropdown` | `changed` | 下拉框。 |
| `textbox` | `changed` | 文本输入框。输入内容保存在 `Text` / `Value`，支持单行或多行。 |
| `progress_bar` | 无 | 进度条。脚本通过 `Progress` / `set_progress(...)` 控制进度，范围 `0.0` 到 `1.0`。 |

按钮控件除了 `clicked` 外，还会额外派发：

- `pressed`：鼠标按下按钮的那一帧
- `released`：鼠标松开按钮的那一帧

这两个事件适合做“按住录音、松开结束”的 push-to-talk 场景。

GUI 控件属性：

| C# 属性 | Python 快照 | 说明 |
| --- | --- | --- |
| `Id` | `id` | 控件 Id。 |
| `Name` | `name` | 控件名称。 |
| `Type` | `type` | `button`、`label`、`checkbox`、`dropdown`、`textbox`、`progress_bar`。 |
| `Text` | `text` | 显示文本；对 `textbox` 表示当前输入内容。 |
| `Value` | `value` | `Text` 的别名，便于读取文本框输入。 |
| `SelectedText` | `selected_text` | 文本框当前选中的文本。 |
| `HasSelection` | `has_selection` | 文本框当前是否存在选区。 |
| `SelectionStart` / `SelectionEnd` | `selection_start` / `selection_end` | 文本框当前选区起止位置；无选区时两者通常等于光标位置。 |
| `SelectionLength` | `selection_length` | 文本框当前选区长度。 |
| `CursorPosition` | `cursor_position` | 文本框当前光标位置。 |
| `Visible` | `visible` | 是否显示。 |
| `X` / `Y` | `x` / `y` | 屏幕像素坐标。 |
| `Width` / `Height` | `width` / `height` | 控件尺寸。 |
| `LayoutMode` | `layout_mode` | `absolute` 或 `relative`。`relative` 会按项目窗口基准分辨率缩放坐标、尺寸和字体大小。 |
| `TargetEntity` | 无 | 事件目标实体，在编辑器里通常用下拉选择。 |
| `EventName` | 无 | 事件名。 |
| `Checked` | `checked` | 复选框状态。 |
| `Progress` | `progress` | 进度条进度，范围 `0.0` 到 `1.0`。 |
| `WordWrap` | `word_wrap` | 文本自动换行。 |
| `Multiline` | `multiline` | 文本框是否使用多行输入。 |
| `Items` | `items` | 下拉框项目。 |
| `SelectedIndex` | `selected_index` | 下拉框选中项下标。 |
| `SelectedItem` | 无 | C# 可直接取当前选中项。 |
| `ReplaceSelection(text)` | `replace_selection(text)` | 用文本替换当前选区；无选区时在当前光标位置插入。 |

GUI 样式：

| Style 字段 | 说明 |
| --- | --- |
| `Background` / `Hover` / `Active` / `Text` / `Border` | 控件背景、悬停、按下、文字和边框颜色。 |
| `Border thickness` | 边框粗细。 |
| `Rounding` | 圆角半径。 |
| `Font size` | 控件字体大小，单位为像素，默认 `18.0`。GameEditor 预览和 GamePlayer 运行时都会按该值显示。 |
| `Horizontal align` | 水平对齐：`left`、`center`、`right`。 |
| `Vertical align` | 垂直对齐：`top`、`middle`、`bottom`。 |

修改 GUI：

```csharp
RuntimeGuiControl? control = Scene.GetGuiControl("StartButton");
if (control is not null)
{
    control.Text = "开始";
    control.SetPosition(40, 80);
    control.SetSize(180, 40);
    control.SetValue("默认输入");
    control.SetMultiline(true);
    control.SetWordWrap(true);
    control.SetLayoutMode("relative");
    control.SetFontSize(24.0f);
    control.Show();
}
```

```python
control = scene.get_gui_control("StartButton")
if control is not None:
    control.set_text("开始")
    control.set_position(40, 80)
    control.set_size(180, 40)
    control.set_value("默认输入")
    control.set_multiline(True)
    control.set_word_wrap(True)
    control.set_layout_mode("relative")
    control.set_font_size(24.0)
    control.show()
```

按钮点击控制角色说话：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    if (GuiControlName == "Start Button")
    {
        Entity.Speak("按钮被点击了");
    }
}
```

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name == "clicked" and control_name == "Start Button":
        entity.speak("按钮被点击了")
```

控制复选框和下拉框：

```csharp
RuntimeGuiControl? quality = Scene.GetGuiControl("QualityDropdown");
quality?.SetItems("Low", "Medium", "High");
quality?.SetSelectedIndex(2);

RuntimeGuiControl? mute = Scene.GetGuiControl("MuteCheckbox");
if (mute is not null && mute.Checked)
{
    Audio.SetVolume("BGM", 0.0f);
}
```

```python
quality = scene.get_gui_control("QualityDropdown")
if quality is not None:
    quality.set_items(["Low", "Medium", "High"])
    quality.set_selected_index(2)

mute = scene.get_gui_control("MuteCheckbox")
if mute is not None and mute.checked:
    audio.set_volume("BGM", 0.0)
```

控制进度条：

```csharp
RuntimeGuiControl? hp = Scene.GetGuiControl("HP Bar");
if (hp is not null)
{
    hp.SetProgress(0.75f);
    hp.Text = "HP 75%";
    hp.SetLayoutMode("relative");
}
```

```python
hp = scene.get_gui_control("HP Bar")
if hp is not None:
    hp.set_progress(0.75)
    hp.set_text("HP 75%")
    hp.set_layout_mode("relative")
```

读取文本框输入：

```csharp
if (IsGuiEvent && GuiEventName == "changed")
{
    RuntimeGuiControl? inputBox = Scene.GetGuiControl(GuiControlId);
    if (inputBox is not null && inputBox.Type == "textbox")
    {
        Console.WriteLine($"用户输入: {inputBox.Value}");
        Entity.Speak(inputBox.Value);
    }
}
```

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name == "changed":
        input_box = scene.get_gui_control(control_id)
        if input_box is not None and input_box.type == "textbox":
            print("用户输入:", input_box.value)
```

文本框在 GamePlayer 中使用 ImGui `InputText` / `InputTextMultiline`，输入法和键盘处理走 ImGui.NET + Silk.NET，Windows、Linux、macOS 使用同一套代码路径。Linux 发行版缺少系统 CJK 字体时，程序会优先使用内置 `Resources/Fonts/NotoSansCJKsc-Regular.otf`。

文本框还会暴露 `SelectedText` / `SelectionStart` / `SelectionEnd` / `CursorPosition`。可以配合 `Input.SetClipboardText(...)` / `input.set_clipboard_text(...)` 实现脚本级复制；用 `ReplaceSelection(...)` / `replace_selection(...)` 可以按当前选区或光标位置做粘贴。

样式配置在 GameEditor 中编辑，包括背景色、悬停色、按下色、文字色、边框色、边框宽度、圆角、水平对齐、垂直对齐。

### 右键菜单

GameEditor 的 GUI Controls 面板中可以添加 `Context Menus`。PC、Android、Web 使用同一份场景配置：PC/Web 鼠标右键打开菜单，Android 外接鼠标也支持右键；Android 和移动 Web 支持触屏长按。普通模式与桌面精灵模式均适用。

右键菜单配置：

| 字段 | 说明 |
| --- | --- |
| `Id` | 菜单 Id。点击菜单项时会作为 `GuiControlId` 传给脚本。 |
| `Name` | 菜单名称。点击菜单项时会作为 `GuiControlName` 传给脚本。 |
| `Enabled` | 是否启用该菜单。 |
| `Target type` | `window`、`gui_control`、`sprite`、`entity`。 |
| `Targets` / `Add Target` | 可添加多个目标；`gui_control` 选择多个 GUI 控件，`sprite` 选择多个 Sprite，`entity` 选择多个实体。没有目标行或类型为 `window` 时表示整个窗口；已添加但未选择对象的目标行不会命中。 |
| 每个实体的 `Target colliders` | 支持勾选多个 Collider，点击 `All colliders` 清除限制。某个实体的列表为空时，该实体所有 Collider 有效；其他实体各自的限制独立计算。 |
| `Layout mode` | `absolute` 或 `relative`，用于菜单宽度、内边距、字体大小等缩放。 |
| `Width` | 菜单宽度。 |
| `Item height` | 菜单项高度。 |
| `Padding X` / `Padding Y` | 菜单内边距。 |
| `Style` | 背景色、悬停色、按下色、文字色、边框、圆角、字体大小等，和普通 GUI 控件样式一致。 |
| `Items` | 菜单项列表。每个菜单项有 `Id`、`Text`、`Enabled`、`Script event`。 |

目标命中规则：

| Target type | 命中方式 |
| --- | --- |
| `window` | 右击窗口任意位置弹出。桌面精灵点击穿透模式下，透明区域不会把鼠标事件交给 GamePlayer，因此透明区域不会弹出。 |
| `gui_control` | 命中任一绑定 GUI 控件的矩形范围。 |
| `sprite` | 命中任一绑定 2D Sprite 的旋转矩形范围。 |
| `entity` | 用相机射线拾取最近的 Collider，再匹配实体和该实体的 Collider 列表。无 Collider 限制时也允许 PC 的无启用碰撞体 PMX 球形兜底；有明确 Collider 限制时兜底不能命中。 |

场景 JSON 的多目标格式如下。`targetId` 和 Collider 值都可以使用对象 ID 或名称，不区分大小写。`gui_control`、`sprite` 也使用相同的 `targets` 列表，忽略 `targetColliders`。

```json
{
  "targetType": "entity",
  "targets": [
    { "targetId": "ActorA", "targetColliders": ["BodyCollider", "HeadCollider"] },
    { "targetId": "ActorB", "targetColliders": [] }
  ]
}
```

旧场景的单目标 `TargetId` / `TargetCollider` 继续有效；配置了新 `Targets` 列表时以列表为准。编辑器修改菜单后会保存为新格式。

右键菜单项点击会复用 GUI 事件通道：

| 脚本字段 | 值 |
| --- | --- |
| `IsGuiEvent` | `true` |
| `GuiControlId` | 右键菜单 `Id` |
| `GuiControlName` | 右键菜单 `Name` |
| `GuiEventName` | 菜单项 `Script event` |
| `IsContextMenuEvent` | `true`；普通 GUI 事件、Start/Update 为 `false` |
| `ContextMenuTarget` | `ContextMenuTargetInfo`，本次打开菜单时命中的对象快照；普通事件为 `null` |

`ContextMenuTarget` 在打开菜单时保存，点击菜单项后不会重新拾取。移动鼠标到菜单、对象移动或者共用菜单的另一个对象改变位置，都不会改变当前事件的触发对象。

| ContextMenuTarget 属性 | 说明 |
| --- | --- |
| `TargetType` | 实际命中类型：`window`、`gui_control`、`sprite`、`entity`。 |
| `TargetId` / `TargetName` | 实际命中对象的 ID / 名称；窗口菜单为空。即使用名称绑定，返回的 `TargetId` 也是对象真实 ID。 |
| `EntityId` / `EntityName` | `entity` 时是命中实体；GUI/Sprite 时是该对象配置的 `TargetEntity`。未绑定实体或窗口菜单为空，脚本接收者仍可使用默认回退。 |
| `ColliderId` / `ColliderName` | 实体实际命中的 Collider；窗口、GUI、Sprite 或 PMX 球形兜底为空。 |
| `ColliderType` | 实体碰撞形状，例如 `box`、`capsule`；PMX 球形兜底为 `fallback_sphere`，非实体为空。 |
| `PointerX` / `PointerY` | 打开菜单时画布/游戏窗口中的像素坐标，不是菜单项点击位置，也不是桌面屏幕坐标。 |

事件目标：

- 绑定多个 `entity` 时，事件发送给本次实际命中实体的脚本。
- 绑定多个 `gui_control` 时，优先发送给本次实际命中 GUI 控件的 `Target entity`。
- 绑定多个 `sprite` 时，优先发送给本次实际命中 Sprite 的 `Target entity`。
- 绑定为空或 `window` 时，使用普通 GUI 事件的默认脚本目标回退规则。

C# 示例（PC、Android、Web 通用，菜单项的 Script event 配置为 `inspect_target` 或 `hide_target`）：

```csharp
if (IsContextMenuEvent && ContextMenuTarget is { } hit)
{
    // GuiControlId/Name 是菜单；hit.TargetId/Name 才是打开菜单的对象。
    if (GuiEventName == "inspect_target")
    {
        Scene.Save.WriteText("last-menu-target.txt",
            $"{hit.TargetType}: {hit.TargetId} ({hit.TargetName}), collider={hit.ColliderName}");
    }
    else if (GuiEventName == "hide_target")
    {
        switch (hit.TargetType)
        {
            case "entity": Scene.GetEntity(hit.TargetId)?.Hide(); break;
            case "gui_control": Scene.GetGuiControl(hit.TargetId)?.Hide(); break;
            case "sprite": Scene.GetSprite(hit.TargetId)?.Hide(); break;
        }
    }
}
```

PC Python 示例：`gui_event` 可增加第八个参数 `context_menu_target`，其属性名和 C# 一致。原来的六/七参数回调继续有效，也可直接读取模块全局 `IsContextMenuEvent` / `ContextMenuTarget`。

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name,
              context_menu_target=None):
    if context_menu_target is None:
        return
    hit = context_menu_target
    if event_name == "inspect_target":
        print(hit.TargetType, hit.TargetId, hit.TargetName, hit.ColliderName)
    elif event_name == "hide_target" and hit.TargetType == "entity":
        target = scene.get_entity(hit.TargetId)
        if target is not None:
            target.hide()
```

Web Python 使用全局事件入口：

```python
if IsContextMenuEvent and ContextMenuTarget is not None:
    hit = ContextMenuTarget
    if GuiEventName == "inspect_target":
        Scene.Save.WriteText("last-menu-target.txt", hit.TargetId)
    elif GuiEventName == "hide_target" and hit.TargetType == "entity":
        target = Scene.GetEntity(hit.TargetId)
        if target is not None:
            target.Hide()
```

Android 使用上述 C# 脚本入口。Web 直接实现 `IWebGameScript` 时，在 `OnEvent` / `OnEventAsync` 中读取 `gameEvent.IsContextMenuEvent` 和 `gameEvent.ContextMenuTarget`。
