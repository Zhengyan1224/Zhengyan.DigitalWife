---
id: lifecycle
title: 生命周期
category: 事件
objects:
  - CSharpScriptGlobals
  - Python event functions
keywords:
  - lifecycle
  - event
  - IsStart
  - IsUpdate
  - tray_menu_event
---

# 生命周期

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 生命周期 |
| 分类 | 事件 |
| 主要对象 | ``CSharpScriptGlobals``, ``Python event functions`` |
| C# 入口 | `IsStart, IsUpdate, IsGuiEvent, IsTrayMenuEvent` |
| Python 入口 | `start, update, gui_event, tray_menu_event` |
| 说明 | 脚本事件派发、全局变量、Python 回调函数和跨帧状态。 |

## API 内容

### C# 生命周期

C# 脚本每次事件都会执行同一个 `.csx` 文件，通过全局布尔变量区分事件：

```csharp
if (IsStart)
{
    Console.WriteLine($"start: {Entity.Name}");
}

if (IsUpdate)
{
    Entity.RotateY(30.0f * (float)DeltaSeconds);
}

if (IsGuiEvent)
{
    Console.WriteLine($"{GuiControlName} ({GuiControlId}) -> {GuiEventName}");
}

if (IsLoadingEvent)
{
    Console.WriteLine($"{LoadingEventName}: {LoadingProgress:P0} {LoadingMessage}");
}

if (IsSpeechEvent)
{
    Console.WriteLine($"speech callback: {SpeechCallbackName}");
}

if (IsLlmEvent)
{
    Console.WriteLine($"{LlmCallbackName}: {LlmText}");
}

if (IsAsrEvent)
{
    Console.WriteLine($"{AsrCallbackName}: {AsrEventName} -> {AsrText}");
}

if (IsRealtimeVoiceEvent)
{
    Console.WriteLine($"{RealtimeVoiceCallbackName}: {RealtimeVoiceEventName} -> {RealtimeVoiceText}");
}

if (IsTrayMenuEvent)
{
    Console.WriteLine($"tray: {TrayMenuItemText} ({TrayMenuItemId}) -> {TrayMenuEventName}");
}
```

C# 全局变量：

| 名称 | 类型 | 说明 |
| --- | --- | --- |
| `Entity` | `RuntimeEntity` | 当前脚本绑定的实体。场景加载脚本使用内部加载实体。 |
| `Scene` | `RuntimeScene` | 当前场景运行时对象。 |
| `Input` | `RuntimeInput` | 当前帧输入快照。 |
| `Audio` | `RuntimeAudio` | 音频源控制器。 |
| `DeltaSeconds` | `double` | 本帧间隔秒数，仅 `IsUpdate` 时有意义。 |
| `IsStart` | `bool` | 实体脚本启动事件。 |
| `IsUpdate` | `bool` | 每帧更新事件。 |
| `IsGuiEvent` | `bool` | GUI 控件事件。 |
| `GuiControlId` | `string` | 触发事件的 GUI 控件 Id。 |
| `GuiControlName` | `string` | 触发事件的 GUI 控件 Name，适合脚本按可读名称判断控件。 |
| `GuiEventName` | `string` | GUI 事件名，例如 `clicked`、`changed`。 |
| `IsSpriteEvent` | `bool` | 2D Sprite 指针事件。 |
| `SpriteId` | `string` | 触发事件的 Sprite Id。 |
| `SpriteName` | `string` | 触发事件的 Sprite 名称。 |
| `SpriteEventName` | `string` | Sprite 事件名，例如 `entered`、`exited`、`pressed`、`released`、`clicked`。 |
| `IsTrayMenuEvent` | `bool` | 桌面精灵系统托盘菜单事件。 |
| `TrayMenuItemId` | `string` | 被点击的托盘菜单项 Id。 |
| `TrayMenuItemText` | `string` | 被点击的托盘菜单项显示文本。 |
| `TrayMenuEventName` | `string` | GameEditor 中为该菜单项配置的脚本事件名。 |
| `IsLoadingEvent` | `bool` | 场景加载入口脚本事件。 |
| `LoadingEventName` | `string` | `loading_started`、`loading_progress`、`loading_completed`。 |
| `LoadingProgress` | `float` | 加载进度，范围 `0.0` 到 `1.0`。 |
| `LoadingMessage` | `string` | 当前加载步骤文本。 |
| `IsSpeechEvent` | `bool` | TTS 播放完成回调事件。 |
| `SpeechCallbackName` | `string` | `SpeakWithCallback` 传入的回调名。 |
| `IsLlmEvent` | `bool` | LLM 后台流式请求回调事件。 |
| `LlmEvent` | `RuntimeLlmScriptEvent?` | LLM 回调事件完整对象。 |
| `LlmRequestId` | `string` | LLM 请求 Id。 |
| `LlmEventName` | `string` | LLM 事件名，通常为 delta、completed 或 error 对应事件。 |
| `LlmDelta` | `string` | 本次流式增量文本。 |
| `LlmText` | `string` | 当前累计文本。 |
| `LlmIsFinal` | `bool` | 当前回调是否为最终事件。 |
| `LlmError` | `string` | 错误文本；仅错误回调通常有值。 |
| `LlmCallbackName` | `string` | `StartChat(...)` 传入的回调名。 |
| `IsAsrEvent` | `bool` | ASR 后台回调事件。 |
| `AsrEvent` | `RuntimeAsrScriptEvent?` | ASR 回调事件完整对象。 |
| `AsrRequestId` | `string` | ASR 请求 Id。 |
| `AsrEventName` | `string` | 事件名：`partial`、`completed`、`error`。 |
| `AsrText` | `string` | 当前识别文本。 |
| `AsrIsFinal` | `bool` | 当前 ASR 事件是否最终事件。 |
| `AsrError` | `string` | ASR 错误文本。 |
| `AsrCallbackName` | `string` | ASR 回调名。 |
| `AsrOffsetSeconds` | `double` | 当前文本对应的音频时长偏移秒数。 |
| `IsRealtimeVoiceEvent` | `bool` | Realtime Voice 后台回调事件。 |
| `RealtimeVoiceEvent` | `RuntimeRealtimeVoiceScriptEvent?` | Realtime Voice 回调事件完整对象。 |
| `RealtimeVoiceRequestId` | `string` | 语音请求 Id。 |
| `RealtimeVoiceEventName` | `string` | 事件名，例如 `wake_word_detected`、`transcription_completed`、`delta`、`completed`、`speech_completed`、`error`。 |
| `RealtimeVoiceText` | `string` | 当前事件的主文本。唤醒词事件通常是去掉唤醒词后的尾文本；转写事件是用户文本；完成事件是最终回复文本。 |
| `RealtimeVoiceDelta` | `string` | 本次流式回复的 transcript 增量。 |
| `RealtimeVoiceAccumulatedText` | `string` | 当前累计的回复文本。 |
| `RealtimeVoiceIsFinal` | `bool` | 当前回调是否为最终事件。 |
| `RealtimeVoiceError` | `string` | 错误文本。 |
| `RealtimeVoiceCallbackName` | `string` | `Scene.RealtimeVoice.Start*` / `scene.realtime_voice.start_*` 传入的回调名。 |
| `RealtimeVoiceWakeWord` | `string` | 命中的唤醒词，仅 `wake_word_detected` 通常有值。 |
| `RealtimeVoiceRecognizedText` | `string` | 原始识别文本，例如完整唤醒词句子或用户转写文本。 |

跨帧状态建议放在 `static` 类型里：

```csharp
static class State
{
    public static bool SpaceWasDown;
}

if (IsUpdate)
{
    bool down = Input.IsKeyDown("Space");
    if (down && !State.SpaceWasDown)
    {
        Entity.Speak("空格被按下");
    }

    State.SpaceWasDown = down;
}
```

### C# 自定义函数和类型

C# `.csx` 脚本可以像普通 C# 脚本一样，自行声明函数、局部函数、`static class`、`record`、辅助方法，并在脚本内部自己调用。

简单函数示例：

```csharp
float Clamp01(float value)
{
    return Math.Clamp(value, 0.0f, 1.0f);
}

void SpeakIfNeeded(string text)
{
    if (!string.IsNullOrWhiteSpace(text))
    {
        Entity.Speak(text);
    }
}

if (IsStart)
{
    SpeakIfNeeded("你好，我是小雨");
}
```

带辅助类型的示例：

```csharp
static class Helpers
{
    public static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}

static class State
{
    public static float Accumulator;
}

if (IsUpdate)
{
    State.Accumulator += (float)DeltaSeconds;
    float volume = Helpers.Lerp(0.2f, 1.0f, MathF.Abs(MathF.Sin(State.Accumulator)));
    Audio.SetVolume("BGM", volume);
}
```

注意：

- 这些函数和类型只在当前 `.csx` 文件内可见，除非你在脚本里自己 `#load` 其它脚本文件。
- C# 脚本是按事件重新执行同一个 `.csx` 文件，不会自动保留普通局部变量的值；跨帧状态仍然建议放进 `static` 类型或外部存档。
- 如果函数内部直接使用 `Entity`、`Scene`、`Input`、`Audio`、`DeltaSeconds` 等全局对象，它们就是当前这次事件执行时的上下文。

### Python 生命周期

Python 通过函数名派发事件：

```python
def start(entity, scene, input, audio):
    print("start", entity.name)

def update(entity, scene, input, audio, delta_seconds):
    entity.rotate_y(30.0 * delta_seconds)

def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    print(control_name, control_id, event_name)

def sprite_event(entity, scene, input, audio, sprite_id, sprite_name, event_name):
    print(sprite_name, sprite_id, event_name)

def loading_started(entity, scene, input, audio, progress, message):
    print("loading started", scene.name)

def loading_progress(entity, scene, input, audio, progress, message):
    print(progress, message)

def loading_completed(entity, scene, input, audio, progress, message):
    print("loading completed")

def speech_completed(entity, scene, input, audio, callback_name):
    print("speech completed", callback_name)

def llm_event(entity, scene, input, audio, event):
    print(event.get("callbackName"), event.get("accumulatedText"))

def asr_event(entity, scene, input, audio, event):
    print(event.get("callbackName"), event.get("eventName"), event.get("text"))

def realtime_voice_event(entity, scene, input, audio, event):
    print(event.get("callbackName"), event.get("eventName"), event.get("text"))
```

`entity.speak(..., on_completed="after_speak")` 会优先调用同名函数：

```python
def start(entity, scene, input, audio):
    entity.speak("你好", on_completed="after_speak")

def after_speak(entity, scene, input, audio):
    entity.rotate_y(180)
```

`scene.llm.start_chat(...)` 的回调会优先调用 `on_delta`、`on_completed`、`on_error` 指定的同名函数；如果没有同名函数但脚本定义了 `llm_event(entity, scene, input, audio, event)`，则会调用通用 `llm_event`。

`scene.asr.start_streaming_recognition(...)` 的回调会优先调用 `on_partial`、`on_completed`、`on_error` 指定的同名函数；如果没有同名函数但脚本定义了 `asr_event(entity, scene, input, audio, event)`，则会调用通用 `asr_event`。

`scene.realtime_voice.start_*` 的回调同样会优先调用传入的同名函数；如果没有同名函数但脚本定义了 `realtime_voice_event(entity, scene, input, audio, event)`，则会调用通用 `realtime_voice_event`。

桌面精灵系统托盘菜单项可以在 GameEditor 中配置 `Script event`。Python 会优先调用同名函数，例如 `tray_exit(entity, scene, input, audio, item_id, item_text, event_name)`；如果没有同名函数但定义了 `tray_menu_event(entity, scene, input, audio, item_id, item_text, event_name)`，则调用通用入口。旧式五参数写法 `tray_menu_event(entity, scene, input, audio, item_id, event_name)` 仍可工作。

Python 模块级变量会保留，可用于跨帧状态：

```python
space_was_down = False

def update(entity, scene, input, audio, delta_seconds):
    global space_was_down
    down = input.is_key_down("Space")
    if down and not space_was_down:
        entity.speak("空格被按下")
    space_was_down = down
```
