# GameEditor / GamePlayer 脚本 API

本文档说明 `GamePlayer` 暴露给 C# `.csx` 和 Python `.py` 脚本的运行时 API。编辑器中绑定脚本后，保存项目时会把外部脚本复制到游戏工程目录下，并做一次轻量语法检查。

脚本层定位：

- 脚本负责游戏逻辑、输入响应、对象移动、GUI 事件、场景切换、音频播放、TTS 说话、相机控制、存档读写等。
- 脚本不直接管理 OpenGL 资源、PMX 内部骨骼求解、音频设备、窗口消息循环或底层物理引擎。
- 当前碰撞是轻量级运行时 Collider，不是完整刚体物理系统。它适合射线拾取、触发区域和简单碰撞判断。
- 网络通信通过 `Scene.Network` / `scene.network` 提供，支持 HTTP/HTTPS、TCP 和 UDP。跨平台使用 .NET / Python 标准网络库，适用于 Windows、Linux 和 macOS。
- 远端语音服务通过 `Scene.RealtimeVoice` / `scene.realtime_voice` 提供，可调用 `Zhengyan.DigitalWife.Samples.RealtimeVoice` 做转写、唤醒词监听、流式语音回复和文本直出 TTS。
- 路径建议使用工程相对路径，例如 `assets/audio/bgm.ogg`、`assets/motions/idle.vmd`、`scripts/main.csx`。GamePlayer 会从游戏工程目录解析这些路径。
- 贴图路径可以使用普通工程相对路径，也可以使用 `rt:<RenderTextureName>` 引用 Render Texture。

## 路径规则

GameEditor 保存项目时会尽量把外部脚本和资源复制到游戏工程目录下。脚本运行时建议只引用工程内路径。

| 写法 | 说明 |
| --- | --- |
| `assets/audio/bgm.ogg` | 工程相对路径，推荐写法。 |
| `project:assets/audio/bgm.ogg` | 显式从游戏工程目录解析。 |
| `app:Resources/Skybox/xxx.jpg` | 从 GamePlayer 程序目录解析，适合引擎自带基础资源。 |
| `rt:MiniMapRT` | Render Texture 引用，不是磁盘文件。 |

资源复制规则：

- PMX、音频、动作、贴图、脚本、TTS 模型文件和目录在保存项目时会复制到工程目录。
- 同一个源文件或源目录在同一次保存中只会复制一份，多个配置引用同一目录时会复用同一个工程内路径。
- `app:` 和 `rt:` 不会被复制；`app:` 指向程序自带资源，`rt:` 指向运行时纹理。
- Python 存档 API 只能访问 `saves/`，不能读取任意工程文件。

## 脚本类型

C# 脚本：

- 文件扩展名：`.csx`。
- 运行环境：Roslyn C# Script。
- 默认导入：`System`、`System.Collections.Generic`、`System.Globalization`、`System.IO`、`System.Linq`、`System.Net`、`System.Net.Http`、`System.Net.Sockets`、`System.Numerics`、`System.Text`、`System.Text.Json`、`System.Text.RegularExpressions`、`System.Threading`、`System.Threading.Tasks`、`Zhengyan.DigitalWife.Samples.GamePlayer`。
- 默认可访问全局对象：`Entity`、`Scene`、`Input`、`Audio`。

Python 脚本：

- 文件扩展名：`.py`。
- 运行环境：系统 `python` 或 `python3` 进程。
- 预置标准库模块：`math`、`random`、`re`、`json`、`datetime`、`time`、`statistics`。
- 仍然可以在脚本中正常 `import` 标准库或当前 Python 环境已安装的第三方包。
- Python 脚本通过桥接命令修改 GamePlayer 状态。
- Python 脚本中的对象属性大多是事件开始时的快照；例如调用 `entity.set_position(...)` 后，当前函数内的 `entity.position` 不会立刻更新，要到下一次事件快照才会反映。

## 基础系统 API

脚本层已经支持基础语言和系统 API。字符串、数值、集合、日期时间、正则、JSON、数学函数等不需要额外的引擎封装，可以直接使用 C# / Python 自身能力。

边界说明：

- C# `.csx` 是受信任本地脚本，运行在 GamePlayer 进程内，不是安全沙箱。
- Python `.py` 是受信任本地脚本，运行在独立 Python 进程内，不是安全沙箱。
- 游戏存档建议优先使用 `Scene.Save` / `scene.save`，这样路径会被限制在工程 `saves/` 目录内，更适合跨平台发布。
- 如果直接使用 C# `System.IO` 或 Python `open()` 访问文件，需要自己处理 Windows / Linux / MacOS 的路径差异和权限问题。

C# 常用能力：

| 能力 | 可用 API |
| --- | --- |
| 字符串 | `string`、`StringBuilder`、`Trim`、`Split`、`Replace`、`Contains`、`StartsWith`、`EndsWith` |
| 数值 | `int`、`float`、`double`、`decimal`、`Math`、`MathF`、`Random` |
| 集合 | `List<T>`、`Dictionary<TKey,TValue>`、数组、LINQ |
| 日期时间 | `DateTime`、`DateTimeOffset`、`TimeSpan` |
| 正则 | `Regex` |
| JSON | `JsonSerializer` |
| 向量 | `Vector2`、`Vector3`、`Vector4`、`Quaternion` |
| 异步 | `Task`、`CancellationToken` |

C# 示例：

```csharp
if (IsStart)
{
    string raw = "  小雨@#$ 123 ABC  ";
    string clean = Regex.Replace(raw.Trim(), @"[^\u4e00-\u9fa5a-zA-Z0-9\s,.!?]", "");

    List<int> values = [1, 2, 3, 4, 5];
    int total = values.Where(v => v % 2 == 1).Sum();

    float wave = MathF.Sin((float)DateTime.UtcNow.TimeOfDay.TotalSeconds);
    Vector3 next = Entity.Position + new Vector3(wave, 0.0f, 0.0f);
    Entity.SetPosition(next.X, next.Y, next.Z);

    string json = JsonSerializer.Serialize(new { clean, total });
    Scene.Save.WriteText("system_api_demo.json", json);
}
```

Python 常用能力：

| 能力 | 可用 API |
| --- | --- |
| 字符串 | `str`、`strip`、`split`、`replace`、`in`、`startswith`、`endswith` |
| 数值 | `int`、`float`、`round`、`abs`、`min`、`max`、`sum` |
| 集合 | `list`、`dict`、`set`、`tuple`、列表推导式 |
| 日期时间 | `datetime`、`time` |
| 数学 | `math`、`random`、`statistics` |
| 正则 | `re` |
| JSON | `json` |

Python 示例：

```python
def start(entity, scene, input, audio):
    raw = "  小雨@#$ 123 ABC  "
    clean = re.sub(r"[^\u4e00-\u9fa5a-zA-Z0-9\s,.!?]", "", raw.strip())

    values = [1, 2, 3, 4, 5]
    total = sum(v for v in values if v % 2 == 1)

    wave = math.sin(time.time())
    entity.set_position(entity.position[0] + wave, entity.position[1], entity.position[2])

    scene.save.write_json("system_api_demo.json", {
        "clean": clean,
        "total": total,
        "created_at": datetime.datetime.now().isoformat()
    })
```

## 生命周期

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

## Entity API

`RuntimeEntity` 代表场景中的对象。对象类型包括：

| 类型 | 说明 |
| --- | --- |
| `pmx_model` | PMX 模型，支持动作、材质贴图覆盖、TTS 口型、PMX 绑定关系。 |
| `empty_object` | 空对象，不渲染；支持 Transform、脚本、碰撞体，适合触发器、挂点、相机目标。 |
| `textured_plane` | 3D 矩形面，可设置图片纹理和 Billboard。 |
| `particle_system` | 粒子系统。 |
| `water_surface` | 水面对象，可开启水体交互波纹。 |

常用属性：

| C# 属性 | Python 快照 | 说明 |
| --- | --- | --- |
| `Id` | `id` | 实体 Id。 |
| `Name` | `name` | 实体名称。 |
| `Type` | `type` | 实体类型。 |
| `Position` | `position` | 世界坐标。C# 为 `Vector3`，Python 为 `[x,y,z]`。 |
| `Scale` | `scale` | 缩放。 |
| `Rotation` | `rotation` | 四元数旋转。 |
| `Visible` | 无直接快照字段 | 是否可见。Python 用 `set_visible` 修改。 |
| `IsPlaying` | 无直接快照字段 | PMX 动作/粒子/水面是否播放。Python 用 `set_playing` 修改。 |
| `PlaybackSpeed` | 无直接快照字段 | PMX 动作或粒子模拟速度。 |
| `LoopMotion` | 无直接快照字段 | PMX 动作是否循环。Python 用 `set_loop_motion` 修改。 |
| `ResetPhysicsOnMotionLoop` | 无直接快照字段 | PMX 动作循环时是否重置物理。Python 用 `set_reset_physics_on_motion_loop` 修改。 |
| `EnableEdge` | 无直接快照字段 | PMX 是否绘制描边。Python 用 `set_edge_enabled` 修改。 |
| `EnableShadow` | 无直接快照字段 | PMX 是否参与阴影绘制。Python 用 `set_shadow_enabled` 修改。 |
| `EnableWaterInteraction` | `enable_water_interaction` | 粒子系统是否参与水体交互。Python 用 `set_enable_water_interaction` 修改。 |
| `KillOnWaterContact` | `kill_on_water_contact` | 粒子系统粒子接触水面后是否立即消失。Python 用 `set_kill_on_water_contact` 修改。 |
| `WaterInteractionEnabled` | `water_interaction_enabled` | 水面对象是否启用水体交互检测。Python 用 `set_water_interaction_enabled` 修改。 |
| `WaterInteractionRadius` | `water_interaction_radius` | 水面波纹半径。Python 用 `set_water_interaction_radius` 修改。 |
| `WaterInteractionStrength` | `water_interaction_strength` | 水面波纹强度。Python 用 `set_water_interaction_strength` 修改。 |
| `ParticleRippleMinIntervalSeconds` | `particle_ripple_min_interval_seconds` | 同一区域粒子触水的最小波纹间隔。Python 用 `set_particle_ripple_min_interval_seconds` 修改。 |
| `ParticleRippleMergeDistance` | `particle_ripple_merge_distance` | 粒子触水波纹的空间合并距离。Python 用 `set_particle_ripple_merge_distance` 修改。 |
| `RippleLifetimeSeconds` | 无 | 水面单个波纹的持续时间。 |
| `RippleWaveSpeed` | 无 | 水面波纹传播速度。 |
| `RippleFrequency` | 无 | 水面波纹频率。 |
| `RippleNormalStrength` | 无 | 水面波纹法线扰动强度。 |
| `DrawShadowInMainPass` | 无直接快照字段 | PMX 是否在主渲染通道直接绘制地面影子。Python 用 `set_draw_shadow_in_main_pass` 修改。 |
| `MaterialNames` | `material_names` | PMX 材质名称列表。 |
| `Colliders` | `colliders` | 碰撞体快照。 |

Transform：

```csharp
Entity.SetPosition(0, 1, 0);
Entity.Translate(0, 0, -1);
Entity.SetScale(0.2f, 0.2f, 0.2f);
Entity.RotateX(10);
Entity.RotateY(90);
Entity.RotateZ(5);
Entity.Visible = true;
Entity.IsPlaying = true;
Entity.PlaybackSpeed = 1.2f;
Entity.LoopMotion = true;
Entity.ResetPhysicsOnMotionLoop = true;
Entity.EnableEdge = true;
Entity.EnableShadow = true;
Entity.EnableWaterInteraction = true;
Entity.KillOnWaterContact = true;
Entity.WaterInteractionEnabled = true;
Entity.WaterInteractionRadius = 1.0f;
Entity.WaterInteractionStrength = 0.9f;
Entity.ParticleRippleMinIntervalSeconds = 0.08f;
Entity.ParticleRippleMergeDistance = 0.5f;
Entity.RippleLifetimeSeconds = 2.8f;
Entity.RippleWaveSpeed = 12.0f;
Entity.RippleFrequency = 16.0f;
Entity.RippleNormalStrength = 0.65f;
Entity.DrawShadowInMainPass = false;
```

```python
entity.set_position(0, 1, 0)
entity.translate(0, 0, -1)
entity.set_scale(0.2, 0.2, 0.2)
entity.rotate_x(10)
entity.rotate_y(90)
entity.rotate_z(5)
entity.set_visible(True)
entity.set_playing(True)
entity.set_playback_speed(1.2)
entity.set_loop_motion(True)
entity.set_reset_physics_on_motion_loop(True)
entity.set_edge_enabled(True)
entity.set_shadow_enabled(True)
entity.set_enable_water_interaction(True)
entity.set_kill_on_water_contact(True)
entity.set_water_interaction_enabled(True)
entity.set_water_interaction_radius(1.0)
entity.set_water_interaction_strength(0.9)
entity.set_particle_ripple_min_interval_seconds(0.08)
entity.set_particle_ripple_merge_distance(0.5)
entity.set_draw_shadow_in_main_pass(False)
```

C# 额外可读/可写属性：

| 属性 | 说明 |
| --- | --- |
| `IsPmxModel` | 当前实体是否有 PMX 运行时对象。 |
| `LoopMotion` | PMX 动作是否循环。 |
| `ResetPhysicsOnMotionLoop` | PMX 动作循环时是否重置物理。 |
| `EnableEdge` | PMX 是否绘制描边。 |
| `EnableShadow` | PMX 是否参与阴影绘制。 |
| `EnableWaterInteraction` | 粒子系统是否参与水体交互。 |
| `KillOnWaterContact` | 粒子系统粒子接触水面后是否立即消失。 |
| `WaterInteractionEnabled` | 水面对象是否启用水体交互检测。 |
| `WaterInteractionRadius` | 水面波纹半径。 |
| `WaterInteractionStrength` | 水面波纹强度。 |
| `ParticleRippleMinIntervalSeconds` | 同一区域粒子触水的最小波纹间隔。 |
| `ParticleRippleMergeDistance` | 粒子触水波纹的空间合并距离。 |
| `RippleLifetimeSeconds` | 水面单个波纹的持续时间。 |
| `RippleWaveSpeed` | 水面波纹传播速度。 |
| `RippleFrequency` | 水面波纹频率。 |
| `RippleNormalStrength` | 水面波纹法线扰动强度。 |
| `DrawShadowInMainPass` | PMX 是否在主渲染通道直接绘制地面影子。 |
| `RelationEnabled` | 是否启用 PMX 绑定关系。 |
| `RelationEntity` | 绑定目标实体名称或 Id。 |
| `RelationBindComponentTransform` | 是否绑定组件 Transform。 |
| `RelationBindLighting` | 是否绑定光照。 |
| `Collision` | 旧版单 Collider 配置对象。 |
| `Colliders` | 多 Collider 配置列表。 |
| `CollisionEnabled` | 所有有效 Collider 是否启用。 |
| `CollisionShape` | 主 Collider 形状。 |
| `CollisionCenter` | 主 Collider 本地中心。 |
| `CollisionRadius` | 主胶囊 Collider 半径。 |
| `CollisionHeight` | 主胶囊 Collider 高度。 |
| `CollisionAxis` | 主胶囊 Collider 轴向，`x`、`y`、`z`。 |

简单 WASD 移动：

```csharp
if (IsUpdate)
{
    float speed = Input.IsShiftDown ? 4.0f : 1.5f;
    float step = speed * (float)DeltaSeconds;

    if (Input.IsKeyDown("W")) Entity.Translate(0, 0, -step);
    if (Input.IsKeyDown("S")) Entity.Translate(0, 0, step);
    if (Input.IsKeyDown("A")) Entity.Translate(-step, 0, 0);
    if (Input.IsKeyDown("D")) Entity.Translate(step, 0, 0);
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    speed = 4.0 if input.shift_down else 1.5
    step = speed * delta_seconds

    if input.is_key_down("w"):
        entity.translate(0, 0, -step)
    if input.is_key_down("s"):
        entity.translate(0, 0, step)
    if input.is_key_down("a"):
        entity.translate(-step, 0, 0)
    if input.is_key_down("d"):
        entity.translate(step, 0, 0)
```

## PMX 动作与材质

动作路径一般放在 `assets/motions/*.vmd`。GameEditor 添加动作资源后保存，会把资源复制到工程目录。

```csharp
Entity.ApplyMotion("assets/motions/idle.vmd");
Entity.SetMotionLayers(new[]
{
    new MotionLayerDefinition("assets/motions/idle.vmd", 1.0f),
    new MotionLayerDefinition("assets/motions/wave.vmd", 0.0f)
});
Entity.AddMotionLayer("assets/motions/wave.vmd", weight: 0.5f);
Entity.SetMotionLayerWeight("assets/motions/wave.vmd", 1.0f);
Entity.SetMotionLayerResetPhysicsOnLoop("assets/motions/wave.vmd", true);
Entity.PlayMotion();
Entity.PauseMotion();
Entity.SeekMotionFrame(45);
Entity.SeekMotionTime(1.5f);
Entity.ResetMotionPhysics();
Entity.ResetMotion();
Entity.StopMotion();
Entity.PlayMotionLayer("assets/motions/wave.vmd");
Entity.PauseMotionLayer("assets/motions/wave.vmd");
Entity.SetMotionLayerFrame("assets/motions/wave.vmd", 30);
Entity.SetMotionLayerTime("assets/motions/wave.vmd", 1.0f);
Entity.RemoveMotionLayer("assets/motions/wave.vmd");
Entity.ClearMotion();
```

```python
entity.apply_motion("assets/motions/idle.vmd")
entity.set_motion_layers([
    {"path": "assets/motions/idle.vmd", "weight": 1.0},
    {"path": "assets/motions/wave.vmd", "weight": 0.0},
])
entity.add_motion_layer("assets/motions/wave.vmd", weight=0.5)
entity.set_motion_layer_weight("assets/motions/wave.vmd", 1.0)
entity.set_motion_layer_reset_physics_on_loop("assets/motions/wave.vmd", True)
entity.play_motion()
entity.pause_motion()
entity.seek_motion_frame(45)
entity.seek_motion_time(1.5)
entity.reset_motion_physics()
entity.reset_motion()
entity.stop_motion()
entity.play_motion_layer("assets/motions/wave.vmd")
entity.pause_motion_layer("assets/motions/wave.vmd")
entity.set_motion_layer_frame("assets/motions/wave.vmd", 30)
entity.set_motion_layer_time("assets/motions/wave.vmd", 1.0)
entity.remove_motion_layer("assets/motions/wave.vmd")
entity.clear_motion()
```

PMX 运行时渲染与播放控制：

```csharp
Entity.IsPlaying = true;
Entity.PlaybackSpeed = 1.0f;
Entity.LoopMotion = true;
Entity.ResetPhysicsOnMotionLoop = true;

Entity.EnableEdge = true;
Entity.EnableShadow = true;
Entity.DrawShadowInMainPass = false;
```

```python
entity.set_playing(True)
entity.set_playback_speed(1.0)
entity.set_loop_motion(True)
entity.set_reset_physics_on_motion_loop(True)

entity.set_edge_enabled(True)
entity.set_shadow_enabled(True)
entity.set_draw_shadow_in_main_pass(False)
```

说明：

- `IsPlaying` 只对当前实体的可播放能力生效。对 PMX 表示动作播放，对粒子/水面表示系统启停。
- `PlaybackSpeed` 对 PMX 表示动作倍速，对粒子系统表示模拟速度。
- `LoopMotion` 和 `ResetPhysicsOnMotionLoop` 仅对 PMX 有意义。
- `EnableWaterInteraction` 仅对粒子系统有意义。只有粒子实体和水面实体都开启水体交互时，系统才会按每个活跃粒子的位置和当前尺寸做近似球检测，并在接触水面时触发波纹。
- `KillOnWaterContact` 仅对粒子系统有意义。开启后，接触水面的粒子会在当前帧结束；关闭时粒子会穿过水面继续运动。
- `WaterInteractionEnabled`、`WaterInteractionRadius`、`WaterInteractionStrength`、`ParticleRippleMinIntervalSeconds`、`ParticleRippleMergeDistance`、`RippleLifetimeSeconds`、`RippleWaveSpeed`、`RippleFrequency`、`RippleNormalStrength` 仅对 `water_surface` 有意义。
- `EnableEdge`、`EnableShadow`、`DrawShadowInMainPass` 仅对 PMX 有意义。
- `DrawShadowInMainPass = false` 时，地面影子会交给独立的地面阴影 pass 处理；`true` 时在 PMX 主绘制阶段直接绘制。
- `PlayMotion` / `PauseMotion` 只改变播放状态，不会清掉已加载动作层。
- `StopMotion` 会把动作停下并重置到起始状态，效果接近“停止并回到 0 帧”。
- `ResetMotion` 会重置动作与姿态；`ResetMotionPhysics` 只重置物理。
- `SeekMotionFrame` / `SeekMotionTime` 会把当前 PMX 动作层一起跳到指定帧或秒。
- `PlayMotionLayer` / `PauseMotionLayer` / `SetMotionLayerFrame` / `SetMotionLayerTime` 只作用于指定动作层。

## 水面与粒子触水

水体交互涉及两类实体：

- `particle_system`
  - `EnableWaterInteraction`
  - `KillOnWaterContact`
- `water_surface`
  - `WaterInteractionEnabled`
  - `WaterInteractionRadius`
  - `WaterInteractionStrength`
  - `ParticleRippleMinIntervalSeconds`
  - `ParticleRippleMergeDistance`
  - `RippleLifetimeSeconds`
  - `RippleWaveSpeed`
  - `RippleFrequency`
  - `RippleNormalStrength`

只有粒子系统和水面双方都开启交互时，粒子触水才会出波纹。

C#：

```csharp
RuntimeEntity? rain = Scene.GetEntity("Rain FX");
RuntimeEntity? pond = Scene.GetEntity("Pond");

if (rain is not null && pond is not null)
{
    rain.EnableWaterInteraction = true;
    rain.KillOnWaterContact = true;

    pond.WaterInteractionEnabled = true;
    pond.WaterInteractionRadius = 0.9f;
    pond.WaterInteractionStrength = 0.75f;
    pond.ParticleRippleMinIntervalSeconds = 0.08f;
    pond.ParticleRippleMergeDistance = 0.45f;
    pond.RippleLifetimeSeconds = 2.8f;
    pond.RippleWaveSpeed = 12.0f;
    pond.RippleFrequency = 16.0f;
    pond.RippleNormalStrength = 0.65f;
}
```

Python：

```python
rain = scene.get_entity("Rain FX")
pond = scene.get_entity("Pond")

if rain is not None and pond is not None:
    rain.set_enable_water_interaction(True)
    rain.set_kill_on_water_contact(True)

    pond.set_water_interaction_enabled(True)
    pond.set_water_interaction_radius(0.9)
    pond.set_water_interaction_strength(0.75)
    pond.set_particle_ripple_min_interval_seconds(0.08)
    pond.set_particle_ripple_merge_distance(0.45)
    pond.set_ripple_lifetime_seconds(2.8)
    pond.set_ripple_wave_speed(12.0)
    pond.set_ripple_frequency(16.0)
    pond.set_ripple_normal_strength(0.65)
```

调参建议：

- 雨：`ParticleRippleMinIntervalSeconds` 取 `0.05 - 0.12`，`ParticleRippleMergeDistance` 取 `0.3 - 0.6`。
- 瀑布：`ParticleRippleMinIntervalSeconds` 取 `0.02 - 0.08`，`ParticleRippleMergeDistance` 取 `0.6 - 1.2`。
- 平静水面的小粒子点缀：`KillOnWaterContact = false`，让粒子穿过水面但仍留下波纹。

C# 额外动作查询：

```csharp
float currentTime = Entity.AnimationTimeSeconds;
int layerCount = Entity.MotionLayerCount;
IReadOnlyList<MotionLayerInfo> layers = Entity.GetMotionLayers();
MotionLayerInfo? wave = Entity.GetMotionLayer("assets/motions/wave.vmd");
```

`MotionLayerInfo` 包含：

- `MotionPath`
- `Weight`
- `TimeSeconds`
- `DurationFrames`
- `ResetPhysicsOnLoop`
- `IsPlaying`

PMX Morph 控制：

```csharp
// 列出模型中的 Morph 名称，名称必须和 PMX 文件里的 MMDMorph.Name 一致。
foreach (string morphName in Entity.MorphNames)
{
    Console.WriteLine(morphName);
}

float smile = Entity.GetMorphWeight("笑い");
Entity.SetMorphWeight("笑い", 1.0f);

// 如果希望当前 Morph 权重作为动作混合基准保存下来。
Entity.SaveMorphAnimWeight("笑い");
Entity.SaveAnimWeight("笑い"); // SaveMorphAnimWeight 的别名，更贴近底层 MMDMorph.SaveAnimWeight 命名。
float savedSmile = Entity.GetMorphSaveAnimWeight("笑い");
Entity.SetMorphSaveAnimWeight("笑い", 0.5f);
Entity.LoadMorphAnimWeight("笑い");
Entity.ClearMorphAnimWeight("笑い");

// 对整个 PMX 的骨骼、Morph、IK 保存/恢复/清空基准动画。
Entity.SaveBaseAnimation();
Entity.LoadBaseAnimation();
Entity.ClearBaseAnimation();

// 清除脚本对 Morph 的持续覆盖，让动作层重新完全接管这个 Morph。
Entity.ClearMorphWeightOverride("笑い");
Entity.ClearMorphWeightOverrides();
```

```python
for morph_name in entity.morph_names:
    print(morph_name)

smile = entity.get_morph_weight("笑い")
entity.set_morph_weight("笑い", 1.0)

# 如果希望当前 Morph 权重作为动作混合基准保存下来。
entity.save_morph_anim_weight("笑い")
entity.save_anim_weight("笑い")  # save_morph_anim_weight 的别名。
saved_smile = entity.get_morph_save_anim_weight("笑い")
entity.set_morph_save_anim_weight("笑い", 0.5)
entity.load_morph_anim_weight("笑い")
entity.clear_morph_anim_weight("笑い")

# 对整个 PMX 的骨骼、Morph、IK 保存/恢复/清空基准动画。
entity.save_base_animation()
entity.load_base_animation()
entity.clear_base_animation()

# 清除脚本对 Morph 的持续覆盖，让动作层重新完全接管这个 Morph。
entity.clear_morph_weight_override("笑い")
entity.clear_morph_weight_overrides()
```

Morph 说明：

- `SetMorphWeight(name, weight)` / `entity.set_morph_weight(name, weight)` 默认会持续覆盖同名 Morph，即使当前正在播放 VMD 动作层，也会在动作采样后重新写入该权重。
- 如果只想改一次当前帧权重，不想持续覆盖动作层，C# 可调用 `SetMorphWeight(name, weight, overrideAnimation: false)`，Python 可调用 `set_morph_weight(name, weight, override_animation=False)`。
- `SaveMorphAnimWeight(name)` / `SaveAnimWeight(name)` 对应底层 `MMDMorph.SaveBaseAnimation()`，会把当前 `Weight` 保存到该 Morph 的 `SaveAnimWeight`。
- `SaveBaseAnimation()`、`LoadBaseAnimation()`、`ClearBaseAnimation()` 作用于整个 PMX 模型的骨骼、Morph 和 IK 基准动画。
- Morph 名称区分 PMX 文件内容，常见日文模型可能是 `笑い`、`まばたき`、`あ`、`い` 等；如果名称不存在，C# 抛出异常或 `Try*` 返回 `false`，Python 命令会由运行时忽略或在控制台输出脚本错误。

PMX 骨骼 / MMDNode 控制：

```csharp
// 列出 PMX 骨骼名称，名称必须和 PMX 文件里的 MMDNode.Name 一致。
foreach (string nodeName in Entity.NodeNames)
{
    Console.WriteLine(nodeName);
}

PmxNodeState head = Entity.GetNodeState("頭");
Vector3 headTranslate = head.Translate;
Quaternion headRotate = head.Rotate;
Vector3 headScale = head.Scale;
Vector3 headAnimTranslate = head.AnimTranslate;
Quaternion headAnimRotate = head.AnimRotate;

// 基础骨骼 TRS：影响 MMDNode.Translate / Rotate / Scale。
Entity.SetNodeTranslate("頭", 0.0f, 0.05f, 0.0f);
Entity.SetNodeRotateEuler("頭", 0.0f, 25.0f, 0.0f);
Entity.SetNodeScale("頭", 1.05f, 1.05f, 1.05f);

// 动画偏移：影响 MMDNode.AnimTranslate / AnimRotate，适合在动作层基础上叠加姿态。
Entity.SetNodeAnimTranslate("右腕", 0.0f, 0.0f, 0.02f);
Entity.SetNodeAnimRotateEuler("右腕", 0.0f, 0.0f, -20.0f);

// 单个骨骼基准动画：保存/恢复/清空 AnimTranslate 与 AnimRotate。
Entity.SaveNodeBaseAnimation("右腕");
Entity.LoadNodeBaseAnimation("右腕");
Entity.ClearNodeBaseAnimation("右腕");

// 清除脚本对骨骼的持续覆盖，让 PMX 初始姿态和 VMD 动作重新接管。
Entity.ClearNodeOverrides("右腕");
Entity.ClearAllNodeOverrides();
```

```python
for node_name in entity.node_names:
    print(node_name)

head = entity.get_node_state("頭")
if head is not None:
    print(head["translate"], head["rotate"], head["scale"])

# 基础骨骼 TRS：影响 MMDNode.Translate / Rotate / Scale。
entity.set_node_translate("頭", 0.0, 0.05, 0.0)
entity.set_node_rotate_euler("頭", 0.0, 25.0, 0.0)
entity.set_node_scale("頭", 1.05, 1.05, 1.05)

# 动画偏移：影响 MMDNode.AnimTranslate / AnimRotate，适合在动作层基础上叠加姿态。
entity.set_node_anim_translate("右腕", 0.0, 0.0, 0.02)
entity.set_node_anim_rotate_euler("右腕", 0.0, 0.0, -20.0)

# 单个骨骼基准动画：保存/恢复/清空 AnimTranslate 与 AnimRotate。
entity.save_node_base_animation("右腕")
entity.load_node_base_animation("右腕")
entity.clear_node_base_animation("右腕")

# 清除脚本对骨骼的持续覆盖，让 PMX 初始姿态和 VMD 动作重新接管。
entity.clear_node_overrides("右腕")
entity.clear_all_node_overrides()
```

骨骼控制说明：

- `SetNodeTranslate/Rotate/Scale` 控制的是骨骼基础 TRS，即底层 `MMDNode.Translate`、`MMDNode.Rotate`、`MMDNode.Scale`。它会改变骨骼的本地基础姿态，适合做模型校正、挂点调整或非动作层姿态修改。
- `SetNodeAnimTranslate/AnimRotate` 控制的是动作偏移，即底层 `MMDNode.AnimTranslate`、`MMDNode.AnimRotate`。它会在 VMD 动作层采样后覆盖同名骨骼，适合做运行时看向、手臂微调、程序化姿态叠加。
- 上面这些设置默认会持续覆盖后续帧。C# 可传 `overrideAnimation: false`，Python 可传 `override_animation=False`，表示只写入当前值，不登记持续覆盖。
- 旋转同时支持四元数和欧拉角。C# 可用 `SetNodeRotate(name, Quaternion)` / `SetNodeRotateEuler(name, xDeg, yDeg, zDeg)`；Python 可用 `set_node_rotate(name, x, y, z, w)` / `set_node_rotate_euler(name, x_deg, y_deg, z_deg)`。
- `SaveNodeBaseAnimation(name)` 对应底层 `MMDNode.SaveBaseAnimation()`，保存当前 `AnimTranslate` 与 `AnimRotate` 到 `BaseAnimTranslate` / `BaseAnimRotate`。
- 骨骼名称来自 PMX 文件，常见日文名如 `頭`、`首`、`上半身`、`右腕`、`左腕`。名称不存在时 C# 抛出异常或 `Try*` 返回 `false`，Python 命令会由运行时忽略或在控制台输出脚本错误。

材质贴图覆盖：

```csharp
// 按材质下标。
Entity.SetMaterialTexture(0, "assets/textures/body_alt.png");
Entity.SetMaterialRenderTexture(0, "MiniMapRT");
Entity.ClearMaterialTextureOverride(0);

// 按材质名称。
Entity.SetMaterialTexture("Body", "project:assets/textures/body_alt.png");
Entity.SetMaterialRenderTexture("Screen", "CameraRT");
Entity.ClearMaterialTextureOverrides();
```

```python
entity.set_material_texture(0, "assets/textures/body_alt.png")
entity.set_material_render_texture(0, "MiniMapRT")
entity.clear_material_texture_override(0)

entity.set_material_texture("Body", "project:assets/textures/body_alt.png")
entity.set_material_render_texture("Screen", "CameraRT")
entity.clear_material_texture_overrides()
```

PMX 绑定关系：

```csharp
Entity.BindRelation("TargetPmx", bindComponentTransform: true, bindLighting: false);
Entity.ClearRelationBinding();
```

```python
entity.bind_relation("TargetPmx", bind_component_transform=True, bind_lighting=False)
entity.clear_relation()
```

`BindRelation` 只对 PMX 模型有效；目标也必须是 PMX 模型。

## TTS / 人物说话

`Entity.Speak` 是非阻塞的：调用后会立即返回，音频和口型在运行时异步播放。需要播放结束后继续逻辑时，使用回调。

前提条件：

- 项目 `Voice / TTS` 中启用运行时 TTS。
- TTS 模型路径配置正确。
- 说话实体必须是 PMX 模型，且启用了口型字典时才会驱动口型。
- `PreloadOnSceneLoad` 开启时，GamePlayer 会在场景加载时预热 TTS，避免首次说话明显卡顿。

C#：

```csharp
Entity.Speak("你好");
Entity.Speak("你好", speakerId: 0);
Entity.Speak("语速稍快一点", speakerId: 0, speed: 1.15f);
Entity.Speak("音量小一点", speakerId: 0, speed: 1.0f, volume: 0.7f);

Entity.Speak("播放完后转身", 0, 1.0f, 1.0f, () =>
{
    Entity.RotateY(180);
});

Entity.SpeakWithCallback("播放完触发脚本事件", "after_intro");

if (IsSpeechEvent && SpeechCallbackName == "after_intro")
{
    Entity.SetPosition(0, 0, 0);
}

Entity.StopSpeaking();
```

Python：

```python
def start(entity, scene, input, audio):
    entity.speak("你好", speaker_id=0, speed=1.0, volume=1.0)
    entity.speak("播放完触发回调", speaker_id=0, speed=1.0, volume=1.0, on_completed="after_intro")

def after_intro(entity, scene, input, audio):
    entity.rotate_y(180)

def update(entity, scene, input, audio, delta_seconds):
    if input.is_key_down("Escape"):
        entity.stop_speaking()
```

## Input 输入

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
input.is_key_down("w")
input.is_key_down("Space")
input.is_mouse_button_down("left")
```

常用键名：

- 字母：`A` 到 `Z`，例如 `W`、`A`、`S`、`D`。
- 数字：`Number0` 到 `Number9`，也支持 `D0` 到 `D9`。
- 方向键：`Up`、`Down`、`Left`、`Right`。
- 功能键：`F1` 到 `F12`。
- 控制键：`Space`、`Enter`、`Escape`、`Tab`、`Backspace`、`Delete`。
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

## Audio 音频 / 背景音乐

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

## Scene API

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

## Performance / FPS API

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

## 场景加载入口脚本

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

## Dialogue Bubble API

`Scene.Bubble` / `scene.bubble` 用于在运行时创建、显示、隐藏和删除对话气泡。它和 GUI 控件不同，不需要预先在 GameEditor 里摆一个控件；脚本里按名字创建即可。

适合的场景：

- 给 NPC 头顶挂台词气泡
- 做屏幕角落的系统提示、任务提示
- 复刻 `DigitalHuman` 那种 `header + text + footer` 的三段式气泡

管理器 API：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Bubble.Count` | `scene.bubble.count` | 当前运行时气泡数量。 |
| `Scene.Bubble.Names` | `scene.bubble.names` | 当前气泡名称列表。 |
| `Scene.Bubble.VisibleNames` | `scene.bubble.visible_names` | 当前可见气泡名称列表。 |
| `Scene.Bubble.Contains(name)` | `scene.bubble.contains(name)` | 是否已存在指定名字的气泡。 |
| `Scene.Bubble.GetOrCreate(name)` | `scene.bubble.get_or_create(name)` | 获取或创建一个命名气泡。 |
| `Scene.Bubble.Create(name)` | `scene.bubble.create(name)` | `GetOrCreate` 的别名。 |
| `Scene.Bubble.ShowText(name, text, headerText, footerText)` | `scene.bubble.show(name, text="", header_text="", footer_text="")` | 快速显示一条气泡。 |
| `Scene.Bubble.HideAll()` | `scene.bubble.hide_all()` | 隐藏全部气泡，但保留对象。 |
| `Scene.Bubble.Remove(name)` | `scene.bubble.remove(name)` | 删除指定气泡。 |
| `Scene.Bubble.Clear()` | `scene.bubble.clear()` | 删除全部气泡。 |

单个气泡常用属性 / 方法：

| C# | Python | 说明 |
| --- | --- | --- |
| `Visible` / `Show()` / `Hide()` | `set_visible(value)` / `show(...)` / `hide()` | 显示或隐藏气泡。 |
| `HeaderText` / `Text` / `FooterText` | `set_header_text(...)` / `set_text(...)` / `set_footer_text(...)` | 三段文本内容。都为空时不会绘制。 |
| `LayoutMode` | `set_layout_mode("absolute" / "relative")` | `relative` 会按项目窗口基准分辨率缩放位置、宽度、字号和边距。 |
| `AnchorMode` | `set_anchor_mode("screen" / "world" / "entity")` | 屏幕坐标、世界坐标或绑定实体。 |
| `AttachToEntity(name, useModelTopAnchor)` | `attach_to_entity(name, use_model_top_anchor=True)` | 把气泡挂到实体上；PMX 默认取模型包围盒顶部中心点，更接近数字人气泡。 |
| `UseScreenSpace(x, y, layoutMode)` / `SetScreenPosition(x, y)` | `set_screen_position(x, y, layout_mode=None)` | 直接设置屏幕锚点位置。 |
| `SetScreenOffset(x, y)` | `set_screen_offset(x, y)` | 在最终投影位置上再叠加一个 2D 偏移。 |
| `UseWorldSpace(x, y, z)` / `SetWorldPosition(x, y, z)` | `set_world_position(x, y, z)` | 把气泡锚到一个世界坐标点。 |
| `SetWorldOffset(x, y, z)` | `set_world_offset(x, y, z)` | 世界坐标偏移，常用于把气泡抬高一点。 |
| `Width` | `set_width(width)` | 文本换行宽度。 |
| `SetPadding(x, y)` | `set_padding(x, y)` | 气泡内边距。 |
| `SetPivot(x, y)` | `set_pivot(x, y)` | 锚点枢轴，范围 `0.0 ~ 1.0`。例如 `(0.5, 1.0)` 表示底边中心对齐锚点。 |
| `TextAlignment` | `set_text_alignment("left" / "center" / "right")` | 三段文本统一的水平对齐方式。 |
| `FontSize` / `HeaderFontSize` / `FooterFontSize` | `set_font_size(...)` / `set_header_font_size(...)` / `set_footer_font_size(...)` | 三段字号。 |
| `BackgroundColor` / `BorderColor` | `set_background_color(r, g, b, a)` / `set_border_color(...)` | 气泡背景和边框颜色。 |
| `TextColor` / `HeaderTextColor` / `FooterTextColor` | `set_text_color(...)` / `set_header_text_color(...)` / `set_footer_text_color(...)` | 三段文字颜色。 |
| `Rounding` | `set_rounding(value)` | 圆角半径。 |
| `BorderThickness` | `set_border_thickness(value)` | 边框粗细。 |
| `DrawOrder` | `set_draw_order(value)` | 绘制顺序；值越大越靠后绘制。 |

### C#：挂在 NPC 头顶的对话气泡

```csharp
RuntimeDialogueBubble bubble = Scene.Bubble.GetOrCreate("shopkeeper");
bubble.AttachToEntity("Shopkeeper", useModelTopAnchor: true);
bubble.SetWorldOffset(0.0f, 0.18f, 0.0f);
bubble.SetScreenOffset(0.0f, -18.0f);
bubble.Width = 420.0f;
bubble.TextAlignment = "left";
bubble.FontSize = 22.0f;
bubble.HeaderFontSize = 18.0f;
bubble.FooterFontSize = 16.0f;
bubble.BackgroundColor = new Vector4(0.08f, 0.10f, 0.16f, 0.92f);
bubble.BorderColor = new Vector4(0.62f, 0.84f, 1.0f, 0.95f);
bubble.HeaderTextColor = new Vector4(0.78f, 0.88f, 1.0f, 1.0f);
bubble.TextColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
bubble.FooterTextColor = new Vector4(0.76f, 0.80f, 0.88f, 1.0f);
bubble.SetContent(
    "欢迎来到海边集市，今天的新货刚到。",
    headerText: "商店老板",
    footerText: "按 E 继续");
bubble.Show();
```

### Python：屏幕右上角任务提示

```python
tip = scene.bubble.get_or_create("quest-tip")
tip.set_layout_mode("relative")
tip.set_screen_position(1180, 48, layout_mode="relative")
tip.set_pivot(1.0, 0.0)
tip.set_width(360)
tip.set_padding(16, 12)
tip.set_text_alignment("left")
tip.set_font_size(20)
tip.set_header_font_size(16)
tip.set_footer_font_size(15)
tip.set_background_color(0.08, 0.11, 0.18, 0.92)
tip.set_border_color(0.54, 0.80, 1.00, 0.95)
tip.set_header_text_color(0.80, 0.90, 1.00, 1.00)
tip.set_footer_text_color(0.74, 0.78, 0.86, 1.00)
tip.show(
    text="去码头找渔夫，打听暴风雨前的异常声响。",
    header_text="支线任务",
    footer_text="任务日志已更新")
scene.flush()
```

### Python：更接近 DigitalHuman 的三段式气泡

```python
reply = scene.bubble.get_or_create("assistant-reply")
reply.attach_to_entity("DigitalHumanBody", use_model_top_anchor=True)
reply.set_world_offset(0.0, 0.20, 0.0)
reply.set_screen_offset(0.0, -16.0)
reply.set_width(440)
reply.set_text_alignment("left")
reply.show(
    text="当然可以，我已经帮你整理好了今天的待办。",
    header_text="你：今天有什么安排？",
    footer_text="语音回复中...")
```

说明：

- C# 可以直接保留 `RuntimeDialogueBubble` 引用跨帧复用；Python 通常每次通过 `scene.bubble.get_or_create(name)` 取同名气泡即可。
- Python 里的 `scene.bubble.names` / `visible_names` 是当前脚本事件上下文的快照；如果你在同一事件里更新后想立刻刷新 UI，记得调用 `scene.flush()`。
- `entity` 模式下如果目标是 PMX 模型，会优先用模型顶部中心作为锚点；不是 PMX 时回退到实体当前 `Position`。

### 完整示例：按住录音 ASR -> 可编辑文本框 -> 点击发送 -> 气泡流式回复 + 句子级顺序 Speak

假设：

- 这个脚本绑定在要说话的角色实体上
- 场景里有一个录音按钮：`RecordButton`
- 场景里有一个输入文本框：`PromptInput`
- 场景里有一个发送按钮：`SendButton`
- `RecordButton` 使用按钮默认事件里的 `pressed` / `released`

这个示例实现的行为是：

1. 按住 `RecordButton` 时启动本地麦克风流式 ASR
2. ASR 的部分结果会实时填充到 `PromptInput`
3. 松开按钮时停止录音
4. 用户可以手动修改 `PromptInput` 中的文本
5. 点击 `SendButton` 后，把文本框内容发给 `Scene.Llm.StartChat(...)`
6. LLM 流式返回时，一边更新角色头顶气泡，一边按“句子”切分回复
7. 每次只调用一句 `SpeakWithCallback(...)`
8. 必须等上一句播完，才会播下一句，不会重叠

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;

static class VoiceChatState
{
    public const string BubbleName = "voice-chat-bubble";
    public const string TtsDoneCallback = "voice_chat_tts_done";

    public static string AsrRequestId = string.Empty;
    public static string LlmRequestId = string.Empty;
    public static string CurrentUserText = string.Empty;
    public static string CurrentAssistantText = string.Empty;
    public static string PendingSentenceBuffer = string.Empty;
    public static Queue<string> SpeakQueue = new();
    public static bool IsRecording;
    public static bool IsSpeaking;
    public static bool ReplyCompleted;
}

RuntimeDialogueBubble GetChatBubble()
{
    RuntimeDialogueBubble bubble = Scene.Bubble.GetOrCreate(VoiceChatState.BubbleName);
    bubble.AttachToEntity(Entity.Id, useModelTopAnchor: true);
    bubble.SetWorldOffset(0.0f, 0.20f, 0.0f);
    bubble.SetScreenOffset(0.0f, -16.0f);
    bubble.Width = 440.0f;
    bubble.TextAlignment = "left";
    bubble.FontSize = 20.0f;
    bubble.HeaderFontSize = 16.0f;
    bubble.FooterFontSize = 14.0f;
    bubble.BackgroundColor = new Vector4(0.08f, 0.10f, 0.16f, 0.92f);
    bubble.BorderColor = new Vector4(0.58f, 0.82f, 1.0f, 0.95f);
    bubble.HeaderTextColor = new Vector4(0.82f, 0.90f, 1.0f, 1.0f);
    bubble.TextColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
    bubble.FooterTextColor = new Vector4(0.76f, 0.80f, 0.88f, 1.0f);
    return bubble;
}

void ShowListeningBubble(string userText, string footer)
{
    RuntimeDialogueBubble bubble = GetChatBubble();
    bubble.SetContent(
        userText,
        headerText: "正在听你说",
        footerText: footer);
    bubble.Show();
}

void ShowReplyBubble(string userText, string assistantText, string footer)
{
    RuntimeDialogueBubble bubble = GetChatBubble();
    bubble.SetContent(
        assistantText,
        headerText: string.IsNullOrWhiteSpace(userText) ? "语音助手" : $"你：{userText}",
        footerText: footer);
    bubble.Show();
}

void ResetReplyState(bool stopSpeaking)
{
    VoiceChatState.CurrentAssistantText = string.Empty;
    VoiceChatState.PendingSentenceBuffer = string.Empty;
    VoiceChatState.SpeakQueue.Clear();
    VoiceChatState.ReplyCompleted = false;
    VoiceChatState.IsSpeaking = false;

    if (stopSpeaking)
    {
        Entity.StopSpeaking();
    }
}

bool IsSentenceEnding(char ch)
{
    return ch is '。' or '！' or '？' or '；' or '.' or '!' or '?' or ';' or '\n';
}

IEnumerable<string> DrainCompletedSentences(ref string buffer, bool flushTail)
{
    List<string> result = [];
    int sentenceStart = 0;

    for (int i = 0; i < buffer.Length; i++)
    {
        if (!IsSentenceEnding(buffer[i]))
        {
            continue;
        }

        string sentence = buffer[sentenceStart..(i + 1)].Trim();
        if (!string.IsNullOrWhiteSpace(sentence))
        {
            result.Add(sentence);
        }

        sentenceStart = i + 1;
    }

    string tail = sentenceStart >= buffer.Length ? string.Empty : buffer[sentenceStart..];
    if (flushTail)
    {
        string finalSentence = tail.Trim();
        if (!string.IsNullOrWhiteSpace(finalSentence))
        {
            result.Add(finalSentence);
        }

        buffer = string.Empty;
    }
    else
    {
        buffer = tail;
    }

    return result;
}

void EnqueueAssistantSpeech(string text, bool flushTail)
{
    if (!string.IsNullOrEmpty(text))
    {
        VoiceChatState.PendingSentenceBuffer += text;
    }

    foreach (string sentence in DrainCompletedSentences(ref VoiceChatState.PendingSentenceBuffer, flushTail))
    {
        VoiceChatState.SpeakQueue.Enqueue(sentence);
    }
}

void TrySpeakNext()
{
    if (VoiceChatState.IsSpeaking || VoiceChatState.SpeakQueue.Count == 0)
    {
        return;
    }

    string sentence = VoiceChatState.SpeakQueue.Dequeue();
    VoiceChatState.IsSpeaking = true;
    ShowReplyBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, "语音回复中...");
    Entity.SpeakWithCallback(sentence, VoiceChatState.TtsDoneCallback);
}

string GetPromptInputText()
{
    return Scene.GetGuiControl("PromptInput")?.Value.Trim() ?? string.Empty;
}

void SetPromptInputText(string text)
{
    Scene.GetGuiControl("PromptInput")?.SetValue(text ?? string.Empty);
}

void StartReplyFromPromptInput()
{
    string prompt = GetPromptInputText();
    if (string.IsNullOrWhiteSpace(prompt))
    {
        return;
    }

    VoiceChatState.CurrentUserText = prompt;
    ResetReplyState(stopSpeaking: true);

    ShowReplyBubble(prompt, string.Empty, "思考中...");

    VoiceChatState.LlmRequestId = Scene.Llm.StartChat(
        Entity,
        prompt,
        systemPrompt: "你是一个中文语音助手，请简洁、自然地回答用户的问题。",
        onDeltaCallback: "voice_chat_llm_delta",
        onCompletedCallback: "voice_chat_llm_done",
        onErrorCallback: "voice_chat_llm_error");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "pressed")
{
    if (!Scene.Asr.Enabled || VoiceChatState.IsRecording)
    {
        return;
    }

    VoiceChatState.IsRecording = true;
    VoiceChatState.CurrentUserText = string.Empty;
    ResetReplyState(stopSpeaking: true);
    SetPromptInputText(string.Empty);
    ShowListeningBubble(string.Empty, "请说话，松开按钮后停止录音");

    VoiceChatState.AsrRequestId = Scene.Asr.StartStreamingRecognition(
        Entity,
        onPartialCallback: "voice_chat_asr_partial",
        onCompletedCallback: "voice_chat_asr_completed",
        onErrorCallback: "voice_chat_asr_error");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "released")
{
    if (!VoiceChatState.IsRecording)
    {
        return;
    }

    VoiceChatState.IsRecording = false;
    Scene.Asr.StopStreamingRecognition(VoiceChatState.AsrRequestId);
    ShowListeningBubble(VoiceChatState.CurrentUserText, "录音已结束，可编辑后点击发送");
}

if (IsGuiEvent && GuiControlName == "SendButton" && GuiEventName == "clicked")
{
    if (VoiceChatState.IsRecording)
    {
        VoiceChatState.IsRecording = false;
        Scene.Asr.StopStreamingRecognition(VoiceChatState.AsrRequestId);
    }

    StartReplyFromPromptInput();
}

if (IsAsrEvent && AsrCallbackName == "voice_chat_asr_partial")
{
    if (!string.Equals(AsrRequestId, VoiceChatState.AsrRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.CurrentUserText = AsrText;
    SetPromptInputText(AsrText);
    ShowListeningBubble(AsrText, "请继续说，松开按钮后停止录音");
}

if (IsAsrEvent && AsrCallbackName == "voice_chat_asr_completed")
{
    if (!string.Equals(AsrRequestId, VoiceChatState.AsrRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.IsRecording = false;
    VoiceChatState.CurrentUserText = AsrText.Trim();
    SetPromptInputText(VoiceChatState.CurrentUserText);
    ShowReplyBubble(VoiceChatState.CurrentUserText, string.Empty, "可编辑文本框后点击发送");
}

if (IsAsrEvent && AsrCallbackName == "voice_chat_asr_error")
{
    if (!string.Equals(AsrRequestId, VoiceChatState.AsrRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.IsRecording = false;
    ShowReplyBubble(VoiceChatState.CurrentUserText, string.Empty, "ASR 出错");
    Console.Error.WriteLine(AsrError);
}

if (IsLlmEvent && LlmCallbackName == "voice_chat_llm_delta")
{
    if (!string.Equals(LlmRequestId, VoiceChatState.LlmRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.CurrentAssistantText = LlmText;
    ShowReplyBubble(
        VoiceChatState.CurrentUserText,
        VoiceChatState.CurrentAssistantText,
        VoiceChatState.IsSpeaking ? "语音回复中..." : "正在生成回复...");

    EnqueueAssistantSpeech(LlmDelta, flushTail: false);
    TrySpeakNext();
}

if (IsLlmEvent && LlmCallbackName == "voice_chat_llm_done")
{
    if (!string.Equals(LlmRequestId, VoiceChatState.LlmRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.ReplyCompleted = true;
    VoiceChatState.CurrentAssistantText = LlmText;
    EnqueueAssistantSpeech(string.Empty, flushTail: true);

    ShowReplyBubble(
        VoiceChatState.CurrentUserText,
        VoiceChatState.CurrentAssistantText,
        VoiceChatState.SpeakQueue.Count > 0 || VoiceChatState.IsSpeaking
            ? "语音回复中..."
            : "回复完成");

    TrySpeakNext();
}

if (IsSpeechEvent && SpeechCallbackName == VoiceChatState.TtsDoneCallback)
{
    VoiceChatState.IsSpeaking = false;
    TrySpeakNext();

    if (!VoiceChatState.IsSpeaking && VoiceChatState.SpeakQueue.Count == 0 && VoiceChatState.ReplyCompleted)
    {
        ShowReplyBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, "回复完成");
    }
}

if (IsLlmEvent && LlmCallbackName == "voice_chat_llm_error")
{
    if (!string.Equals(LlmRequestId, VoiceChatState.LlmRequestId, StringComparison.Ordinal))
    {
        return;
    }

    ShowReplyBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, "LLM 出错");
    Console.Error.WriteLine(LlmError);
}
```

这个例子的关键点：

- `RecordButton` 的 `pressed` / `released` 只负责开始和停止 ASR，不会自动发给 LLM
- `PromptInput` 始终保留为最终可编辑输入源，所以用户可以修正 ASR 结果
- 发送时走已经实现好的 `Scene.Llm.StartChat(...)`
- 对话气泡每次都按“用户输入 + 助手回复 + 状态 footer”更新，比较接近 `DigitalHuman` 的呈现方式
- `LlmDelta` 只用来做“句子切分入队”；真正显示给用户的完整文本始终用 `LlmText`
- `SpeakWithCallback(...)` + `Queue<string>` 保证一句播完以后才播下一句，不会出现上一句没播完就插播下一句

如果你想写 Python 版，核心 API 对应关系就是：

- `Scene.Asr.StartStreamingRecognition(...)` -> `scene.asr.start_streaming_recognition(...)`
- `Scene.Llm.StartChat(...)` -> `scene.llm.start_chat(...)`
- `Scene.Bubble.GetOrCreate(...)` -> `scene.bubble.get_or_create(...)`
- `Entity.SpeakWithCallback(...)` -> `entity.speak(..., on_completed="callback_name")`

## GUI API

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

样式配置在 GameEditor 中编辑，包括背景色、悬停色、按下色、文字色、边框色、边框宽度、圆角、水平对齐、垂直对齐。

## 2D Sprite API

2D Sprite 绘制在 GUI 层背景 draw list 上，适合 HUD 图标、头像、提示图、Render Texture 预览。

属性：

| C# 属性 | Python 快照 | 说明 |
| --- | --- | --- |
| `Id` | `id` | 精灵 Id。 |
| `Name` | `name` | 精灵名称。 |
| `Path` / `Texture` | `path` / `texture` | 图片路径或 `rt:<name>`。 |
| `X` / `Y` | `x` / `y` | 屏幕像素坐标。 |
| `Width` / `Height` | `width` / `height` | 尺寸。 |
| `RotationDegrees` | 无 | C# 可设置旋转角度。 |
| `Opacity` | `opacity` | 透明度。 |
| `DrawOrder` | 无 | 绘制顺序。 |
| `Visible` | `visible` | 是否显示。 |

C#：

```csharp
RuntimeSpriteControl? portrait = Scene.GetSprite("Portrait");
if (portrait is not null)
{
    portrait.SetPosition(24, 420);
    portrait.SetSize(160, 160);
    portrait.Opacity = 0.9f;
    portrait.Texture = "assets/textures/portrait.png";
    portrait.Show();
}
```

Python：

```python
portrait = scene.get_sprite("Portrait")
if portrait is not None:
    portrait.set_position(24, 420)
    portrait.set_size(160, 160)
    portrait.set_opacity(0.9)
    portrait.set_texture("assets/textures/portrait.png")
    portrait.show()
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

## Window / Runtime 设置

窗口设置可在 GameEditor 的 Project 面板中配置，也可以脚本运行时修改。

C#：

```csharp
Scene.Window.SetTitle("Battle Scene");
Scene.Window.SetSize(1600, 900);
Scene.Window.SetFullscreen(false);
Scene.Window.SetResizable(true);
Scene.Window.SetTimingMode("time_synchronized");

Console.WriteLine(Scene.Runtime.ComputeBackend);
Console.WriteLine(Scene.Runtime.IsUsingOpenCL);
Scene.Runtime.SetUseOpenCL(true);
```

Python：

```python
scene.window.set_title("Battle Scene")
scene.window.set_size(1600, 900)
scene.window.set_fullscreen(False)
scene.window.set_resizable(True)
scene.window.set_timing_mode("time_synchronized")

print(scene.runtime.compute_backend)
print(scene.runtime.is_using_opencl)
scene.runtime.set_use_opencl(True)
```

Runtime 设置：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Runtime.UseOpenCL` | `scene.runtime.use_opencl` | 项目/运行时是否请求使用 OpenCL。它表示“希望启用”，不代表当前一定已经使用。 |
| `Scene.Runtime.IsUsingOpenCL` | `scene.runtime.is_using_opencl` | 当前已加载 PMX 是否实际使用 OpenCL 后端。OpenCL 不可用或初始化失败时为 `false`。 |
| `Scene.Runtime.ComputeBackend` | `scene.runtime.compute_backend` | 当前实际计算后端，通常为 `OpenCL` 或 `CPU`。 |
| `Scene.Runtime.SetUseOpenCL(value)` | `scene.runtime.set_use_opencl(value)` | 切换是否请求 OpenCL；GamePlayer 会重新应用运行时设置，失败时自动回退 CPU。 |

窗口设置：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Window.Title` | `scene.window.title` | 当前窗口标题快照。C# 可直接赋值，Python 用 `set_title` 修改。 |
| `Scene.Window.Width` / `Height` | `scene.window.width` / `height` | 当前配置窗口尺寸。Python 是事件快照。 |
| `Scene.Window.Fullscreen` | `scene.window.fullscreen` | 是否全屏。C# 可直接赋值，Python 用 `set_fullscreen` 修改。 |
| `Scene.Window.Resizable` | `scene.window.resizable` | 是否允许调整窗口大小。C# 可直接赋值，Python 用 `set_resizable` 修改。 |
| `Scene.Window.TimingMode` | `scene.window.timing_mode` | 动画计时模式。C# 可直接赋值，Python 用 `set_timing_mode` 修改。 |
| `Scene.Window.SetSize(width, height)` | `scene.window.set_size(width, height)` | 设置窗口尺寸。 |

`TimingMode` 可用值：

- `time_synchronized`：按真实时间推进动画，帧率波动时动画速度保持稳定。
- `frame_rate_dependent`：按帧推进，帧率下降时动画会变慢。

窗口尺寸最小会被限制到 `320 x 240`。

OpenCL 说明：

- `UseOpenCL` 是偏好设置；实际后端以 `IsUsingOpenCL` / `ComputeBackend` 为准。
- 如果机器没有可用 OpenCL GPU、驱动枚举失败、`skinned_animation.cl` 编译失败或初始化失败，GamePlayer 会回退到 CPU。
- 切换 OpenCL 会让已加载 PMX 重新加载当前模型以应用后端变化，运行中切换可能有短暂停顿。

## Camera API

相机支持多相机、主相机、投影参数、相机控制模式、射线、Render Texture 绑定。

常用属性：

| C# 属性 | Python 快照 | 说明 |
| --- | --- | --- |
| `Position` | `position` | 相机位置。 |
| `Target` | `target` | 相机目标点。 |
| `Forward` | `forward` | 前方向。 |
| `Up` | `up` | 上方向。 |
| `Right` | `right` | 右方向。 |
| `Width` / `Height` | `width` / `height` | 当前渲染尺寸。启用 Camera Viewport 时，窗口主渲染仍按各 Viewport 区域绘制。 |
| `ControlMode` | `control_mode` | 当前控制模式。 |
| `ProjectionMode` | `projection_mode` | `perspective` 或 `orthographic`。 |
| `Fov` | `fov` | 透视相机视野角。 |
| `OrthographicSize` | `orthographic_size` | 正交相机尺寸。 |
| `NearClipPlane` | `near_clip_plane` | 近裁剪面。 |
| `FarClipPlane` | `far_clip_plane` | 远裁剪面。 |
| `MainCamera` | `main_camera` | 当前主相机名称。 |
| `CameraNames` | `camera_names` | 场景相机名称列表。 |
| `RenderTextureNames` | `render_texture_names` | Render Texture 名称列表。 |

运行时控制参数：

| C# 属性 | Python 快照 | 说明 |
| --- | --- | --- |
| `TargetEntity` | 无 | 跟随类相机目标实体。Python 用模式函数或 `configure_control` 设置。 |
| `SubjectEntity` | 无 | 锁定相机的主控实体。 |
| `Distance` | 无 | 跟随距离。 |
| `HeightOffset` | 无 | 高度偏移。 |
| `ShoulderOffset` | 无 | 肩位偏移。 |
| `Smoothing` | 无 | 平滑系数。 |
| `MoveSpeed` | 无 | 自由飞行 / RTS 移动速度。 |
| `MouseSensitivity` | 无 | 鼠标灵敏度。 |

基础控制：

```csharp
Scene.Camera.SetLookAt(0, 3, 8, 0, 1, 0);
Scene.Camera.ProjectionMode = "perspective";
Scene.Camera.Fov = 45.0f;
Scene.Camera.NearClipPlane = 0.1f;
Scene.Camera.FarClipPlane = 1000.0f;
Scene.Camera.Orbit(10, -5);
Scene.Camera.Pan(20, 0);
Scene.Camera.Dolly(-1);
```

```python
scene.camera.set_look_at(0, 3, 8, 0, 1, 0)
scene.camera.orbit(10, -5)
scene.camera.pan(20, 0)
scene.camera.dolly(-1)
```

动态调整当前相机模式参数：

```csharp
Scene.Camera.ConfigureControl(distance: 6.0f, height: 1.8f, smoothing: 10.0f);
Scene.Camera.SetYawPitch(yawDegrees: 45.0f, pitchDegrees: -12.0f);
Scene.Camera.SetMouseLook(enabled: true, requireRightMouse: true);
```

```python
scene.camera.configure_control(distance=6.0, height=1.8, smoothing=10.0)
scene.camera.set_yaw_pitch(45.0, -12.0)
scene.camera.set_mouse_look(True, require_right_mouse=True)
```

Camera Viewport：

```csharp
// 以 Project -> Window / Runtime 的 Width / Height 为基准做相对缩放。
Scene.Camera.SetCameraViewport("Main Camera", 0, 0, 960, 720, "relative");
Scene.Camera.EnableCameraViewport("Main Camera", true);

// 第二个相机渲染到右上角区域，形成分屏 / 小窗视角。
Scene.Camera.SetCameraLookAt("Side Camera", 6, 3, 6, 0, 1, 0);
Scene.Camera.SetCameraViewport("Side Camera", 960, 0, 320, 240, "relative");
Scene.Camera.EnableCameraViewport("Side Camera", true);

// 关闭后该相机不再参与窗口 Viewport 渲染。
Scene.Camera.EnableCameraViewport("Side Camera", false);
```

```python
scene.camera.set_camera_viewport("Main Camera", 0, 0, 960, 720, "relative")
scene.camera.enable_camera_viewport("Main Camera", True)

scene.camera.set_camera_look_at("Side Camera", 6, 3, 6, 0, 1, 0)
scene.camera.set_camera_viewport("Side Camera", 960, 0, 320, 240, "relative")
scene.camera.enable_camera_viewport("Side Camera", True)
```

`layout_mode` 可用 `relative` 或 `absolute`。`relative` 会以项目窗口基准分辨率缩放 Viewport；`absolute` 直接使用像素值。如果没有任何相机启用 Viewport，GamePlayer 使用主相机全屏渲染。

相机模式：

```csharp
Scene.Camera.UseEditorOrbitMode();
Scene.Camera.UseMaxEditorMode();
Scene.Camera.UseThirdPersonMode("Player", distance: 5.0f, height: 1.5f);
Scene.Camera.UseTpsMode("Player", distance: 5.0f, height: 1.5f);
Scene.Camera.UseShoulderMode("Player", distance: 4.0f, height: 1.6f, shoulderOffset: 0.55f);
Scene.Camera.UseLockOnMode("Player", "Enemy", distance: 5.0f, height: 1.6f, safeRadius: 0.25f);
Scene.Camera.UseFirstPersonMode("Player", eyeHeight: 1.65f);
Scene.Camera.UseFpsMode("Player", eyeHeight: 1.65f);
Scene.Camera.UseFreeFlyMode(moveSpeed: 5.0f, mouseSensitivity: 0.15f);
Scene.Camera.UseRtsMode(height: 12.0f, pitch: 55.0f, moveSpeed: 8.0f);
Scene.Camera.UseTopDownMode("Player", height: 12.0f);
Scene.Camera.UseIsometricMode("Player", distance: 12.0f);
Scene.Camera.UseSideScrollerMode("Player", distance: 10.0f, height: 1.5f);
Scene.Camera.UseFixedMode(0, 3, 8, 0, 1, 0);
Scene.Camera.UseCinematicFollowMode("Player", offsetX: 0, offsetY: 2, offsetZ: 6);
Scene.Camera.UseOrbitalFollowMode("Player", distance: 6.0f, height: 1.5f, yawSpeed: 20.0f);
Scene.Camera.UseCustomMode();
```

```python
scene.camera.use_editor_orbit_mode()
scene.camera.use_max_editor_mode()
scene.camera.use_tps_mode("Player", distance=5.0, height=1.5)
scene.camera.use_third_person_mode("Player", distance=5.0, height=1.5)
scene.camera.use_shoulder_mode("Player", distance=4.0, height=1.6, shoulder_offset=0.55)
scene.camera.use_lock_on_mode("Player", "Enemy", distance=5.0, height=1.6, safe_radius=0.25)
scene.camera.use_fps_mode("Player", eye_height=1.65)
scene.camera.use_first_person_mode("Player", eye_height=1.65)
scene.camera.use_free_fly_mode(move_speed=5.0, mouse_sensitivity=0.15)
scene.camera.use_rts_mode(height=12.0, pitch=55.0, move_speed=8.0)
scene.camera.use_top_down_mode("Player", height=12.0)
scene.camera.use_isometric_mode("Player", distance=12.0)
scene.camera.use_side_scroller_mode("Player", distance=10.0, height=1.5)
scene.camera.use_fixed_mode(0, 3, 8, 0, 1, 0)
scene.camera.use_cinematic_follow_mode("Player", offset_y=2, offset_z=6)
scene.camera.use_orbital_follow_mode("Player", distance=6.0, height=1.5, yaw_speed=20.0)
scene.camera.use_custom_mode()
```

自定义模式示例：

```csharp
if (IsStart)
{
    Scene.Camera.UseCustomMode();
}

if (IsUpdate)
{
    Vector3 p = Entity.Position;
    Scene.Camera.SetLookAt(p.X, p.Y + 2.0f, p.Z + 6.0f, p.X, p.Y + 1.2f, p.Z);
}
```

```python
def start(entity, scene, input, audio):
    scene.camera.use_custom_mode()

def update(entity, scene, input, audio, delta_seconds):
    p = entity.position
    scene.camera.set_look_at(p[0], p[1] + 2.0, p[2] + 6.0, p[0], p[1] + 1.2, p[2])
```

## 射线与拾取

`ScreenPointToRay` 类似 Unity 的 `camera.ScreenPointToRay(Input.mousePosition)`。

C#：

```csharp
RuntimeRay ray = Scene.Camera.ScreenPointToRay(Input.MouseX, Input.MouseY);
RuntimeRay mouseRay = Scene.Camera.MousePointToRay(Input);
RuntimeRay centerRay = Scene.Camera.ViewportPointToRay(0.5f, 0.5f);
RuntimeEntity? picked = Scene.Camera.PickEntity(Input.MouseX, Input.MouseY);

if (ray.TryIntersectPlaneY(0.0f, out Vector3 ground))
{
    Entity.SetPosition(ground.X, ground.Y, ground.Z);
}

if (Scene.Camera.RaycastEntity(ray, out RuntimeRaycastHit hit))
{
    Console.WriteLine($"hit {hit.Entity.Name}, shape={hit.ColliderShape}, collider={hit.ColliderName}");
}
```

`RuntimeRay` C# 方法：

| 方法 | 说明 |
| --- | --- |
| `GetPoint(distance)` | 获取射线上指定距离的世界坐标点。 |
| `TryIntersectPlaneY(y, out point)` | 与水平面 `Y = y` 求交。 |
| `TryIntersectSphere(center, radius, out distance)` | 与球体求交。 |

Python：

```python
ray = scene.camera.screen_point_to_ray(input.mouse_x, input.mouse_y)
ground = ray.intersect_plane_y(0.0)
if ground is not None:
    entity.set_position(ground[0], ground[1], ground[2])

hit = entity.raycast(ray)
if hit is not None:
    print(hit["distance"], hit["point"], hit["shape"])
```

Python `Ray` 方法：

| 方法 | 说明 |
| --- | --- |
| `get_point(distance)` | 获取射线上指定距离的世界坐标点。 |
| `intersect_plane_y(y)` | 与水平面 `Y = y` 求交，未命中返回 `None`。 |
| `intersect_sphere(center, radius)` | 与球体求交，返回距离或 `None`。 |
| `intersect_capsule(capsule)` | 与胶囊体求交，返回命中信息或 `None`。 |
| `intersect_box(box)` | 与盒体求交，返回命中信息或 `None`。 |
| `intersect_collider(collider)` | 按 Collider 形状自动求交。 |

射线检测规则：

- 优先检测实体显式绑定的 Collider，返回最近命中。
- 只有 `pmx_model` 在没有显式 Collider 时会使用中心包围球 fallback。
- `water_surface`、`particle_system`、`empty_object`、`textured_plane` 如果需要被射线命中，应在编辑器中添加 Collider。
- 当前不会对 PMX 三角面做逐面相交检测。
- C# 提供 `Scene.Camera.RaycastEntity(...)` 做场景级拾取。
- Python 当前没有场景级 `scene.camera.raycast_entity(...)` 桥接方法；Python 可以用 `entity.raycast(ray)` 检测当前实体，或对已知实体调用 `scene.get_entity(name)` 后逐个检测。

射线调试绘制：

```csharp
RuntimeRay ray = Scene.Camera.MousePointToRay(Input);
Scene.Debug.DrawRay(ray.Origin, ray.Direction, length: 20.0f, durationSeconds: 0.05f);
Scene.Debug.DrawLine(new Vector3(0, 0, 0), new Vector3(0, 2, 0), durationSeconds: 1.0f);
```

```python
ray = scene.camera.mouse_point_to_ray(input)
scene.debug.draw_ray(ray.origin, ray.direction, length=20.0, color=[1, 0, 0, 1], duration=0.05)
scene.debug.draw_line([0, 0, 0], [0, 2, 0], color=[1, 1, 0, 1], duration=1.0)
```

## Collision / Collider API

每个实体都可以绑定多个 Collider。Collider 相对于绑定对象本地坐标，实体移动、旋转、缩放时 Collider 会跟随变换。

支持形状：

- `capsule`：胶囊体。
- `box`：有向盒体。

C#：

```csharp
if (IsStart)
{
    Entity.ClearColliders();

    Entity.AddCapsuleCollider(
        name: "Body",
        radius: 0.35f,
        height: 1.7f,
        centerX: 0.0f,
        centerY: 0.85f,
        centerZ: 0.0f,
        axis: "y");

    Entity.AddBoxCollider(
        name: "PickupRange",
        sizeX: 1.2f,
        sizeY: 1.0f,
        sizeZ: 1.2f,
        centerX: 0.0f,
        centerY: 0.5f,
        centerZ: 0.0f);
}

if (IsUpdate)
{
    RuntimeEntity? enemy = Scene.GetEntity("Enemy");
    if (enemy is not null && Entity.CheckCollision(enemy))
    {
        Console.WriteLine("colliding");
    }
}
```

Python：

```python
def start(entity, scene, input, audio):
    entity.clear_colliders()
    entity.add_capsule_collider(
        name="Body",
        radius=0.35,
        height=1.7,
        center_y=0.85,
        axis="y")
    entity.add_box_collider(
        name="PickupRange",
        size_x=1.2,
        size_y=1.0,
        size_z=1.2,
        center_y=0.5)

def update(entity, scene, input, audio, delta_seconds):
    enemy = scene.get_entity("Enemy")
    if enemy is not None and entity.check_collision(enemy):
        print("colliding")
```

API 列表：

| C# | Python | 说明 |
| --- | --- | --- |
| `SetCapsuleCollider(...)` | `set_capsule_collider(...)` | 清空并设置单个胶囊 Collider。 |
| `AddCapsuleCollider(...)` | `add_capsule_collider(...)` | 增加胶囊 Collider。 |
| `AddBoxCollider(...)` | `add_box_collider(...)` | 增加盒体 Collider。 |
| `RemoveCollider(idOrName)` | `remove_collider(id_or_name)` | 删除指定 Collider。 |
| `ClearColliders()` | `clear_colliders()` | 清空所有 Collider。 |
| `DisableCollider()` | `disable_collider()` | 禁用所有 Collider。 |
| `Raycast(ray, out distance, out point)` | `raycast(ray)` | 检测射线与本实体 Collider。 |
| `CheckCollision(other)` | `check_collision(other)` | 检测两个实体 Collider 是否相交。 |
| `DistanceToCollider(other)` | `distance_to_collider(other)` | 计算 Collider 间距离。 |
| `TryGetCapsule(out capsule)` | `capsule()` | 获取第一个胶囊 Collider，通常用于兼容旧逻辑。 |

水面交互：

- 在水面对象上开启 `Enable water interaction` 后，GamePlayer 会检测其它实体 Collider 与水面的接触。
- 对粒子系统，如果 `EnableWaterInteraction` 开启，则会按每个活跃粒子的当前位置和当前显示尺寸做近似球检测，不再依赖实体级 Collider。
- 水面对象的 `ParticleRippleMinIntervalSeconds` 和 `ParticleRippleMergeDistance` 控制粒子触水波纹的密度与合并粒度。
- 接触时生成视觉波纹。
- 该功能不提供浮力、刚体推挤或动力学响应。

## 多相机与 Render Texture

场景支持多个相机和多个 Render Texture。Render Texture 可以作为 2D Sprite 贴图，也可以赋给 PMX 材质。

C#：

```csharp
Console.WriteLine(Scene.Camera.MainCamera);
foreach (string name in Scene.Camera.CameraNames)
{
    Console.WriteLine(name);
}

Scene.Camera.SetMainCamera("Battle Camera");
Scene.Camera.SetCameraViewport("Battle Camera", 0, 0, 960, 720, "relative");
Scene.Camera.EnableCameraViewport("Battle Camera", true);
Scene.Camera.SetCameraLookAt(
    "MiniMap Camera",
    positionX: 0, positionY: 20, positionZ: 0,
    targetX: 0, targetY: 0, targetZ: 0);
Scene.Camera.SetCameraViewport("MiniMap Camera", 960, 0, 320, 240, "relative");
Scene.Camera.EnableCameraViewport("MiniMap Camera", true);
Scene.Camera.BindRenderTextureCamera("MiniMapRT", "MiniMap Camera");

RuntimeSpriteControl? miniMap = Scene.GetSprite("MiniMap");
miniMap?.SetRenderTexture("MiniMapRT");

Entity.SetMaterialRenderTexture(0, "MiniMapRT");
```

Python：

```python
print(scene.camera.main_camera)
print(scene.camera.camera_names)
print(scene.camera.render_texture_names)

scene.camera.set_main_camera("Battle Camera")
scene.camera.set_camera_viewport("Battle Camera", 0, 0, 960, 720, "relative")
scene.camera.enable_camera_viewport("Battle Camera", True)
scene.camera.set_camera_look_at("MiniMap Camera", 0, 20, 0, 0, 0, 0)
scene.camera.set_camera_viewport("MiniMap Camera", 960, 0, 320, 240, "relative")
scene.camera.enable_camera_viewport("MiniMap Camera", True)
scene.camera.bind_render_texture_camera("MiniMapRT", "MiniMap Camera")

mini_map = scene.get_sprite("MiniMap")
if mini_map is not None:
    mini_map.set_render_texture("MiniMapRT")

entity.set_material_render_texture(0, "MiniMapRT")
```

限制：

- Render Texture 当前渲染 3D 场景对象。
- 不包含 GamePlayer GUI、加载遮罩、Debug.DrawRay、编辑器坐标轴和编辑器 Collider 线框。
- Camera Viewport 是窗口主渲染区域切分；Render Texture 是离屏渲染目标。两者可以同时使用。

## Save 存档 API

存档目录固定在游戏工程目录下的 `saves/`。API 传入文件名或 `saves/` 下相对路径，例如 `slot1.json`、`chapter1/slot1.json`。运行时会阻止 `../` 逃出 `saves/`。

C#：

```csharp
if (IsStart)
{
    var data = new
    {
        x = Entity.Position.X,
        y = Entity.Position.Y,
        z = Entity.Position.Z
    };

    Scene.Save.WriteJson("slot1.json", data);
}

if (Scene.Save.Exists("slot1.json"))
{
    string raw = Scene.Save.ReadText("slot1.json");
    Console.WriteLine(raw);
}
```

Python：

```python
def start(entity, scene, input, audio):
    scene.save.write_json("slot1.json", {
        "player": {
            "x": entity.position[0],
            "y": entity.position[1],
            "z": entity.position[2],
        }
    })

    data = scene.save.read_json("slot1.json", fallback={})
    print(data)
```

API：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Save.SaveDirectory` | `scene.save.directory` | 存档目录。 |
| `WriteText(fileName, text)` | `write_text(file_name, text)` | 写文本。 |
| `ReadText(fileName, fallback)` | `read_text(file_name, fallback="")` | 读文本。 |
| `WriteJson<T>(fileName, value)` | `write_json(file_name, value)` | 写 JSON。 |
| `ReadJson<T>(fileName, fallback)` | `read_json(file_name, fallback=None)` | 读 JSON。 |
| `Exists(fileName)` | `exists(file_name)` | 文件是否存在。 |
| `Delete(fileName)` | `delete(file_name)` | 删除存档。 |
| `GetFullPath(fileName)` | 无 | 获取完整路径。 |

Python 的 `scene.save.directory` 是只读路径字符串；直接使用 Python `open()` 不会自动限制到 `saves/`，需要脚本自行保证路径安全。推荐优先使用 `scene.save.*` 方法。

## Network 网络通信 API

`Scene.Network` / `scene.network` 提供 HTTP/HTTPS、TCP 和 UDP 通信能力。实现基于 .NET / Python 标准网络库，目标平台为 Windows、Linux 和 macOS。

注意：网络调用可能等待超时。不要在每帧 `update` 里做长时间阻塞请求；实时逻辑应设置较短的 `timeout`，长请求优先放在点击事件、加载脚本或后台逻辑里。脚本网络访问没有额外沙盒限制，访问外网或监听端口时需要遵守系统防火墙、网络权限和目标服务协议。

### C# Network

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    RuntimeHttpResponse response = await Scene.Network.HttpGetAsync(
        "https://example.com",
        timeoutSeconds: 10);

    Console.WriteLine(response.StatusCode);
    Console.WriteLine(response.Body);
}

var payload = new { message = "hello" };
RuntimeHttpResponse post = await Scene.Network.HttpPostJsonAsync(
    "https://example.com/api/messages",
    payload,
    timeoutSeconds: 10);

string tcpReply = await Scene.Network.TcpSendTextAsync(
    "127.0.0.1",
    9000,
    "ping\n",
    timeoutSeconds: 3);

RuntimeTcpMessage tcpMessage = await Scene.Network.TcpReceiveTextOnceAsync(
    9001,
    timeoutSeconds: 10);

string udpReply = await Scene.Network.UdpSendTextAsync(
    "127.0.0.1",
    9002,
    "ping",
    timeoutSeconds: 3);

RuntimeUdpMessage udpMessage = await Scene.Network.UdpReceiveTextAsync(
    9003,
    timeoutSeconds: 10);

await Scene.Network.UdpSendAsync(
    "127.0.0.1",
    9004,
    Encoding.UTF8.GetBytes("fire-and-forget"),
    waitForResponse: false);
```

C# API：

| 方法 | 说明 |
| --- | --- |
| `HttpGetAsync(url, timeoutSeconds, headers)` | 发送 HTTP GET。`url` 必须是绝对 `http://` 或 `https://` 地址。 |
| `HttpPostTextAsync(url, text, contentType, timeoutSeconds, headers)` | 发送文本 POST。 |
| `HttpPostJsonAsync(url, value, timeoutSeconds, headers)` | 序列化对象为 JSON 并发送 POST。 |
| `HttpSendAsync(method, url, body, contentType, timeoutSeconds, headers)` | 自定义 HTTP 方法、请求体和 Content-Type。 |
| `TcpSendTextAsync(host, port, text, timeoutSeconds, encodingName, receiveBytes)` | TCP 连接、发送文本，并读取一次响应。 |
| `TcpSendAsync(host, port, data, timeoutSeconds, receiveBytes)` | TCP 连接、发送字节，并读取一次响应；`receiveBytes <= 0` 时不等待响应。 |
| `TcpReceiveTextOnceAsync(port, timeoutSeconds, encodingName, receiveBytes, listenAddress)` | 启动一次性 TCP 监听，接受一个连接并返回文本。 |
| `TcpReceiveOnceAsync(port, timeoutSeconds, receiveBytes, listenAddress)` | 启动一次性 TCP 监听，接受一个连接并返回字节。 |
| `UdpSendTextAsync(host, port, text, timeoutSeconds, encodingName, receiveBytes, waitForResponse)` | UDP 发送文本；`waitForResponse = false` 时不等待回复。 |
| `UdpSendAsync(host, port, data, timeoutSeconds, receiveBytes, waitForResponse)` | UDP 发送字节；可选择等待一个回复包。 |
| `UdpReceiveTextAsync(port, timeoutSeconds, encodingName, receiveBytes, listenAddress)` | 监听一个 UDP 数据包并返回文本。 |
| `UdpReceiveAsync(port, timeoutSeconds, receiveBytes, listenAddress)` | 监听一个 UDP 数据包并返回字节。 |

返回类型：

| 类型 | 字段 |
| --- | --- |
| `RuntimeHttpResponse` | `StatusCode`、`IsSuccessStatusCode`、`ReasonPhrase`、`Body`、`Headers`、`GetHeader(name)`。 |
| `RuntimeTcpMessage` | `Text`、`Data`、`RemoteHost`、`RemotePort`。 |
| `RuntimeUdpMessage` | `Text`、`Data`、`RemoteHost`、`RemotePort`。 |

### Python Network

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    response = scene.network.http_get("https://example.com", timeout=10)
    print(response["status_code"])
    print(response["body"])

    post = scene.network.http_post_json(
        "https://example.com/api/messages",
        {"message": "hello"},
        timeout=10)

    tcp_reply = scene.network.tcp_send_text(
        "127.0.0.1",
        9000,
        "ping\n",
        timeout=3)

    tcp_message = scene.network.tcp_receive_text_once(9001, timeout=10)

    udp_reply = scene.network.udp_send_text(
        "127.0.0.1",
        9002,
        "ping",
        timeout=3)

    udp_message = scene.network.udp_receive_text(9003, timeout=10)

    scene.network.udp_send(
        "127.0.0.1",
        9004,
        b"fire-and-forget",
        wait_for_response=False)
```

Python API：

| 方法 | 说明 |
| --- | --- |
| `http_get(url, timeout=15, headers=None)` | 发送 HTTP GET。 |
| `http_post_text(url, text, content_type="text/plain; charset=utf-8", timeout=15, headers=None)` | 发送文本 POST。 |
| `http_post_json(url, value, timeout=15, headers=None)` | 序列化对象为 JSON 并发送 POST。 |
| `http_send(method, url, body=None, content_type=None, timeout=15, headers=None)` | 自定义 HTTP 方法、请求体和 Content-Type。 |
| `tcp_send_text(host, port, text, timeout=5, encoding="utf-8", receive_bytes=65536)` | TCP 发送文本并读取一次响应。 |
| `tcp_send(host, port, data, timeout=5, receive_bytes=65536)` | TCP 发送字节并读取一次响应；`receive_bytes <= 0` 时不等待响应。 |
| `tcp_receive_text_once(port, timeout=10, encoding="utf-8", receive_bytes=65536, listen_address="0.0.0.0")` | 一次性 TCP 监听，返回文本消息。 |
| `tcp_receive_once(port, timeout=10, receive_bytes=65536, listen_address="0.0.0.0")` | 一次性 TCP 监听，返回字节消息。 |
| `udp_send_text(host, port, text, timeout=5, encoding="utf-8", receive_bytes=65536, wait_for_response=True)` | UDP 发送文本；可选择等待回复。 |
| `udp_send(host, port, data, timeout=5, receive_bytes=65536, wait_for_response=True)` | UDP 发送字节；可选择等待回复。 |
| `udp_receive_text(port, timeout=10, encoding="utf-8", receive_bytes=65536, listen_address="0.0.0.0")` | 监听一个 UDP 数据包并返回文本。 |
| `udp_receive(port, timeout=10, receive_bytes=65536, listen_address="0.0.0.0")` | 监听一个 UDP 数据包并返回字节。 |

Python 返回值：

| 方法类别 | 返回值 |
| --- | --- |
| HTTP | `dict`：`status_code`、`is_success_status_code`、`reason_phrase`、`body`、`headers`。 |
| TCP / UDP 接收 | `dict`：`text`、`data`、`remote_host`、`remote_port`。 |
| TCP / UDP 发送 | 文本方法返回 `str`，字节方法返回 `bytes`；不等待响应时返回空字节。 |

边界说明：

- HTTP 只支持绝对 `http://` 和 `https://` 地址；当前没有 WebSocket 封装。
- TCP 接收 API 是一次性监听：收到一个连接并读取一次后关闭监听。
- UDP 是无连接数据包协议，可能丢包、乱序或没有响应；只发不等回复时使用 `waitForResponse: false` / `wait_for_response=False`。
- 监听端口可能被系统防火墙、杀毒软件或已有进程占用。
- C# 网络 API 是 `async`；Python 网络 API 是同步阻塞调用。

## ASR API

GamePlayer 支持从脚本调用本地麦克风录音和 ASR 识别。配置在 GameEditor 的 `Project` 标签页 `ASR` 中，也会保存到 `game.project.json` 的 `asr` 节点。

当前支持：

- `SherpaOnnx`
- `Whisper.net`

主要场景是：

- 按下按钮开始录音
- 一边录音一边拿到部分识别结果
- 松开按钮后拿到最终文本
- 再把最终文本交给 `Scene.Llm` 做后续对话逻辑

配置字段：

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本 ASR。 |
| `Provider` | `sherpa` 或 `whisper`。 |
| `InputDeviceIndex` | 本地 `PortAudio` 输入设备索引；为空时使用默认输入设备。 |
| `PartialResultIntervalSeconds` | 流式部分结果推送间隔。对 Whisper 更明显；Sherpa 在线模型会更频繁地产生 partial。 |
| `Capture.SampleRate` / `Channels` / `FramesPerBuffer` | 麦克风打开参数。 |
| `Sherpa.*` | Sherpa 模型路径、线程、采样率、语言、provider 等。 |
| `Whisper.*` | Whisper 模型路径、语言、线程、是否用 GPU 等。 |

### C# ASR

`Scene.Asr` 采用后台录音 + 后台识别 + 脚本事件回调模式。

C# API：

| API | 说明 |
| --- | --- |
| `Scene.Asr.Enabled` | 当前项目是否启用 ASR。 |
| `Scene.Asr.Provider` | 当前 ASR Provider 名称。 |
| `Scene.Asr.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.Asr.PartialResultIntervalSeconds` | 当前部分结果间隔秒数。 |
| `Scene.Asr.IsRecording` | 当前是否在录音。 |
| `StartStreamingRecognition(entity, requestId, onPartialCallback, onCompletedCallback, onErrorCallback)` | 开始后台录音，并持续推送部分识别结果。 |
| `StopStreamingRecognition(requestId)` | 停止指定流式识别；如果不传 requestId，则停止当前录音。 |
| `StopAllStreamingRecognitions()` | 停止全部流式识别。 |

ASR 回调事件字段：

| 字段 | 说明 |
| --- | --- |
| `AsrRequestId` | 识别请求 Id。 |
| `AsrEventName` | `partial`、`completed`、`error`。 |
| `AsrText` | 当前识别文本。 |
| `AsrIsFinal` | 当前是否最终结果。 |
| `AsrError` | 错误文本。 |
| `AsrCallbackName` | 当前命中的回调名。 |
| `AsrOffsetSeconds` | 当前结果对应的音频偏移秒数。 |

完整示例：按下按钮开始录音，录音时实时显示 ASR 结果；松开按钮后把最终文本发送给 LLM，流式接收回复，同时按句子顺序调用 `Speak(...)`。

假设场景里有：

- 一个按钮：`Record Button`
- 一个文本框：`Asr Box`
- 一个标签：`Reply Label`

```csharp
using System.Text;

static class AsrChatState
{
    public static bool Recording;
    public static string AsrRequestId = string.Empty;
    public static string AsrText = string.Empty;
    public static string LlmReplyText = string.Empty;
    public static StringBuilder PendingSentenceBuffer = new();
    public static Queue<string> SpeakQueue = new();
    public static bool Speaking;
}

void ClearReplyUi()
{
    Scene.GetGuiControl("Reply Label")?.SetValue(string.Empty);
    AsrChatState.LlmReplyText = string.Empty;
    AsrChatState.PendingSentenceBuffer.Clear();
    AsrChatState.SpeakQueue.Clear();
    AsrChatState.Speaking = false;
}

void TrySpeakNext()
{
    if (AsrChatState.Speaking || AsrChatState.SpeakQueue.Count == 0)
    {
        return;
    }

    string sentence = AsrChatState.SpeakQueue.Dequeue();
    AsrChatState.Speaking = true;
    Entity.Speak(sentence, () =>
    {
        AsrChatState.Speaking = false;
        TrySpeakNext();
    });
}

void EnqueueCompletedSentences(string text, bool flushTail)
{
    AsrChatState.PendingSentenceBuffer.Append(text);
    string buffer = AsrChatState.PendingSentenceBuffer.ToString();
    int lastCut = 0;
    for (int i = 0; i < buffer.Length; i++)
    {
        char ch = buffer[i];
        if (ch is '。' or '！' or '？' or '!' or '?' or ';' or '；')
        {
            string sentence = buffer[lastCut..(i + 1)].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                AsrChatState.SpeakQueue.Enqueue(sentence);
            }

            lastCut = i + 1;
        }
    }

    if (flushTail)
    {
        string tail = buffer[lastCut..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            AsrChatState.SpeakQueue.Enqueue(tail);
        }

        AsrChatState.PendingSentenceBuffer.Clear();
    }
    else
    {
        AsrChatState.PendingSentenceBuffer.Clear();
        AsrChatState.PendingSentenceBuffer.Append(buffer[lastCut..]);
    }

    TrySpeakNext();
}

if (IsGuiEvent && GuiControlName == "Record Button" && GuiEventName == "pressed")
{
    if (!Scene.Asr.Enabled || AsrChatState.Recording)
    {
        return;
    }

    AsrChatState.Recording = true;
    AsrChatState.AsrText = string.Empty;
    Scene.GetGuiControl("Asr Box")?.SetValue(string.Empty);
    ClearReplyUi();

    AsrChatState.AsrRequestId = Scene.Asr.StartStreamingRecognition(
        Entity,
        onPartialCallback: "asr_partial",
        onCompletedCallback: "asr_completed",
        onErrorCallback: "asr_error");
}

if (IsGuiEvent && GuiControlName == "Record Button" && GuiEventName == "released")
{
    if (!AsrChatState.Recording)
    {
        return;
    }

    AsrChatState.Recording = false;
    Scene.Asr.StopStreamingRecognition(AsrChatState.AsrRequestId);
}

if (IsAsrEvent && AsrCallbackName == "asr_partial")
{
    AsrChatState.AsrText = AsrText;
    Scene.GetGuiControl("Asr Box")?.SetValue(AsrText);
}

if (IsAsrEvent && AsrCallbackName == "asr_completed")
{
    AsrChatState.Recording = false;
    AsrChatState.AsrText = AsrText.Trim();
    Scene.GetGuiControl("Asr Box")?.SetValue(AsrChatState.AsrText);

    if (!string.IsNullOrWhiteSpace(AsrChatState.AsrText))
    {
        ClearReplyUi();
        Scene.Llm.StartChat(
            Entity,
            AsrChatState.AsrText,
            systemPrompt: "你是一个中文语音助手，请简洁、自然地回答用户的问题。",
            onDeltaCallback: "reply_delta",
            onCompletedCallback: "reply_done",
            onErrorCallback: "reply_error");
    }
}

if (IsAsrEvent && AsrCallbackName == "asr_error")
{
    AsrChatState.Recording = false;
    Console.Error.WriteLine(AsrError);
}

if (IsLlmEvent && LlmCallbackName == "reply_delta")
{
    AsrChatState.LlmReplyText = LlmText;
    Scene.GetGuiControl("Reply Label")?.SetValue(AsrChatState.LlmReplyText);
    EnqueueCompletedSentences(LlmDelta, flushTail: false);
}

if (IsLlmEvent && LlmCallbackName == "reply_done")
{
    AsrChatState.LlmReplyText = LlmText;
    Scene.GetGuiControl("Reply Label")?.SetValue(AsrChatState.LlmReplyText);

    string tail = AsrChatState.PendingSentenceBuffer.ToString();
    AsrChatState.PendingSentenceBuffer.Clear();
    if (!string.IsNullOrWhiteSpace(tail))
    {
        EnqueueCompletedSentences(tail, flushTail: true);
    }
}

if (IsLlmEvent && LlmCallbackName == "reply_error")
{
    Console.Error.WriteLine(LlmError);
}
```

### Python ASR

Python API：

| API | 说明 |
| --- | --- |
| `scene.asr.enabled` | 当前项目是否启用 ASR。 |
| `scene.asr.provider` | 当前 ASR Provider 名称。 |
| `scene.asr.input_device_index` | 当前输入设备索引。 |
| `scene.asr.partial_result_interval_seconds` | 当前部分结果间隔秒数。 |
| `scene.asr.is_recording` | 当前是否在录音。 |
| `scene.asr.start_streaming_recognition(request_id=None, on_partial="asr_partial", on_completed="asr_completed", on_error="asr_error")` | 开始后台录音并持续识别。 |
| `scene.asr.stop_streaming_recognition(request_id=None)` | 停止流式识别。 |

Python 通用回调事件字典字段包括：

- `requestId`
- `eventName`
- `text`
- `isFinal`
- `error`
- `callbackName`
- `offsetSeconds`

## ASR API

GamePlayer 支持从脚本调用本地麦克风和 ASR 识别器。配置在 GameEditor 的 `Project` 标签页 `ASR` 中，也会保存到 `game.project.json` 的 `asr` 节点。

当前支持：

- `sherpa`
- `whisper`

常见用法是：

- 按钮 `pressed` 时启动流式录音识别
- 一边录音一边更新识别文本
- 按钮 `released` 时停止录音并拿到最终文本
- 再把最终文本交给 `Scene.Llm` 继续做对话逻辑

项目配置重点：

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本层 ASR。 |
| `Provider` | `sherpa` 或 `whisper`。 |
| `InputDeviceIndex` | 本地 `PortAudio` 输入设备索引；为空表示默认输入设备。 |
| `PartialResultIntervalSeconds` | 流式部分结果刷新间隔。 |
| `Capture` | 麦克风打开参数：`SampleRate`、`Channels`、`FramesPerBuffer`。 |
| `Sherpa.*` | SherpaOnnx 模型与推理参数。 |
| `Whisper.*` | Whisper 模型与推理参数。 |

### C# ASR

C# API：

| API | 说明 |
| --- | --- |
| `Scene.Asr.Enabled` | 当前项目是否启用 ASR。 |
| `Scene.Asr.Provider` | 当前 provider。 |
| `Scene.Asr.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.Asr.PartialResultIntervalSeconds` | 当前部分结果刷新间隔。 |
| `Scene.Asr.IsRecording` | 当前是否正在录音识别。 |
| `StartStreamingRecognition(entity, requestId, onPartialCallback, onCompletedCallback, onErrorCallback)` | 开始后台录音，并推送部分识别结果。 |
| `StopStreamingRecognition(requestId)` | 停止指定流式识别；为空则停止当前录音。 |
| `StopAllStreamingRecognitions()` | 停止所有流式识别任务。 |

ASR 回调字段：

| 字段 | 说明 |
| --- | --- |
| `AsrRequestId` | 当前请求 Id。 |
| `AsrEventName` | `partial`、`completed`、`error`。 |
| `AsrText` | 当前识别文本。 |
| `AsrIsFinal` | 是否最终结果。 |
| `AsrError` | 错误文本。 |
| `AsrCallbackName` | 当前回调名。 |
| `AsrOffsetSeconds` | 当前识别文本对应的音频时长偏移。 |

### Python ASR

Python API：

| API | 说明 |
| --- | --- |
| `scene.asr.enabled` | 当前项目是否启用 ASR。 |
| `scene.asr.provider` | 当前 provider。 |
| `scene.asr.input_device_index` | 当前输入设备索引。 |
| `scene.asr.partial_result_interval_seconds` | 当前部分结果刷新间隔。 |
| `scene.asr.is_recording` | 当前是否正在录音识别。 |
| `scene.asr.start_streaming_recognition(request_id=None, on_partial="asr_partial", on_completed="asr_completed", on_error="asr_error")` | 开始后台录音识别。 |
| `scene.asr.stop_streaming_recognition(request_id=None)` | 停止流式识别。 |

Python 通用事件函数：

```python
def asr_event(entity, scene, input, audio, event):
    print(event["eventName"], event["text"])
```

### 完整示例：按住录音 -> 流式 ASR -> 松开发给 LLM -> 句子级顺序 Speak

假设场景中有：

- 一个按钮：`RecordButton`
- 一个文本框：`AsrInput`
- 一个标签：`Reply`

```csharp
using System.Text;

static class DemoState
{
    public static string AsrRequestId = string.Empty;
    public static string CurrentAsrText = string.Empty;
    public static string LlmAccumulatedText = string.Empty;
    public static string PendingSentenceBuffer = string.Empty;
    public static Queue<string> SpeakQueue = new();
    public static bool IsSpeaking;
}

IEnumerable<string> ExtractCompletedSentences(ref string buffer)
{
    List<string> result = [];
    int start = 0;
    for (int i = 0; i < buffer.Length; i++)
    {
        char ch = buffer[i];
        if (ch is '。' or '！' or '？' or '.' or '!' or '?')
        {
            string sentence = buffer[start..(i + 1)].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                result.Add(sentence);
            }

            start = i + 1;
        }
    }

    buffer = start >= buffer.Length ? string.Empty : buffer[start..];
    return result;
}

void TrySpeakNext()
{
    if (DemoState.IsSpeaking || DemoState.SpeakQueue.Count == 0)
    {
        return;
    }

    string sentence = DemoState.SpeakQueue.Dequeue();
    DemoState.IsSpeaking = true;
    Entity.SpeakWithCallback(sentence, "llm_sentence_done");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "pressed")
{
    DemoState.CurrentAsrText = string.Empty;
    DemoState.LlmAccumulatedText = string.Empty;
    DemoState.PendingSentenceBuffer = string.Empty;
    DemoState.SpeakQueue.Clear();
    DemoState.IsSpeaking = false;

    Scene.GetGuiControl("AsrInput")?.SetValue("");
    Scene.GetGuiControl("Reply")?.SetValue("");

    DemoState.AsrRequestId = Scene.Asr.StartStreamingRecognition(
        Entity,
        requestId: "push_to_talk",
        onPartialCallback: "asr_partial",
        onCompletedCallback: "asr_completed",
        onErrorCallback: "asr_error");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "released")
{
    Scene.Asr.StopStreamingRecognition(DemoState.AsrRequestId);
}

if (IsAsrEvent && AsrCallbackName == "asr_partial")
{
    DemoState.CurrentAsrText = AsrText;
    Scene.GetGuiControl("AsrInput")?.SetValue(AsrText);
}

if (IsAsrEvent && AsrCallbackName == "asr_completed")
{
    DemoState.CurrentAsrText = AsrText.Trim();
    Scene.GetGuiControl("AsrInput")?.SetValue(DemoState.CurrentAsrText);

    if (!string.IsNullOrWhiteSpace(DemoState.CurrentAsrText))
    {
        Scene.Llm.StartChat(
            Entity,
            DemoState.CurrentAsrText,
            systemPrompt: "你是一个中文语音助手，请简洁、自然地回答用户的问题。",
            onDeltaCallback: "reply_delta",
            onCompletedCallback: "reply_done",
            onErrorCallback: "reply_error");
    }
}

if (IsAsrEvent && AsrCallbackName == "asr_error")
{
    Console.Error.WriteLine(AsrError);
}

if (IsLlmEvent && LlmCallbackName == "reply_delta")
{
    DemoState.LlmAccumulatedText = LlmText;
    Scene.GetGuiControl("Reply")?.SetValue(DemoState.LlmAccumulatedText);

    string delta = LlmDelta;
    if (!string.IsNullOrEmpty(delta))
    {
        DemoState.PendingSentenceBuffer += delta;
        foreach (string sentence in ExtractCompletedSentences(ref DemoState.PendingSentenceBuffer))
        {
            DemoState.SpeakQueue.Enqueue(sentence);
        }

        TrySpeakNext();
    }
}

if (IsLlmEvent && LlmCallbackName == "reply_done")
{
    Scene.GetGuiControl("Reply")?.SetValue(LlmText);

    string tail = DemoState.PendingSentenceBuffer.Trim();
    if (!string.IsNullOrWhiteSpace(tail))
    {
        DemoState.SpeakQueue.Enqueue(tail);
        DemoState.PendingSentenceBuffer = string.Empty;
    }

    TrySpeakNext();
}

if (IsSpeechEvent && SpeechCallbackName == "llm_sentence_done")
{
    DemoState.IsSpeaking = false;
    TrySpeakNext();
}

if (IsLlmEvent && LlmCallbackName == "reply_error")
{
    Console.Error.WriteLine(LlmError);
}
```

这个示例的关键点是：

- ASR 用 `pressed` / `released` 做 push-to-talk
- LLM 仍然走已经有的 `Scene.Llm.StartChat(...)`
- `SpeakWithCallback(...)` 保证一句说完后再说下一句，不会重叠播放

## ASR API

GamePlayer 支持从脚本调用本地麦克风和 ASR 识别器。配置在 GameEditor 的 `Project` 标签页 `ASR` 中，也会保存到 `game.project.json` 的 `asr` 节点。

当前支持：

- `sherpa`
- `whisper`

常见用法是：

- 按钮 `pressed` 时启动流式录音识别
- 一边录音一边更新识别文本
- 按钮 `released` 时停止录音并拿到最终文本
- 再把最终文本交给 `Scene.Llm` 继续做对话逻辑

项目配置重点：

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本层 ASR。 |
| `Provider` | `sherpa` 或 `whisper`。 |
| `InputDeviceIndex` | 本地 `PortAudio` 输入设备索引；为空表示默认输入设备。 |
| `PartialResultIntervalSeconds` | 流式部分结果刷新间隔。 |
| `Capture` | 麦克风打开参数：`SampleRate`、`Channels`、`FramesPerBuffer`。 |
| `Sherpa.*` | SherpaOnnx 模型与推理参数。 |
| `Whisper.*` | Whisper 模型与推理参数。 |

### C# ASR

C# API：

| API | 说明 |
| --- | --- |
| `Scene.Asr.Enabled` | 当前项目是否启用 ASR。 |
| `Scene.Asr.Provider` | 当前 provider。 |
| `Scene.Asr.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.Asr.PartialResultIntervalSeconds` | 当前部分结果刷新间隔。 |
| `Scene.Asr.IsRecording` | 当前是否正在录音识别。 |
| `StartStreamingRecognition(entity, requestId, onPartialCallback, onCompletedCallback, onErrorCallback)` | 开始后台录音，并推送部分识别结果。 |
| `StopStreamingRecognition(requestId)` | 停止指定流式识别；为空则停止当前录音。 |
| `StopAllStreamingRecognitions()` | 停止所有流式识别任务。 |

ASR 回调字段：

| 字段 | 说明 |
| --- | --- |
| `AsrRequestId` | 当前请求 Id。 |
| `AsrEventName` | `partial`、`completed`、`error`。 |
| `AsrText` | 当前识别文本。 |
| `AsrIsFinal` | 是否最终结果。 |
| `AsrError` | 错误文本。 |
| `AsrCallbackName` | 当前回调名。 |
| `AsrOffsetSeconds` | 当前识别文本对应的音频时长偏移。 |

### Python ASR

Python API：

| API | 说明 |
| --- | --- |
| `scene.asr.enabled` | 当前项目是否启用 ASR。 |
| `scene.asr.provider` | 当前 provider。 |
| `scene.asr.input_device_index` | 当前输入设备索引。 |
| `scene.asr.partial_result_interval_seconds` | 当前部分结果刷新间隔。 |
| `scene.asr.is_recording` | 当前是否正在录音识别。 |
| `scene.asr.start_streaming_recognition(request_id=None, on_partial="asr_partial", on_completed="asr_completed", on_error="asr_error")` | 开始后台录音识别。 |
| `scene.asr.stop_streaming_recognition(request_id=None)` | 停止流式识别。 |

Python 通用事件函数：

```python
def asr_event(entity, scene, input, audio, event):
    print(event["eventName"], event["text"])
```

### 完整示例：按住录音 -> 流式 ASR -> 松开发给 LLM -> 句子级顺序 Speak

假设场景中有：

- 一个按钮：`RecordButton`
- 一个文本框：`AsrInput`
- 一个标签：`Reply`

```csharp
using System.Text;

static class DemoState
{
    public static string AsrRequestId = string.Empty;
    public static string CurrentAsrText = string.Empty;
    public static string LlmAccumulatedText = string.Empty;
    public static string PendingSentenceBuffer = string.Empty;
    public static Queue<string> SpeakQueue = new();
    public static bool IsSpeaking;
}

IEnumerable<string> ExtractCompletedSentences(ref string buffer)
{
    List<string> result = [];
    int start = 0;
    for (int i = 0; i < buffer.Length; i++)
    {
        char ch = buffer[i];
        if (ch is '。' or '！' or '？' or '.' or '!' or '?')
        {
            string sentence = buffer[start..(i + 1)].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                result.Add(sentence);
            }

            start = i + 1;
        }
    }

    buffer = start >= buffer.Length ? string.Empty : buffer[start..];
    return result;
}

void TrySpeakNext()
{
    if (DemoState.IsSpeaking || DemoState.SpeakQueue.Count == 0)
    {
        return;
    }

    string sentence = DemoState.SpeakQueue.Dequeue();
    DemoState.IsSpeaking = true;
    Entity.SpeakWithCallback(sentence, "llm_sentence_done");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "pressed")
{
    DemoState.CurrentAsrText = string.Empty;
    DemoState.LlmAccumulatedText = string.Empty;
    DemoState.PendingSentenceBuffer = string.Empty;
    DemoState.SpeakQueue.Clear();
    DemoState.IsSpeaking = false;

    Scene.GetGuiControl("AsrInput")?.SetValue("");
    Scene.GetGuiControl("Reply")?.SetValue("");

    DemoState.AsrRequestId = Scene.Asr.StartStreamingRecognition(
        Entity,
        requestId: "push_to_talk",
        onPartialCallback: "asr_partial",
        onCompletedCallback: "asr_completed",
        onErrorCallback: "asr_error");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "released")
{
    Scene.Asr.StopStreamingRecognition(DemoState.AsrRequestId);
}

if (IsAsrEvent && AsrCallbackName == "asr_partial")
{
    DemoState.CurrentAsrText = AsrText;
    Scene.GetGuiControl("AsrInput")?.SetValue(AsrText);
}

if (IsAsrEvent && AsrCallbackName == "asr_completed")
{
    DemoState.CurrentAsrText = AsrText.Trim();
    Scene.GetGuiControl("AsrInput")?.SetValue(DemoState.CurrentAsrText);

    if (!string.IsNullOrWhiteSpace(DemoState.CurrentAsrText))
    {
        Scene.Llm.StartChat(
            Entity,
            DemoState.CurrentAsrText,
            systemPrompt: "你是一个中文语音助手，请简洁、自然地回答用户的问题。",
            onDeltaCallback: "reply_delta",
            onCompletedCallback: "reply_done",
            onErrorCallback: "reply_error");
    }
}

if (IsAsrEvent && AsrCallbackName == "asr_error")
{
    Console.Error.WriteLine(AsrError);
}

if (IsLlmEvent && LlmCallbackName == "reply_delta")
{
    DemoState.LlmAccumulatedText = LlmText;
    Scene.GetGuiControl("Reply")?.SetValue(DemoState.LlmAccumulatedText);

    string delta = LlmDelta;
    if (!string.IsNullOrEmpty(delta))
    {
        DemoState.PendingSentenceBuffer += delta;
        foreach (string sentence in ExtractCompletedSentences(ref DemoState.PendingSentenceBuffer))
        {
            DemoState.SpeakQueue.Enqueue(sentence);
        }

        TrySpeakNext();
    }
}

if (IsLlmEvent && LlmCallbackName == "reply_done")
{
    Scene.GetGuiControl("Reply")?.SetValue(LlmText);

    string tail = DemoState.PendingSentenceBuffer.Trim();
    if (!string.IsNullOrWhiteSpace(tail))
    {
        DemoState.SpeakQueue.Enqueue(tail);
        DemoState.PendingSentenceBuffer = string.Empty;
    }

    TrySpeakNext();
}

if (IsSpeechEvent && SpeechCallbackName == "llm_sentence_done")
{
    DemoState.IsSpeaking = false;
    TrySpeakNext();
}

if (IsLlmEvent && LlmCallbackName == "reply_error")
{
    Console.Error.WriteLine(LlmError);
}
```

这个示例的关键点是：

- ASR 用按钮 `pressed` / `released` 做 push-to-talk
- LLM 仍然走已经有的 `Scene.Llm.StartChat(...)`
- `SpeakWithCallback(...)` 保证一句说完后再说下一句

## LLM / OpenAI-compatible API

GamePlayer 支持从脚本调用 OpenAI-compatible `/v1/chat/completions` 接口，并支持流式输出。配置在 GameEditor 的 `Project` 标签页 `LLM / OpenAI-compatible` 中，也会保存到 `game.project.json` 的 `llm` 节点。

配置字段：

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本 LLM。未启用时调用会报错。 |
| `BaseUrl` | API 根地址，例如 `https://api.openai.com` 或私有供应商地址。 |
| `ApiKeyEnvironmentVariable` | API Key 环境变量名，默认 `OPENAI_API_KEY`。推荐使用它，避免把密钥写入工程文件。 |
| `ApiKey` | 直接写入工程的 API Key 覆盖值。不建议提交到版本库。 |
| `Model` | 默认模型名，例如 `gpt-4o-mini`、`qwen-plus`、私有模型名等。 |
| `ChatCompletionsPath` | 默认 `/v1/chat/completions`。如果供应商路径不同可在这里改。 |
| `TimeoutSeconds` | 请求超时时间。 |
| `DefaultTemperature` | 默认温度；为空则不发送 `temperature` 字段。 |

### C# LLM

`Scene.Llm` 提供三类调用：

- `await Scene.Llm.ChatAsync(...)`：等待完整结果，适合加载脚本、短请求或不介意等待的逻辑。
- `await foreach (var update in Scene.Llm.StreamChatAsync(...))`：当前脚本事件内同步流式读取。注意如果在 GUI 事件里直接等待，会阻塞这次脚本事件，画面要等事件结束后才继续刷新。
- `Scene.Llm.StartChat(...)`：后台请求，流式 delta 会通过 `IsLlmEvent` 回到同一个实体脚本，适合运行时 UI 和对话。

C# LLM API：

| API | 说明 |
| --- | --- |
| `Scene.Llm.Enabled` | 当前项目是否启用 LLM。 |
| `Scene.Llm.Provider` | Provider 名称。 |
| `Scene.Llm.BaseUrl` | API 根地址。 |
| `Scene.Llm.Model` | 默认模型名。 |
| `Scene.Llm.ChatCompletionsPath` | Chat Completions 路径。 |
| `Scene.Llm.DefaultTemperature` | 默认温度，可能为 `null`。 |
| `ChatAsync(text, systemPrompt, model, temperature)` | 非流式返回完整文本。 |
| `StreamChatAsync(text, systemPrompt, model, temperature)` | 按文本 prompt 发起流式请求。 |
| `StreamChatAsync(messages, model, temperature)` | 按消息列表发起流式请求。 |
| `StartChat(entity, text, systemPrompt, model, temperature, requestId, onDeltaCallback, onCompletedCallback, onErrorCallback)` | 后台流式请求，通过脚本事件回调。 |

C# 阻塞式完整结果：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    string answer = await Scene.Llm.ChatAsync(
        "用一句话介绍这片海。",
        systemPrompt: "你是游戏中的旁白，回答要短。");

    Scene.GetGuiControl("LLM Output")?.SetValue(answer);
}
```

C# 事件内同步流式读取：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    RuntimeGuiControl? output = Scene.GetGuiControl("LLM Output");
    output?.SetValue("");

    await foreach (RuntimeLlmStreamUpdate update in Scene.Llm.StreamChatAsync(
        "写一句欢迎玩家进入海边场景的台词。",
        systemPrompt: "你是游戏 NPC。",
        temperature: 0.7f))
    {
        output?.SetValue(update.AccumulatedText);
    }
}
```

C# 后台流式输出，不阻塞渲染：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    Scene.GetGuiControl("LLM Output")?.SetValue("");

    Scene.Llm.StartChat(
        Entity,
        "写一句欢迎玩家进入海边场景的台词。",
        systemPrompt: "你是游戏 NPC。",
        onDeltaCallback: "npc_reply_delta",
        onCompletedCallback: "npc_reply_done",
        onErrorCallback: "npc_reply_error");
}

if (IsLlmEvent && LlmCallbackName == "npc_reply_delta")
{
    Scene.GetGuiControl("LLM Output")?.SetValue(LlmText);
}

if (IsLlmEvent && LlmCallbackName == "npc_reply_done")
{
    Entity.Speak(LlmText);
}

if (IsLlmEvent && LlmCallbackName == "npc_reply_error")
{
    Console.Error.WriteLine(LlmError);
}
```

C# 消息列表调用：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    var messages = new[]
    {
        new RuntimeLlmChatMessage("system", "你是游戏任务设计助手。"),
        new RuntimeLlmChatMessage("user", "给玩家一个 20 字以内的探索任务。")
    };

    await foreach (RuntimeLlmStreamUpdate update in Scene.Llm.StreamChatAsync(messages))
    {
        Scene.GetGuiControl("Quest Text")?.SetValue(update.AccumulatedText);
    }
}
```

### Python LLM

`scene.llm` 提供三类调用：

- `scene.llm.chat(...)`：等待完整结果。
- `scene.llm.stream_chat(...)` / `stream_messages(...)`：在当前函数里同步流式迭代。适合加载脚本或测试；运行中 UI 建议用 `start_chat`，否则当前脚本事件会占用主循环。
- `scene.llm.start_chat(...)`：后台请求，delta / completed / error 会回调到指定 Python 函数，不阻塞当前事件。

Python LLM API：

| API | 说明 |
| --- | --- |
| `scene.llm.enabled` | 当前项目是否启用 LLM。 |
| `scene.llm.model` | 默认模型名。 |
| `scene.llm.chat(text, system_prompt=None, model=None, temperature=None)` | 非流式返回完整文本。 |
| `scene.llm.stream_chat(text, system_prompt=None, model=None, temperature=None)` | 按文本 prompt 发起流式请求。 |
| `scene.llm.stream_messages(messages, model=None, temperature=None)` | 按消息列表发起流式请求。 |
| `scene.llm.start_chat(text, system_prompt=None, model=None, temperature=None, request_id=None, on_delta="llm_delta", on_completed="llm_completed", on_error="llm_error")` | 后台流式请求，通过 Python 函数回调。 |

Python 完整结果：

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    answer = scene.llm.chat(
        "用一句话介绍这片海。",
        system_prompt="你是游戏中的旁白，回答要短。")

    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value(answer)
```

Python 当前函数内同步流式读取：

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value("")
        scene.flush()

    for update in scene.llm.stream_chat(
        "写一句欢迎玩家进入海边场景的台词。",
        system_prompt="你是游戏 NPC。",
        temperature=0.7):
        if output:
            output.set_value(update["accumulated_text"])
            scene.flush()
```

Python 后台流式输出，不阻塞渲染：

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value("")

    scene.llm.start_chat(
        "写一句欢迎玩家进入海边场景的台词。",
        system_prompt="你是游戏 NPC。",
        on_delta="npc_reply_delta",
        on_completed="npc_reply_done",
        on_error="npc_reply_error")

def npc_reply_delta(entity, scene, input, audio, event):
    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value(event["accumulatedText"])

def npc_reply_done(entity, scene, input, audio, event):
    entity.speak(event["accumulatedText"])

def npc_reply_error(entity, scene, input, audio, event):
    print("LLM error:", event["error"])
```

Python 消息列表调用：

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    messages = [
        {"role": "system", "content": "你是游戏任务设计助手。"},
        {"role": "user", "content": "给玩家一个 20 字以内的探索任务。"},
    ]

    output = scene.get_gui_control("Quest Text")
    for update in scene.llm.stream_messages(messages):
        if output:
            output.set_value(update["accumulated_text"])
            scene.flush()
```

注意事项：

- LLM 请求是网络请求。同步 `ChatAsync` / `chat` / 当前函数内 `stream_chat` 会占用脚本事件执行时间；运行中实时 UI 建议用 `StartChat` / `start_chat`。
- Python 的 `scene.flush()` 只会提交当前已累计的引擎命令，例如 GUI 文本变化、实体移动等；不会重新读取新的输入快照，也不会让被阻塞的主循环提前渲染新帧。
- `start_chat` 的回调事件字段包括 `requestId`、`eventName`、`delta`、`accumulatedText`、`isFinal`、`error`、`callbackName`。
- 当前 LLM API 面向文本 Chat Completions；图片、多模态、工具调用等还没有封装到脚本层。

## Realtime Voice API

GamePlayer 支持从脚本调用 `Zhengyan.DigitalWife.Samples.RealtimeVoice` 服务。配置在 GameEditor 的 `Project` 标签页 `Realtime Voice` 中，也会保存到 `game.project.json` 的 `realtimeVoice` 节点。

这组 API 面向远端语音服务，主要能力是：

- 本地麦克风录音并上传给服务转写
- 按配置的唤醒词组监听唤醒词，并通过脚本事件通知
- 发送用户文本到 Realtime 会话，流式播放返回音频，并把 transcript delta / completed 回调给脚本
- 调用 `/v1/audio/speech` 做固定文本提示语

项目配置重点：

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本层 Realtime Voice。 |
| `BaseUrl` | 服务根地址，例如 `http://127.0.0.1:5000`。 |
| `RealtimePath` | Realtime WebSocket 路径，默认 `/v1/realtime`。 |
| `AudioSpeechPath` | 文本直出 TTS 路径，默认 `/v1/audio/speech`。 |
| `ApiKeyEnvironmentVariable` / `ApiKey` | API Key 优先读取直接配置，其次读环境变量。 |
| `Model` | 会话默认模型名。 |
| `Voice` | 远端 voice 字段。 |
| `InputAudioSampleRate` | 发给 Realtime 协议层的输入采样率。 |
| `OutputAudioSampleRate` | 期望远端返回的输出采样率。 |
| `InputDeviceIndex` | 本地 `PortAudio` 输入设备索引；为空时使用默认输入设备。 |
| `OutputVolume` | 回放远端语音时的输出音量倍数。 |
| `PromptSpeed` | `/v1/audio/speech` 固定提示语请求的默认速度。 |
| `UserCapture` | `start_transcription` / `StartVoiceTurn` 使用的本地录音参数。 |
| `WakeWord.Enabled` / `WakeWord.Keywords` | 是否启用脚本唤醒词监听以及唤醒词组。 |
| `WakeWord.Capture` | 唤醒词分片录音参数。 |

### C# Realtime Voice

`Scene.RealtimeVoice` 采用后台任务 + 脚本事件回调的模式。不要在 `Update` 里每帧调用 `Start*`；通常在 `IsStart`、GUI 点击、或收到上一轮回调后再触发下一步。

C# API：

| API | 说明 |
| --- | --- |
| `Scene.RealtimeVoice.Enabled` | 当前项目是否启用 Realtime Voice。 |
| `Scene.RealtimeVoice.BaseUrl` | 服务根地址。 |
| `Scene.RealtimeVoice.Model` | 默认模型名。 |
| `Scene.RealtimeVoice.Voice` | 默认 voice。 |
| `Scene.RealtimeVoice.WakeWordEnabled` | 是否启用了唤醒词监听配置。 |
| `Scene.RealtimeVoice.WakeWords` | 当前配置的唤醒词列表。 |
| `Scene.RealtimeVoice.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.RealtimeVoice.IsWakeWordMonitoring` | 当前是否在监听唤醒词。 |
| `StartWakeWordMonitoring(entity, onDetectedCallback, onErrorCallback)` | 开始后台唤醒词监听。 |
| `StopWakeWordMonitoring()` | 停止唤醒词监听。 |
| `StartTranscription(entity, requestId, timeoutSeconds, onCompletedCallback, onTimeoutCallback, onErrorCallback)` | 录音直到静音并转写；可配置超时。 |
| `StartResponse(entity, userText, requestId, onDeltaCallback, onCompletedCallback, onErrorCallback)` | 发送用户文本到 Realtime 会话，流式播放远端音频。 |
| `StartVoiceTurn(entity, requestId, timeoutSeconds, onTranscriptionCompletedCallback, onDeltaCallback, onCompletedCallback, onTimeoutCallback, onErrorCallback)` | 录音、转写、发起回复的一体化后台流程；可配置等待用户输入的超时。 |
| `StartSpeakText(entity, text, speed, requestId, onCompletedCallback, onErrorCallback)` | 调用 `/v1/audio/speech` 播放固定提示语。 |
| `ResetConversationAsync()` | 清空远端会话中的历史消息。 |
| `CancelRequest(requestId)` | 取消指定后台语音请求。 |
| `CancelAllRequests()` | 取消当前实体触发的全部后台语音请求。 |

典型流程：开始唤醒词监听，命中后发起一轮语音对话。

```csharp
if (IsStart && Scene.RealtimeVoice.Enabled)
{
    Scene.RealtimeVoice.StartWakeWordMonitoring(
        Entity,
        onDetectedCallback: "wake_word_hit",
        onErrorCallback: "wake_word_error");
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "wake_word_hit")
{
    Scene.GetGuiControl("Status")?.SetValue($"已唤醒: {RealtimeVoiceWakeWord}");
    Scene.RealtimeVoice.StartVoiceTurn(
        Entity,
        onTranscriptionCompletedCallback: "voice_transcribed",
        onDeltaCallback: "voice_delta",
        onCompletedCallback: "voice_done",
        onErrorCallback: "voice_error");
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_transcribed")
{
    Scene.GetGuiControl("Heard")?.SetValue(RealtimeVoiceText);
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_delta")
{
    Scene.GetGuiControl("Reply")?.SetValue(RealtimeVoiceAccumulatedText);
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_done")
{
    Scene.GetGuiControl("Reply")?.SetValue(RealtimeVoiceText);
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_error")
{
    Console.Error.WriteLine(RealtimeVoiceError);
}
```

完整的“唤醒词 -> 远端转写/对话 -> TTS 播放 -> 30s 内继续对话，超时后回到待机”脚本示例：

说明：

- 现在脚本层已经支持 `timeout` 和 `cancel`，可以完整实现接近 `DigitalHuman` 的状态机。
- 下面的写法是：先靠唤醒词进入会话；用户说完后，数字人回复；回复完成后继续等待 30 秒下一句；如果 30 秒内没有新的语音输入，则清空会话并回到“等待唤醒”。
- 假设项目里已经有两个动作文件：`assets/motions/basic_stand.vmd` 和 `assets/motions/basic_wait.vmd`。
- 这里用 `basic_stand.vmd` 和 `basic_wait.vmd` 同时放进动作层列表，通过层权重平滑过渡，效果更接近 `DigitalHuman` 项目的动作混合方式。

```csharp
static class VoiceState
{
    public static bool InConversation;
    public static bool WakeWordMonitorStarted;
    public static bool WaitingForUserSpeech;
    public static string PendingTurnRequestId = string.Empty;
    public static DateTimeOffset WaitTurnExpiresAtUtc = DateTimeOffset.MinValue;
    public static bool MotionBlendReady;
    public static float WaitLayerWeight;
    public static float TargetWaitLayerWeight;
}

const string StandMotion = "assets/motions/basic_stand.vmd";
const string WaitMotion = "assets/motions/basic_wait.vmd";
const float MotionBlendDurationSeconds = 0.35f;
const double WaitForUserSpeechTimeoutSeconds = 30.0;

void EnsureMotionBlendSetup()
{
    if (VoiceState.MotionBlendReady)
    {
        return;
    }

    Entity.SetMotionLayers(new[]
    {
        new MotionLayerDefinition(StandMotion, 1.0f, false),
        new MotionLayerDefinition(WaitMotion, 0.0f, false)
    });
    Entity.LoopMotion = true;
    Entity.PlayMotion();
    VoiceState.MotionBlendReady = true;
    VoiceState.WaitLayerWeight = 0.0f;
    VoiceState.TargetWaitLayerWeight = 0.0f;
}

void SetStandState()
{
    EnsureMotionBlendSetup();
    VoiceState.TargetWaitLayerWeight = 0.0f;
}

void SetWaitState()
{
    EnsureMotionBlendSetup();
    VoiceState.TargetWaitLayerWeight = 1.0f;
}

void UpdateMotionBlend()
{
    if (!IsUpdate || !VoiceState.MotionBlendReady)
    {
        return;
    }

    float current = VoiceState.WaitLayerWeight;
    float target = VoiceState.TargetWaitLayerWeight;
    if (MathF.Abs(current - target) < 0.001f)
    {
        return;
    }

    float step = (float)(DeltaSeconds / MotionBlendDurationSeconds);
    float next = target > current
        ? MathF.Min(target, current + step)
        : MathF.Max(target, current - step);

    VoiceState.WaitLayerWeight = next;
    Entity.SetMotionLayerWeight(StandMotion, 1.0f - next);
    Entity.SetMotionLayerWeight(WaitMotion, next);
}

void BeginWaitingForUserSpeech()
{
    SetWaitState();
    VoiceState.WaitingForUserSpeech = true;
    VoiceState.WaitTurnExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(WaitForUserSpeechTimeoutSeconds);
    Scene.GetGuiControl("Status")?.SetValue($"等待下一句（{WaitForUserSpeechTimeoutSeconds:0}s）");
    VoiceState.PendingTurnRequestId = Scene.RealtimeVoice.StartVoiceTurn(
        Entity,
        timeoutSeconds: (float)WaitForUserSpeechTimeoutSeconds,
        onTranscriptionCompletedCallback: "voice_transcribed",
        onDeltaCallback: "voice_delta",
        onCompletedCallback: "voice_done",
        onTimeoutCallback: "voice_timeout",
        onErrorCallback: "voice_error");
}

void EndConversationAndReturnToStand(string statusText)
{
    SetStandState();
    VoiceState.WaitingForUserSpeech = false;
    VoiceState.WaitTurnExpiresAtUtc = DateTimeOffset.MinValue;

    if (!string.IsNullOrWhiteSpace(VoiceState.PendingTurnRequestId))
    {
        Scene.RealtimeVoice.CancelRequest(VoiceState.PendingTurnRequestId);
        VoiceState.PendingTurnRequestId = string.Empty;
    }

    VoiceState.InConversation = false;
    _ = Scene.RealtimeVoice.ResetConversationAsync();
    Scene.GetGuiControl("Status")?.SetValue(statusText);
    EnsureWakeWordMonitor();
}

void EnsureWakeWordMonitor()
{
    EnsureMotionBlendSetup();
    if (VoiceState.WakeWordMonitorStarted || !Scene.RealtimeVoice.Enabled)
    {
        return;
    }

    Scene.RealtimeVoice.StartWakeWordMonitoring(
        Entity,
        onDetectedCallback: "wake_word_hit",
        onErrorCallback: "voice_error");
    VoiceState.WakeWordMonitorStarted = true;
    SetStandState();
    Scene.GetGuiControl("Status")?.SetValue("等待唤醒");
}

if (IsStart)
{
    EnsureWakeWordMonitor();
}

if (IsUpdate)
{
    UpdateMotionBlend();

    if (VoiceState.WaitingForUserSpeech
        && VoiceState.WaitTurnExpiresAtUtc != DateTimeOffset.MinValue
        && DateTimeOffset.UtcNow >= VoiceState.WaitTurnExpiresAtUtc)
    {
        EndConversationAndReturnToStand("超时，重新等待唤醒");
    }
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "wake_word_hit")
{
    if (VoiceState.InConversation)
    {
        return;
    }

    VoiceState.InConversation = true;
    Scene.RealtimeVoice.StopWakeWordMonitoring();
    VoiceState.WakeWordMonitorStarted = false;
    SetWaitState();
    Scene.GetGuiControl("Status")?.SetValue($"已唤醒: {RealtimeVoiceWakeWord}");

    if (!string.IsNullOrWhiteSpace(RealtimeVoiceText))
    {
        // 用户在同一句里已经说了“唤醒词 + 问题”，直接发给远端会话。
        Scene.RealtimeVoice.StartResponse(
            Entity,
            RealtimeVoiceText,
            onDeltaCallback: "voice_delta",
            onCompletedCallback: "voice_done",
            onErrorCallback: "voice_error");
    }
    else
    {
        // 用户只说了唤醒词，先播提示语，播完后再录下一句。
        Scene.RealtimeVoice.StartSpeakText(
            Entity,
            "我在，请说。",
            speed: 1.0f,
            onCompletedCallback: "prompt_done",
            onErrorCallback: "voice_error");
    }
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "prompt_done")
{
    Scene.GetGuiControl("Status")?.SetValue("正在听");
    BeginWaitingForUserSpeech();
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_transcribed")
{
    VoiceState.WaitingForUserSpeech = false;
    VoiceState.WaitTurnExpiresAtUtc = DateTimeOffset.MinValue;
    SetWaitState();
    Scene.GetGuiControl("Heard")?.SetValue(RealtimeVoiceText);
    Scene.GetGuiControl("Status")?.SetValue("思考中");
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_delta")
{
    SetWaitState();
    Scene.GetGuiControl("Reply")?.SetValue(RealtimeVoiceAccumulatedText);
    Scene.GetGuiControl("Status")?.SetValue("正在说话");
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_done")
{
    SetWaitState();
    VoiceState.PendingTurnRequestId = string.Empty;
    Scene.GetGuiControl("Reply")?.SetValue(RealtimeVoiceText);
    BeginWaitingForUserSpeech();
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_timeout")
{
    EndConversationAndReturnToStand("超时，重新等待唤醒");
}

if (IsRealtimeVoiceEvent && RealtimeVoiceCallbackName == "voice_error")
{
    Console.Error.WriteLine(RealtimeVoiceError);
    EndConversationAndReturnToStand("语音出错，重新等待唤醒");
}
```

固定提示语示例：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    Scene.RealtimeVoice.StartSpeakText(
        Entity,
        "我在，请说。",
        speed: 1.0f,
        onCompletedCallback: "prompt_done",
        onErrorCallback: "prompt_error");
}
```

Realtime Voice 回调事件字段：

| 字段 | 说明 |
| --- | --- |
| `RealtimeVoiceRequestId` | 请求 Id。 |
| `RealtimeVoiceEventName` | 事件名：`wake_word_detected`、`transcription_completed`、`delta`、`completed`、`speech_completed`、`timeout`、`error`。 |
| `RealtimeVoiceText` | 主文本：唤醒词事件通常是去掉唤醒词后的尾文本，转写事件是用户文本，完成事件是最终回复文本。 |
| `RealtimeVoiceDelta` | 当前 transcript 增量。 |
| `RealtimeVoiceAccumulatedText` | 当前累计回复文本。 |
| `RealtimeVoiceIsFinal` | 是否最终事件。 |
| `RealtimeVoiceError` | 错误文本。 |
| `RealtimeVoiceCallbackName` | 当前命中的脚本回调名。 |
| `RealtimeVoiceWakeWord` | 命中的唤醒词。 |
| `RealtimeVoiceRecognizedText` | 原始识别文本。 |

### Python Realtime Voice

`scene.realtime_voice` 提供与 C# 同名的后台能力，但 Python 会自动把回调目标绑定到当前脚本实体。

Python API：

| API | 说明 |
| --- | --- |
| `scene.realtime_voice.enabled` | 当前项目是否启用 Realtime Voice。 |
| `scene.realtime_voice.base_url` | 服务根地址。 |
| `scene.realtime_voice.model` | 默认模型名。 |
| `scene.realtime_voice.voice` | 默认 voice。 |
| `scene.realtime_voice.wake_word_enabled` | 是否启用了唤醒词监听配置。 |
| `scene.realtime_voice.wake_words` | 当前配置的唤醒词列表。 |
| `scene.realtime_voice.input_device_index` | 当前输入设备索引。 |
| `scene.realtime_voice.start_wake_word_monitoring(on_detected="wake_word_detected", on_error="wake_word_error")` | 开始后台唤醒词监听。 |
| `scene.realtime_voice.stop_wake_word_monitoring()` | 停止唤醒词监听。 |
| `scene.realtime_voice.start_transcription(request_id=None, timeout_seconds=None, on_completed="realtime_voice_transcription_completed", on_timeout="realtime_voice_timeout", on_error="realtime_voice_error")` | 录音直到静音并转写；可配置超时。 |
| `scene.realtime_voice.start_response(user_text, request_id=None, on_delta="realtime_voice_delta", on_completed="realtime_voice_completed", on_error="realtime_voice_error")` | 发送用户文本到 Realtime 会话并流式播放回复。 |
| `scene.realtime_voice.start_voice_turn(request_id=None, timeout_seconds=30, on_transcription_completed="realtime_voice_transcription_completed", on_delta="realtime_voice_delta", on_completed="realtime_voice_completed", on_timeout="realtime_voice_timeout", on_error="realtime_voice_error")` | 一体化后台语音对话流程；可配置等待用户输入的超时。 |
| `scene.realtime_voice.start_speak_text(text, speed=None, request_id=None, on_completed="realtime_voice_speech_completed", on_error="realtime_voice_error")` | 文本直出 TTS。 |
| `scene.realtime_voice.reset_conversation()` | 重置远端会话。 |
| `scene.realtime_voice.cancel_request(request_id)` | 取消指定后台语音请求。 |
| `scene.realtime_voice.cancel_all_requests()` | 取消当前脚本实体触发的全部后台语音请求。 |

Python 典型流程：

```python
state = {
    "in_conversation": False,
    "waiting_for_user_speech": False,
    "pending_turn_request_id": "",
    "wake_word_monitor_started": False,
    "wait_turn_expires_at": None,
    "motion_blend_ready": False,
    "wait_weight": 0.0,
    "target_wait_weight": 0.0
}

STAND_MOTION = "assets/motions/basic_stand.vmd"
WAIT_MOTION = "assets/motions/basic_wait.vmd"
BLEND_DURATION_SECONDS = 0.35
WAIT_FOR_USER_SPEECH_TIMEOUT_SECONDS = 30.0

def ensure_motion_blend_setup(entity):
    if state["motion_blend_ready"]:
        return

    entity.set_motion_layers([
        {"path": STAND_MOTION, "weight": 1.0, "resetPhysicsOnLoop": False},
        {"path": WAIT_MOTION, "weight": 0.0, "resetPhysicsOnLoop": False},
    ])
    entity.set_loop_motion(True)
    entity.play_motion()
    state["motion_blend_ready"] = True
    state["wait_weight"] = 0.0
    state["target_wait_weight"] = 0.0

def set_stand_state(entity):
    ensure_motion_blend_setup(entity)
    state["target_wait_weight"] = 0.0

def set_wait_state(entity):
    ensure_motion_blend_setup(entity)
    state["target_wait_weight"] = 1.0

def update_motion_blend(entity, delta_seconds):
    if not state["motion_blend_ready"]:
        return

    current = state["wait_weight"]
    target = state["target_wait_weight"]
    if abs(current - target) < 0.001:
        return

    step = delta_seconds / BLEND_DURATION_SECONDS
    if target > current:
        current = min(target, current + step)
    else:
        current = max(target, current - step)

    state["wait_weight"] = current
    entity.set_motion_layer_weight(STAND_MOTION, 1.0 - current)
    entity.set_motion_layer_weight(WAIT_MOTION, current)

def begin_waiting_for_user_speech(entity, scene):
    set_wait_state(entity)
    state["waiting_for_user_speech"] = True
    state["wait_turn_expires_at"] = time.time() + WAIT_FOR_USER_SPEECH_TIMEOUT_SECONDS
    status = scene.get_gui_control("Status")
    if status:
        status.set_value(f"等待下一句（{WAIT_FOR_USER_SPEECH_TIMEOUT_SECONDS:.0f}s）")
    state["pending_turn_request_id"] = scene.realtime_voice.start_voice_turn(
        timeout_seconds=WAIT_FOR_USER_SPEECH_TIMEOUT_SECONDS,
        on_transcription_completed="voice_transcribed",
        on_delta="voice_delta",
        on_completed="voice_done",
        on_timeout="voice_timeout",
        on_error="voice_error")

def end_conversation_and_return_to_stand(entity, scene, status_text):
    set_stand_state(entity)
    state["waiting_for_user_speech"] = False
    state["wait_turn_expires_at"] = None

    if state["pending_turn_request_id"]:
        scene.realtime_voice.cancel_request(state["pending_turn_request_id"])
        state["pending_turn_request_id"] = ""

    state["in_conversation"] = False
    scene.realtime_voice.reset_conversation()
    status = scene.get_gui_control("Status")
    if status:
        status.set_value(status_text)
    state["wake_word_monitor_started"] = False
    start(entity, scene, input=None, audio=None)

def start(entity, scene, input, audio):
    if scene.realtime_voice.enabled and not state["wake_word_monitor_started"]:
        set_stand_state(entity)
        scene.realtime_voice.start_wake_word_monitoring(
            on_detected="wake_word_hit",
            on_error="voice_error")
        state["wake_word_monitor_started"] = True

def update(entity, scene, input, audio, delta_seconds):
    update_motion_blend(entity, delta_seconds)
    if state["waiting_for_user_speech"] and state["wait_turn_expires_at"] is not None and time.time() >= state["wait_turn_expires_at"]:
        end_conversation_and_return_to_stand(entity, scene, "超时，重新等待唤醒")

def wake_word_hit(entity, scene, input, audio, event):
    if state["in_conversation"]:
        return

    state["in_conversation"] = True
    scene.realtime_voice.stop_wake_word_monitoring()
    state["wake_word_monitor_started"] = False
    set_wait_state(entity)

    status = scene.get_gui_control("Status")
    if status:
        status.set_value("已唤醒: " + event["wakeWord"])

    if event["text"].strip():
        scene.realtime_voice.start_response(
            event["text"],
            on_delta="voice_delta",
            on_completed="voice_done",
            on_error="voice_error")
    else:
        scene.realtime_voice.start_speak_text(
            "我在，请说。",
            on_completed="prompt_done",
            on_error="voice_error")

def prompt_done(entity, scene, input, audio, event):
    status = scene.get_gui_control("Status")
    if status:
        status.set_value("正在听")
    begin_waiting_for_user_speech(entity, scene)

def voice_transcribed(entity, scene, input, audio, event):
    state["waiting_for_user_speech"] = False
    state["wait_turn_expires_at"] = None
    set_wait_state(entity)
    heard = scene.get_gui_control("Heard")
    if heard:
        heard.set_value(event["text"])

def voice_delta(entity, scene, input, audio, event):
    set_wait_state(entity)
    reply = scene.get_gui_control("Reply")
    if reply:
        reply.set_value(event["accumulatedText"])

def voice_done(entity, scene, input, audio, event):
    set_wait_state(entity)
    state["pending_turn_request_id"] = ""
    reply = scene.get_gui_control("Reply")
    if reply:
        reply.set_value(event["text"])
    begin_waiting_for_user_speech(entity, scene)

def voice_timeout(entity, scene, input, audio, event):
    end_conversation_and_return_to_stand(entity, scene, "超时，重新等待唤醒")

def voice_error(entity, scene, input, audio, event):
    end_conversation_and_return_to_stand(entity, scene, "语音出错，重新等待唤醒")
    print("Realtime Voice error:", event["error"])
```

Python 通用回调事件字典字段包括：

- `requestId`
- `eventName`
- `text`
- `delta`
- `accumulatedText`
- `isFinal`
- `error`
- `callbackName`
- `wakeWord`
- `recognizedText`

注意事项：

- `RealtimeVoice` 的后台方法是非阻塞的，但如果你在 `Update` 每帧都调用 `start_*`，会不断创建新任务。通常要用脚本状态位控制，或只在 GUI 点击、唤醒词命中、上一轮完成回调后再触发下一轮。
- 对“30s 内等待下一句，超时后回到站立”这类逻辑，推荐像上面的示例一样，由脚本自己维护 `WaitTurnExpiresAtUtc` / `wait_turn_expires_at` 并主动 `cancel_request(...)`，这样比单纯依赖后台请求回调更稳定。
- 麦克风输入当前走本地 `PortAudio`。Linux 下如果打不开录音设备，优先检查 `Realtime Voice -> Input device index`、`User Capture` 和 `Wake Word Capture` 的采样率。
- `start_response` / `start_voice_turn` 会自动播放远端返回音频；脚本无需再手动把音频流接到 `Audio.Play(...)`。
- `ResetConversationAsync()` / `reset_conversation()` 会清空远端会话历史，适合切场景或开始新的对话主题时调用。
- `timeout` 事件只表示“在指定等待时间内没有等到一轮可提交的用户语音”。它不是错误，也不会自动清空远端会话，是否重置会话由脚本自行决定。

## 常见脚本组合示例

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

## 当前边界与注意事项

- Python 对象是事件快照，修改命令在函数返回后由 GamePlayer 执行。
- C# 脚本方法调用直接作用于运行时对象；但每次事件都会重新执行 `.csx` 文件，需要跨帧状态时建议使用 `static` 类型保存。
- GUI 控件使用 `absolute` 时坐标和大小是窗口像素；使用 `relative` 时会以项目窗口基准分辨率缩放。Sprite 坐标和鼠标坐标仍是窗口像素，左上角为 `(0, 0)`。
- 3D 世界坐标使用 `System.Numerics.Vector3`，通常 Y 轴向上。
- 音频脚本只控制已注册 Audio 资源的播放状态，不负责动态加载文件。
- 轻量 Collider 用于拾取、触发和简单碰撞判断，不是完整物理模拟。
- PMX 动作、PMX 材质贴图覆盖、TTS 口型、PMX 绑定关系只对 `pmx_model` 有效。
- 如果脚本绑定到 GUI 控件的目标实体为空，GUI 事件不会有实体脚本接收；建议在 GameEditor 里为控件设置 `Target entity`。
