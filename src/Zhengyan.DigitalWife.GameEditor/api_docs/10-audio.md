---
id: audio
title: Audio 音频 / 背景音乐
category: 音频
objects:
  - RuntimeAudio
  - audio
keywords:
  - audio
  - bgm
  - volume
  - loop
---

# Audio 音频 / 背景音乐

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Audio 音频 / 背景音乐 |
| 分类 | 音频 |
| 主要对象 | ``RuntimeAudio``, ``audio`` |
| C# 入口 | `Audio.Play/Pause/Stop/SetVolume` |
| Python 入口 | `audio.play/pause/stop/set_volume` |
| 说明 | 音频播放、暂停、停止、音量和循环控制。 |

## API 内容

音频资源在 GameEditor 的 Audio 区域添加，支持 `.wav` 和 `.ogg`。保存项目时会复制到工程目录。运行时脚本通过音频资源的 `Name` 或 `Path` 控制播放。

编辑器中的音频资源属性：

- `Name`：脚本推荐用这个名称控制音频，例如 `BGM`。
- `Path`：音频路径，例如 `assets/audio/bgm.ogg`。
- `Loop`：是否循环，适合背景音乐。
- `Volume`：初始音量。
- `PlayOnStart`：场景启动后自动播放。

脚本可用 API：

| C# | Python | 说明 |
| --- | --- | --- |
| `Audio.Play(nameOrPath)` | `audio.play(name)` | 播放音频。 |
| `Audio.Pause(nameOrPath)` | `audio.pause(name)` | 暂停音频。 |
| `Audio.Stop(nameOrPath)` | `audio.stop(name)` | 停止音频。 |
| `Audio.SetVolume(nameOrPath, volume)` | `audio.set_volume(name, volume)` | 设置音量，通常 `0.0` 到 `1.0`。 |
| `Audio.SetLoop(nameOrPath, loop)` | `audio.set_loop(name, loop)` | 设置运行时是否循环播放。 |
| `Audio.GetLoop(nameOrPath)` | 无 | 读取当前运行时循环状态，找不到音频时返回 `false`。 |

播放背景音乐：

```csharp
if (IsStart)
{
    Audio.SetVolume("BGM", 0.7f);
    Audio.SetLoop("BGM", true);
    Audio.Play("BGM");
}
```

```python
def start(entity, scene, input, audio):
    audio.set_volume("BGM", 0.7)
    audio.set_loop("BGM", True)
    audio.play("BGM")
```

按键开关 BGM：

```csharp
static class State
{
    public static bool MWasDown;
    public static bool Muted;
}

if (IsUpdate)
{
    bool down = Input.IsKeyDown("M");
    if (down && !State.MWasDown)
    {
        State.Muted = !State.Muted;
        Audio.SetVolume("BGM", State.Muted ? 0.0f : 0.7f);
    }

    State.MWasDown = down;
}
```

```python
muted = False
m_was_down = False

def update(entity, scene, input, audio, delta_seconds):
    global muted, m_was_down
    down = input.is_key_down("M")
    if down and not m_was_down:
        muted = not muted
        audio.set_volume("BGM", 0.0 if muted else 0.7)
    m_was_down = down
```

播放音效：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    Audio.Play("ClickSfx");
}
```

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name == "clicked":
        audio.play("ClickSfx")
```

运行时切换循环：

```csharp
if (IsGuiEvent && GuiControlName == "Loop BGM" && GuiEventName == "changed")
{
    RuntimeGuiControl? checkbox = Scene.GetGuiControl(GuiControlId);
    if (checkbox is not null)
    {
        Audio.SetLoop("BGM", checkbox.Checked);
    }
}
```

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if control_name == "Loop BGM" and event_name == "changed":
        checkbox = scene.get_gui_control(control_id)
        if checkbox is not None:
            audio.set_loop("BGM", checkbox.checked)
```

当前音频脚本边界：

- 脚本不能动态加载未在项目里登记的音频源。
- 脚本可以修改当前运行时 `Loop` 状态；它不会回写到 `game.project.json`。
- 脚本不能修改 `PlayOnStart`；这是场景加载时行为，在编辑器里配置。
- `Play(name)` 找不到名称时会静默忽略，不会抛异常。
