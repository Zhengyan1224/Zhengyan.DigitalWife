# GameEditor / GamePlayer 脚本 API

本文档说明 `GamePlayer` 暴露给 C# `.csx` 和 Python `.py` 脚本的运行时 API。编辑器中绑定脚本后，保存项目时会把外部脚本复制到游戏工程目录下，并做一次轻量语法检查。

脚本层定位：

- 脚本负责游戏逻辑、输入响应、对象移动、GUI 事件、场景切换、音频播放、TTS 说话、相机控制、存档读写等。
- 脚本不直接管理 OpenGL 资源、PMX 内部骨骼求解、音频设备、窗口消息循环或底层物理引擎。
- 当前碰撞是轻量级运行时 Collider，不是完整刚体物理系统。它适合射线拾取、触发区域和简单碰撞判断。
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
- 默认导入：`System`、`System.Numerics`、`Zhengyan.DigitalWife.Samples.GamePlayer`。
- 默认可访问全局对象：`Entity`、`Scene`、`Input`、`Audio`。

Python 脚本：

- 文件扩展名：`.py`。
- 运行环境：系统 `python` 或 `python3` 进程。
- Python 脚本通过桥接命令修改 GamePlayer 状态。
- Python 脚本中的对象属性大多是事件开始时的快照；例如调用 `entity.set_position(...)` 后，当前函数内的 `entity.position` 不会立刻更新，要到下一次事件快照才会反映。

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
    Console.WriteLine($"{GuiControlId} -> {GuiEventName}");
}

if (IsLoadingEvent)
{
    Console.WriteLine($"{LoadingEventName}: {LoadingProgress:P0} {LoadingMessage}");
}

if (IsSpeechEvent)
{
    Console.WriteLine($"speech callback: {SpeechCallbackName}");
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
| `GuiEventName` | `string` | GUI 事件名，例如 `clicked`、`changed`。 |
| `IsLoadingEvent` | `bool` | 场景加载入口脚本事件。 |
| `LoadingEventName` | `string` | `loading_started`、`loading_progress`、`loading_completed`。 |
| `LoadingProgress` | `float` | 加载进度，范围 `0.0` 到 `1.0`。 |
| `LoadingMessage` | `string` | 当前加载步骤文本。 |
| `IsSpeechEvent` | `bool` | TTS 播放完成回调事件。 |
| `SpeechCallbackName` | `string` | `SpeakWithCallback` 传入的回调名。 |

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

### Python 生命周期

Python 通过函数名派发事件：

```python
def start(entity, scene, input, audio):
    print("start", entity.name)

def update(entity, scene, input, audio, delta_seconds):
    entity.rotate_y(30.0 * delta_seconds)

def gui_event(entity, scene, input, audio, control_id, event_name):
    print(control_id, event_name)

def loading_started(entity, scene, input, audio, progress, message):
    print("loading started", scene.name)

def loading_progress(entity, scene, input, audio, progress, message):
    print(progress, message)

def loading_completed(entity, scene, input, audio, progress, message):
    print("loading completed")

def speech_completed(entity, scene, input, audio, callback_name):
    print("speech completed", callback_name)
```

`entity.speak(..., on_completed="after_speak")` 会优先调用同名函数：

```python
def start(entity, scene, input, audio):
    entity.speak("你好", on_completed="after_speak")

def after_speak(entity, scene, input, audio):
    entity.rotate_y(180)
```

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
```

C# 额外可读/可写属性：

| 属性 | 说明 |
| --- | --- |
| `IsPmxModel` | 当前实体是否有 PMX 运行时对象。 |
| `LoopMotion` | PMX 动作是否循环。 |
| `ResetPhysicsOnMotionLoop` | PMX 动作循环时是否重置物理。 |
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
Entity.AddMotionLayer("assets/motions/wave.vmd", weight: 0.5f);
Entity.SetMotionLayerWeight("assets/motions/wave.vmd", 1.0f);
Entity.SetMotionLayerResetPhysicsOnLoop("assets/motions/wave.vmd", true);
Entity.RemoveMotionLayer("assets/motions/wave.vmd");
Entity.ClearMotion();
```

```python
entity.apply_motion("assets/motions/idle.vmd")
entity.add_motion_layer("assets/motions/wave.vmd", weight=0.5)
entity.set_motion_layer_weight("assets/motions/wave.vmd", 1.0)
entity.set_motion_layer_reset_physics_on_loop("assets/motions/wave.vmd", True)
entity.remove_motion_layer("assets/motions/wave.vmd")
entity.clear_motion()
```

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

播放背景音乐：

```csharp
if (IsStart)
{
    Audio.SetVolume("BGM", 0.7f);
    Audio.Play("BGM");
}
```

```python
def start(entity, scene, input, audio):
    audio.set_volume("BGM", 0.7)
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
def gui_event(entity, scene, input, audio, control_id, event_name):
    if event_name == "clicked":
        audio.play("ClickSfx")
```

当前音频脚本边界：

- 脚本不能动态加载未在项目里登记的音频源。
- 脚本不能修改 `Loop` 和 `PlayOnStart`；这些在编辑器里配置。
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
```

属性和方法：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Name` | `scene.name` | 当前场景名。 |
| `Scene.Entities` | `scene.get_entity(...)` | C# 可枚举所有实体；Python 用查找函数获取实体。 |
| `Scene.GuiControls` | `scene.get_gui_control(...)` | C# 可枚举 GUI；Python 用查找函数。 |
| `Scene.Sprites` | `scene.get_sprite(...)` | C# 可枚举 2D 精灵；Python 用查找函数。 |
| `Scene.Window` | `scene.window` | 窗口控制。 |
| `Scene.Camera` | `scene.camera` | 相机控制。 |
| `Scene.Debug` | `scene.debug` | 调试绘制。 |
| `Scene.Save` | `scene.save` | 存档读写。 |
| `Scene.RenderTexture(name)` | `scene.render_texture(name)` | 返回 `rt:name` 引用。 |
| `Scene.LoadScene(path)` | `scene.load_scene(path)` | 切换场景。 |

场景切换会显示同一套加载遮罩，并触发目标场景的加载入口脚本。

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

加载界面背景色、背景图和背景图透明度在 GameEditor 的场景属性里配置。

## GUI API

GUI 控件类型：

| 类型 | 默认事件 | 说明 |
| --- | --- | --- |
| `button` | `clicked` | 按钮。 |
| `label` | 无 | 文本标签。支持自动换行。 |
| `checkbox` | `changed` | 复选框。 |
| `dropdown` | `changed` | 下拉框。 |

GUI 控件属性：

| C# 属性 | Python 快照 | 说明 |
| --- | --- | --- |
| `Id` | `id` | 控件 Id。 |
| `Name` | `name` | 控件名称。 |
| `Type` | `type` | `button`、`label`、`checkbox`、`dropdown`。 |
| `Text` | `text` | 显示文本。 |
| `Visible` | `visible` | 是否显示。 |
| `X` / `Y` | `x` / `y` | 屏幕像素坐标。 |
| `Width` / `Height` | `width` / `height` | 控件尺寸。 |
| `TargetEntity` | 无 | 事件目标实体，在编辑器里通常用下拉选择。 |
| `EventName` | 无 | 事件名。 |
| `Checked` | `checked` | 复选框状态。 |
| `WordWrap` | `word_wrap` | 文本自动换行。 |
| `Items` | `items` | 下拉框项目。 |
| `SelectedIndex` | `selected_index` | 下拉框选中项下标。 |
| `SelectedItem` | 无 | C# 可直接取当前选中项。 |

修改 GUI：

```csharp
RuntimeGuiControl? control = Scene.GetGuiControl("StartButton");
if (control is not null)
{
    control.Text = "开始";
    control.SetPosition(40, 80);
    control.SetSize(180, 40);
    control.SetWordWrap(true);
    control.Show();
}
```

```python
control = scene.get_gui_control("StartButton")
if control is not None:
    control.set_text("开始")
    control.set_position(40, 80)
    control.set_size(180, 40)
    control.set_word_wrap(True)
    control.show()
```

按钮点击控制角色说话：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    Entity.Speak("按钮被点击了");
}
```

```python
def gui_event(entity, scene, input, audio, control_id, event_name):
    if event_name == "clicked":
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
```

Python：

```python
scene.window.set_title("Battle Scene")
scene.window.set_size(1600, 900)
scene.window.set_fullscreen(False)
scene.window.set_resizable(True)
scene.window.set_timing_mode("time_synchronized")
```

`TimingMode` 可用值：

- `time_synchronized`：按真实时间推进动画，帧率波动时动画速度保持稳定。
- `frame_rate_dependent`：按帧推进，帧率下降时动画会变慢。

窗口尺寸最小会被限制到 `320 x 240`。

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
| `Width` / `Height` | `width` / `height` | 当前渲染尺寸。 |
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
Scene.Camera.SetCameraLookAt(
    "MiniMap Camera",
    positionX: 0, positionY: 20, positionZ: 0,
    targetX: 0, targetY: 0, targetZ: 0);
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
scene.camera.set_camera_look_at("MiniMap Camera", 0, 20, 0, 0, 0, 0)
scene.camera.bind_render_texture_camera("MiniMapRT", "MiniMap Camera")

mini_map = scene.get_sprite("MiniMap")
if mini_map is not None:
    mini_map.set_render_texture("MiniMapRT")

entity.set_material_render_texture(0, "MiniMapRT")
```

限制：

- Render Texture 当前渲染 3D 场景对象。
- 不包含 GamePlayer GUI、加载遮罩、Debug.DrawRay、编辑器坐标轴和编辑器 Collider 线框。

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
def gui_event(entity, scene, input, audio, control_id, event_name):
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
def gui_event(entity, scene, input, audio, control_id, event_name):
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
- GUI 坐标、Sprite 坐标、鼠标坐标都是窗口像素坐标，左上角为 `(0, 0)`。
- 3D 世界坐标使用 `System.Numerics.Vector3`，通常 Y 轴向上。
- 音频脚本只控制已注册 Audio 资源的播放状态，不负责动态加载文件。
- 轻量 Collider 用于拾取、触发和简单碰撞判断，不是完整物理模拟。
- PMX 动作、PMX 材质贴图覆盖、TTS 口型、PMX 绑定关系只对 `pmx_model` 有效。
- 如果脚本绑定到 GUI 控件的目标实体为空，GUI 事件不会有实体脚本接收；建议在 GameEditor 里为控件设置 `Target entity`。
