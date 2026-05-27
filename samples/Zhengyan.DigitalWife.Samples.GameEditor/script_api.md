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
- 默认导入：`System`、`System.Collections.Generic`、`System.Globalization`、`System.IO`、`System.Linq`、`System.Numerics`、`System.Text`、`System.Text.Json`、`System.Text.RegularExpressions`、`System.Threading`、`System.Threading.Tasks`、`Zhengyan.DigitalWife.Samples.GamePlayer`。
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
| `LoopMotion` | 无直接快照字段 | PMX 动作是否循环。Python 用 `set_loop_motion` 修改。 |
| `ResetPhysicsOnMotionLoop` | 无直接快照字段 | PMX 动作循环时是否重置物理。Python 用 `set_reset_physics_on_motion_loop` 修改。 |
| `EnableEdge` | 无直接快照字段 | PMX 是否绘制描边。Python 用 `set_edge_enabled` 修改。 |
| `EnableShadow` | 无直接快照字段 | PMX 是否参与阴影绘制。Python 用 `set_shadow_enabled` 修改。 |
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
- `EnableEdge`、`EnableShadow`、`DrawShadowInMainPass` 仅对 PMX 有意义。
- `DrawShadowInMainPass = false` 时，地面影子会交给独立的地面阴影 pass 处理；`true` 时在 PMX 主绘制阶段直接绘制。
- `PlayMotion` / `PauseMotion` 只改变播放状态，不会清掉已加载动作层。
- `StopMotion` 会把动作停下并重置到起始状态，效果接近“停止并回到 0 帧”。
- `ResetMotion` 会重置动作与姿态；`ResetMotionPhysics` 只重置物理。
- `SeekMotionFrame` / `SeekMotionTime` 会把当前 PMX 动作层一起跳到指定帧或秒。
- `PlayMotionLayer` / `PauseMotionLayer` / `SetMotionLayerFrame` / `SetMotionLayerTime` 只作用于指定动作层。

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
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
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
| `Scene.Performance` | `scene.performance` | 性能指标快照，例如 FPS。 |
| `Scene.Fps` | `scene.fps` | 平滑后的当前 FPS 快捷属性。 |
| `Scene.RawFps` | `scene.raw_fps` | 当前帧瞬时 FPS。 |
| `Scene.RenderTexture(name)` | `scene.render_texture(name)` | 返回 `rt:name` 引用。 |
| `Scene.LoadScene(path)` | `scene.load_scene(path)` | 切换场景。 |

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
