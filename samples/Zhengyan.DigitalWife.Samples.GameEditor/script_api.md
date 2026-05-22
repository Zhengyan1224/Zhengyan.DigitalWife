# GameEditor / GamePlayer 脚本 API

本文档说明 `GamePlayer` 运行工程时提供给 C# `.csx` 和 Python `.py` 脚本的 API。编辑器只负责创建和保存脚本绑定，实际调用发生在 `GamePlayer`。

## 脚本类型

实体脚本：

- 绑定在场景实体上。
- 场景加载完成后调用启动逻辑。
- 每帧调用更新逻辑。
- GUI 事件会派发给目标实体的脚本。

场景加载脚本：

- 绑定在 `Scene Loading Scripts`。
- 每次加载该场景时调用。
- 用于自定义转场逻辑、加载提示、加载时初始化等。

## C# 脚本生命周期

C# 脚本是 `.csx` 文件。脚本每次事件调用时都会带入同一组全局变量，通过布尔值判断当前事件。

可用全局变量：

- `Entity`：当前实体，类型 `RuntimeEntity`。
- `Scene`：当前场景，类型 `RuntimeScene`。
- `Input`：输入状态，类型 `RuntimeInput`。
- `Audio`：音频控制，类型 `RuntimeAudio`。
- `DeltaSeconds`：本帧时间，单位秒。
- `IsStart`：实体脚本启动事件。
- `IsUpdate`：实体脚本每帧更新事件。
- `IsGuiEvent`：GUI 控件事件。
- `GuiControlId`：触发事件的 GUI 控件 Id。
- `GuiEventName`：触发事件名称，例如 `clicked` 或 `changed`。
- `IsLoadingEvent`：场景加载生命周期事件。
- `LoadingEventName`：`loading_started`、`loading_progress`、`loading_completed`。
- `LoadingProgress`：加载进度，范围 `0.0 - 1.0`。
- `LoadingMessage`：当前加载步骤提示。
- `IsSpeechEvent`：语音播放完成事件。
- `SpeechCallbackName`：语音完成回调名称。

示例：

```csharp
if (IsStart)
{
    Entity.Speak("你好，我是小雨", speakerId: 0, speed: 1.0f, volume: 1.0f);
}

if (IsUpdate)
{
    Entity.RotateY(20.0f * (float)DeltaSeconds);
}

if (IsGuiEvent && GuiEventName == "clicked")
{
    Scene.GetGuiControl(GuiControlId)?.Hide();
}

if (IsLoadingEvent && LoadingEventName == "loading_progress")
{
    Console.WriteLine($"loading {LoadingProgress:P0}: {LoadingMessage}");
}
```

## Python 脚本生命周期

Python 脚本是 `.py` 文件。按函数名派发事件。

实体脚本函数：

```python
def start(entity, scene, input, audio):
    pass

def update(entity, scene, input, audio, delta_seconds):
    pass

def gui_event(entity, scene, input, audio, control_id, event_name):
    pass
```

场景加载脚本函数：

```python
def loading_started(entity, scene, input, audio, progress, message):
    pass

def loading_progress(entity, scene, input, audio, progress, message):
    pass

def loading_completed(entity, scene, input, audio, progress, message):
    pass
```

示例：

```python
def start(entity, scene, input, audio):
    entity.speak("你好，我是小雨", speaker_id=0, speed=1.0, volume=1.0)

def update(entity, scene, input, audio, delta_seconds):
    entity.rotate_y(20.0 * delta_seconds)

def gui_event(entity, scene, input, audio, control_id, event_name):
    if event_name == "clicked":
        control = scene.get_gui_control(control_id)
        if control is not None:
            control.hide()
```

## 实体 API

### C#

常用属性：

- `Entity.Id`
- `Entity.Name`
- `Entity.Type`
- `Entity.Position`
- `Entity.Scale`
- `Entity.Rotation`
- `Entity.IsPlaying`
- `Entity.PlaybackSpeed`
- `Entity.LoopMotion`
- `Entity.ResetPhysicsOnMotionLoop`
- `Entity.Visible`
- `Entity.IsPmxModel`

Transform：

```csharp
Entity.SetPosition(0, 1, 0);
Entity.Translate(0, 0, 1);
Entity.SetScale(0.2f, 0.2f, 0.2f);
Entity.RotateX(10);
Entity.RotateY(30);
Entity.RotateZ(5);
```

### Python

常用字段：

- `entity.id`
- `entity.name`
- `entity.type`
- `entity.position`
- `entity.scale`

Transform：

```python
entity.set_position(0, 1, 0)
entity.translate(0, 0, 1)
entity.set_scale(0.2, 0.2, 0.2)
entity.rotate_x(10)
entity.rotate_y(30)
entity.rotate_z(5)
entity.set_playing(True)
entity.set_visible(True)
entity.set_playback_speed(1.0)
```

## PMX 动作 API

C#：

```csharp
Entity.ApplyMotion("assets/motions/idle.vmd");
Entity.AddMotionLayer("assets/motions/wave.vmd", weight: 0.5f);
Entity.SetMotionLayerWeight("assets/motions/wave.vmd", 1.0f);
Entity.SetMotionLayerResetPhysicsOnLoop("assets/motions/wave.vmd", true);
Entity.RemoveMotionLayer("assets/motions/wave.vmd");
Entity.ClearMotion();
```

Python：

```python
entity.apply_motion("assets/motions/idle.vmd")
entity.add_motion_layer("assets/motions/wave.vmd", weight=0.5)
entity.set_motion_layer_weight("assets/motions/wave.vmd", 1.0)
entity.set_motion_layer_reset_physics_on_loop("assets/motions/wave.vmd", True)
entity.remove_motion_layer("assets/motions/wave.vmd")
entity.clear_motion()
```

## 人物说话 API

需要在 `Project -> Voice / TTS` 启用 TTS 并配置模型。

`Speak` 不是阻塞同步调用。它会启动后台 TTS 合成，并在主线程播放音频，调用后脚本会立即继续执行。如果需要等待播放结束后再执行下一步，应使用完成回调。

C#：

```csharp
Entity.Speak("你好，我是小雨");
Entity.Speak("你好，我是小雨", speakerId: 0);
Entity.Speak("你好，我是小雨", speakerId: 0, speed: 1.0f);
Entity.Speak("你好，我是小雨", speakerId: 0, speed: 1.0f, volume: 1.0f);
Entity.Speak("你好，我是小雨", () =>
{
    Console.WriteLine("说话播放结束");
});

Entity.Speak("你好，我是小雨", speakerId: 0, speed: 1.0f, volume: 1.0f, onCompleted: () =>
{
    Entity.RotateY(30);
});

Entity.SpeakWithCallback("你好，我是小雨", "after_intro");
Entity.StopSpeaking();
```

也可以用命名回调事件：

```csharp
if (IsSpeechEvent && SpeechCallbackName == "after_intro")
{
    Entity.SetPosition(0, 0, 0);
}
```

Python：

```python
entity.speak("你好，我是小雨")
entity.speak("你好，我是小雨", speaker_id=0, speed=1.0, volume=1.0)
entity.speak("你好，我是小雨", speaker_id=0, speed=1.0, volume=1.0, on_completed="after_intro")
entity.stop_speaking()
```

Python 的 `on_completed` 是当前脚本中的函数名：

```python
def after_intro(entity, scene, input, audio):
    entity.rotate_y(30)
```

## PMX 绑定 API

将当前 PMX 绑定到另一个 PMX，按同名骨骼同步姿态。

C#：

```csharp
Entity.BindRelation("Body", bindComponentTransform: true, bindLighting: false);
Entity.ClearRelationBinding();
```

Python：

```python
entity.bind_relation("Body", bind_component_transform=True, bind_lighting=False)
entity.clear_relation()
```

## 场景 API

### C#

```csharp
RuntimeEntity? player = Scene.GetEntity("Player");
RuntimeGuiControl? button = Scene.GetGuiControl("StartButton");
RuntimeSpriteControl? logo = Scene.GetSprite("Logo");
Scene.LoadScene("scenes/next.scene.json");
```

属性：

- `Scene.Name`
- `Scene.Entities`
- `Scene.GuiControls`
- `Scene.Sprites`
- `Scene.Window`
- `Scene.Camera`

### Python

```python
player = scene.get_entity("Player")
button = scene.get_gui_control("StartButton")
logo = scene.get_sprite("Logo")
scene.load_scene("scenes/next.scene.json")
```

字段：

- `scene.name`
- `scene.window`
- `scene.camera`

## 窗口 API

窗口配置来自 `game.project.json` 的 `window` 节点，也可以运行时修改。

C#：

```csharp
Scene.Window.SetTitle("Demo Game");
Scene.Window.SetSize(1280, 720);
Scene.Window.SetFullscreen(false);
Scene.Window.SetResizable(true);
Scene.Window.SetTimingMode("time_synchronized");
```

属性：

- `Scene.Window.Title`
- `Scene.Window.Width`
- `Scene.Window.Height`
- `Scene.Window.Fullscreen`
- `Scene.Window.Resizable`
- `Scene.Window.TimingMode`

Python：

```python
scene.window.set_title("Demo Game")
scene.window.set_size(1280, 720)
scene.window.set_fullscreen(False)
scene.window.set_resizable(True)
scene.window.set_timing_mode("time_synchronized")
```

字段：

- `scene.window.title`
- `scene.window.width`
- `scene.window.height`
- `scene.window.fullscreen`
- `scene.window.resizable`
- `scene.window.timing_mode`

`TimingMode` 可选：

- `time_synchronized`
- `frame_rate_dependent`

## 输入 API

C#：

```csharp
float x = Input.MouseX;
float y = Input.MouseY;
float dx = Input.MouseDeltaX;
float dy = Input.MouseDeltaY;
float scroll = Input.ScrollY;

bool left = Input.IsMouseButtonDown("left");
bool jump = Input.IsKeyDown("Space");
bool alt = Input.IsAltDown;
bool ctrl = Input.IsControlDown;
```

Python：

```python
x = input.mouse_x
y = input.mouse_y
dx = input.mouse_delta_x
dy = input.mouse_delta_y
scroll = input.scroll_y

left = input.is_mouse_button_down("left")
jump = input.is_key_down("Space")
alt = input.alt_down
ctrl = input.control_down
```

鼠标按钮名称：

- `left`
- `right`
- `middle`

## 相机和射线 API

相机支持透视和正交投影，运行时 API 类似 Unity 的 `Camera.ScreenPointToRay(...)`。

### C#

属性：

- `Scene.Camera.Position`
- `Scene.Camera.Target`
- `Scene.Camera.Forward`
- `Scene.Camera.Up`
- `Scene.Camera.Right`
- `Scene.Camera.Width`
- `Scene.Camera.Height`
- `Scene.Camera.ControlMode`
- `Scene.Camera.TargetEntity`
- `Scene.Camera.SubjectEntity`
- `Scene.Camera.Distance`
- `Scene.Camera.HeightOffset`
- `Scene.Camera.ShoulderOffset`
- `Scene.Camera.Smoothing`
- `Scene.Camera.MoveSpeed`
- `Scene.Camera.MouseSensitivity`
- `Scene.Camera.ProjectionMode`
- `Scene.Camera.Fov`
- `Scene.Camera.OrthographicSize`
- `Scene.Camera.NearClipPlane`
- `Scene.Camera.FarClipPlane`

方法：

```csharp
Scene.Camera.SetLookAt(0, 2, 8, 0, 1.2f, 0);
Scene.Camera.UseCustomMode();
Scene.Camera.UseEditorOrbitMode();
Scene.Camera.UseThirdPersonMode("Player", distance: 5.0f, height: 1.5f);
Scene.Camera.UseShoulderMode("Player", distance: 4.0f, height: 1.6f, shoulderOffset: 0.55f);
Scene.Camera.UseLockOnMode("Player", "Enemy", distance: 5.0f, height: 1.6f);
Scene.Camera.UseFirstPersonMode("Player", eyeHeight: 1.65f);
Scene.Camera.UseFreeFlyMode(moveSpeed: 5.0f, mouseSensitivity: 0.15f);
Scene.Camera.UseRtsMode(height: 12.0f, pitch: 55.0f, moveSpeed: 8.0f);
Scene.Camera.UseTopDownMode("Player", height: 12.0f);
Scene.Camera.UseIsometricMode("Player", distance: 12.0f);
Scene.Camera.UseSideScrollerMode("Player", distance: 10.0f, height: 1.5f);
Scene.Camera.UseFixedMode(0, 3, 8, 0, 1, 0);
Scene.Camera.UseCinematicFollowMode("Player", offsetX: 0, offsetY: 2, offsetZ: 6);
Scene.Camera.UseOrbitalFollowMode("Player", distance: 6.0f, height: 1.5f, yawSpeed: 20.0f);

RuntimeRay ray = Scene.Camera.ScreenPointToRay(Input.MouseX, Input.MouseY);
RuntimeRay centerRay = Scene.Camera.ViewportPointToRay(0.5f, 0.5f);
RuntimeRay mouseRay = Scene.Camera.MousePointToRay(Input);

Vector3 point = ray.GetPoint(10.0f);
bool hitGround = ray.TryIntersectPlaneY(0.0f, out Vector3 groundPoint);
bool hitSphere = ray.TryIntersectSphere(Entity.Position, radius: 0.5f, out float distance);

RuntimeEntity? picked = Scene.Camera.PickEntity(Input.MouseX, Input.MouseY, radius: 0.5f);
```

相机控制模式：

- `custom` / `UseCustomMode()`：脚本完全接管相机，每帧调用 `SetLookAt`、`Orbit`、`Pan`、`Dolly` 等方法。
- `editor` / `UseEditorOrbitMode()`：3ds Max 风格编辑器相机，右键旋转，中键平移，滚轮缩放，`W/A/S/D/Q/E` 辅助移动。
- `tps` / `UseThirdPersonMode()`：第三人称跟随相机，右键旋转，滚轮调距离。
- `shoulder` / `UseShoulderMode()`：越肩相机，适合射击和角色动作游戏。
- `lock_on` / `UseLockOnMode()`：锁定目标相机，镜头看向主控角色和目标之间的位置，让目标靠近画面中心，并通过 `safeRadius` 保留主控角色安全区域。
- `fps` / `UseFirstPersonMode()`：第一人称相机，镜头放在目标实体头部高度，右键控制视角。
- `free_fly` / `UseFreeFlyMode()`：自由飞行调试相机，右键看向，`W/A/S/D/Q/E` 移动。
- `rts` / `UseRtsMode()`：RTS 俯视斜角相机，`W/A/S/D` 平移，滚轮缩放。
- `top_down` / `UseTopDownMode()`：正上方俯视跟随。
- `isometric` / `UseIsometricMode()`：等距视角跟随。
- `side_scroller` / `UseSideScrollerMode()`：横版相机。
- `fixed` / `UseFixedMode()`：固定机位。
- `cinematic_follow` / `UseCinematicFollowMode()`：带偏移的平滑跟拍，适合剧情镜头。
- `orbital_follow` / `UseOrbitalFollowMode()`：围绕目标自动环绕。

自定义相机示例：

```csharp
if (IsStart)
{
    Scene.Camera.UseCustomMode();
}

if (IsUpdate)
{
    float t = (float)DateTime.Now.TimeOfDay.TotalSeconds;
    float x = MathF.Sin(t) * 6.0f;
    float z = MathF.Cos(t) * 6.0f;
    Scene.Camera.SetLookAt(x, 2.5f, z, Entity.Position.X, Entity.Position.Y + 1.2f, Entity.Position.Z);
}
```

点击地面移动实体：

```csharp
if (IsUpdate && Input.IsMouseButtonDown("left"))
{
    RuntimeRay ray = Scene.Camera.ScreenPointToRay(Input.MouseX, Input.MouseY);
    if (ray.TryIntersectPlaneY(0.0f, out Vector3 hit))
    {
        Entity.SetPosition(hit.X, hit.Y, hit.Z);
    }
}
```

点击拾取实体：

```csharp
if (IsUpdate && Input.IsMouseButtonDown("left"))
{
    RuntimeEntity? picked = Scene.Camera.PickEntity(Input.MouseX, Input.MouseY, radius: 0.6f);
    if (picked is not null)
    {
        picked.Visible = false;
    }
}
```

### Python

字段：

- `scene.camera.position`
- `scene.camera.target`
- `scene.camera.forward`
- `scene.camera.up`
- `scene.camera.right`
- `scene.camera.control_mode`
- `scene.camera.projection_mode`
- `scene.camera.fov`
- `scene.camera.orthographic_size`
- `scene.camera.near_clip_plane`
- `scene.camera.far_clip_plane`
- `scene.camera.width`
- `scene.camera.height`

方法：

```python
scene.camera.use_custom_mode()
scene.camera.use_editor_orbit_mode()
scene.camera.use_tps_mode("Player", distance=5.0, height=1.5)
scene.camera.use_shoulder_mode("Player", distance=4.0, height=1.6, shoulder_offset=0.55)
scene.camera.use_lock_on_mode("Player", "Enemy", distance=5.0, height=1.6)
scene.camera.use_fps_mode("Player", eye_height=1.65)
scene.camera.use_free_fly_mode(move_speed=5.0, mouse_sensitivity=0.15)
scene.camera.use_rts_mode(height=12.0, pitch=55.0, move_speed=8.0)
scene.camera.use_top_down_mode("Player", height=12.0)
scene.camera.use_isometric_mode("Player", distance=12.0)
scene.camera.use_side_scroller_mode("Player", distance=10.0, height=1.5)
scene.camera.use_fixed_mode(0, 3, 8, 0, 1, 0)
scene.camera.use_cinematic_follow_mode("Player", offset_x=0, offset_y=2, offset_z=6)
scene.camera.use_orbital_follow_mode("Player", distance=6.0, height=1.5, yaw_speed=20.0)

ray = scene.camera.screen_point_to_ray(input.mouse_x, input.mouse_y)
center_ray = scene.camera.viewport_point_to_ray(0.5, 0.5)
mouse_ray = scene.camera.mouse_point_to_ray(input)

point = ray.get_point(10.0)
ground = ray.intersect_plane_y(0.0)
distance = ray.intersect_sphere(entity.position, 0.5)
```

Python 自定义相机示例：

```python
import math

def start(entity, scene, input, audio):
    scene.camera.use_custom_mode()

def update(entity, scene, input, audio, delta_seconds):
    t = getattr(update, "time", 0.0) + delta_seconds
    update.time = t
    x = math.sin(t) * 6.0
    z = math.cos(t) * 6.0
    scene.camera.set_look_at(x, 2.5, z, entity.position[0], entity.position[1] + 1.2, entity.position[2])
```

点击地面移动实体：

```python
def update(entity, scene, input, audio, delta_seconds):
    if input.is_mouse_button_down("left"):
        ray = scene.camera.screen_point_to_ray(input.mouse_x, input.mouse_y)
        hit = ray.intersect_plane_y(0.0)
        if hit is not None:
            entity.set_position(hit[0], hit[1], hit[2])
```

## GUI 事件

GUI 控件事件会发送给 `Target entity` 指定的实体脚本；如果没有指定目标，运行器会尝试发送给第一个脚本实体。

C#：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    RuntimeGuiControl? control = Scene.GetGuiControl(GuiControlId);
    if (control is not null)
    {
        control.Text = "已点击";
        control.SetPosition(40, 80);
        control.Hide();
    }
}
```

Python：

```python
def gui_event(entity, scene, input, audio, control_id, event_name):
    if event_name == "clicked":
        control = scene.get_gui_control(control_id)
        if control is not None:
            control.set_text("已点击")
            control.set_position(40, 80)
            control.hide()
```

## GUI 控件 API

C# `RuntimeGuiControl`：

- `Id`
- `Name`
- `Type`
- `Text`
- `X`
- `Y`
- `Width`
- `Height`
- `Visible`
- `WordWrap`
- `Checked`
- `Items`
- `SelectedIndex`
- `SelectedItem`
- `SetPosition(float x, float y)`
- `SetSize(float width, float height)`
- `SetWordWrap(bool enabled)`
- `SetChecked(bool enabled)`
- `SetItems(params string[] items)`
- `SetSelectedIndex(int index)`
- `Show()`
- `Hide()`

Python `GuiControl`：

- `id`
- `name`
- `type`
- `text`
- `x`
- `y`
- `width`
- `height`
- `visible`
- `checked`
- `word_wrap`
- `items`
- `selected_index`
- `set_position(x, y)`
- `set_size(width, height)`
- `set_visible(enabled)`
- `show()`
- `hide()`
- `set_text(text)`
- `set_checked(enabled)`
- `set_word_wrap(enabled)`
- `set_items(items)`
- `set_selected_index(index)`

## 2D 精灵 API

C#：

```csharp
RuntimeSpriteControl? logo = Scene.GetSprite("Logo");
if (logo is not null)
{
    logo.SetPosition(40, 40);
    logo.SetSize(256, 128);
    logo.Opacity = 0.85f;
    logo.Show();
}
```

Python：

```python
logo = scene.get_sprite("Logo")
if logo is not None:
    logo.set_position(40, 40)
    logo.set_size(256, 128)
    logo.set_opacity(0.85)
    logo.show()
```

## 音频 API

音频资源来自场景的 `audio` 列表。

C#：

```csharp
Audio.Play("Bgm");
Audio.Pause("Bgm");
Audio.Stop("Bgm");
Audio.SetVolume("Bgm", 0.5f);
```

Python：

```python
audio.play("Bgm")
audio.pause("Bgm")
audio.stop("Bgm")
audio.set_volume("Bgm", 0.5)
```

## 场景加载脚本示例

C#：

```csharp
if (IsLoadingEvent && LoadingEventName == "loading_started")
{
    Console.WriteLine($"开始加载: {Scene.Name}");
}

if (IsLoadingEvent && LoadingEventName == "loading_progress")
{
    Console.WriteLine($"{LoadingProgress:P0} {LoadingMessage}");
}

if (IsLoadingEvent && LoadingEventName == "loading_completed")
{
    Console.WriteLine($"加载完成: {Scene.Name}");
}
```

Python：

```python
def loading_started(entity, scene, input, audio, progress, message):
    print(f"开始加载: {scene.name}")

def loading_progress(entity, scene, input, audio, progress, message):
    print(f"{progress:.0%} {message}")

def loading_completed(entity, scene, input, audio, progress, message):
    print(f"加载完成: {scene.name}")
```
