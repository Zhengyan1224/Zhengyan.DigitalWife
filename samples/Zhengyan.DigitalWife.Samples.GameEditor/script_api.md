# GameEditor / GamePlayer 脚本 API

本文说明 `GamePlayer` 提供给 C# `.csx` 和 Python `.py` 脚本的主要 API。

## 生命周期

C# 脚本通过全局变量判断事件：

- `IsStart`：实体脚本启动。
- `IsUpdate`：每帧更新。
- `IsGuiEvent`：GUI 控件事件。
- `IsLoadingEvent`：场景加载事件。
- `IsSpeechEvent`：TTS 播放完成事件。
- `Entity`、`Scene`、`Input`、`Audio`、`DeltaSeconds`：当前上下文对象。

Python 脚本按函数名派发：

```python
def start(entity, scene, input, audio):
    pass

def update(entity, scene, input, audio, delta_seconds):
    pass

def gui_event(entity, scene, input, audio, control_id, event_name):
    pass

def loading_started(entity, scene, input, audio, progress, message):
    pass

def loading_progress(entity, scene, input, audio, progress, message):
    pass

def loading_completed(entity, scene, input, audio, progress, message):
    pass
```

## 实体 API

C#：

```csharp
Entity.SetPosition(0, 1, 0);
Entity.Translate(0, 0, 1);
Entity.SetScale(0.2f, 0.2f, 0.2f);
Entity.RotateY(30);
Entity.Visible = true;
Entity.IsPlaying = true;
```

Python：

```python
entity.set_position(0, 1, 0)
entity.translate(0, 0, 1)
entity.set_scale(0.2, 0.2, 0.2)
entity.rotate_y(30)
entity.set_visible(True)
entity.set_playing(True)
```

`pmx_model`、`empty_object`、`textured_plane`、`particle_system` 和 `water_surface` 都支持 Transform、脚本和碰撞体。空对象不渲染，适合作为触发器、拾取点、摄像机目标点或逻辑控制器。`textured_plane` 是 3D 矩形面对象，可用图片作为纹理，并可在编辑器中开启 Billboard 让它始终面向相机。

## Input 输入 API

`Input` 是每帧输入状态快照。它适合在 `IsUpdate` / `update(...)` 中读取键盘、鼠标、滚轮和组合键。

当前输入 API 是“按住状态”而不是“按下瞬间”：

- `IsKeyDown(...)` / `is_key_down(...)`：只要按键当前处于按住状态就返回 `true`。
- `IsMouseButtonDown(...)` / `is_mouse_button_down(...)`：只要鼠标按钮当前处于按住状态就返回 `true`。
- 如果需要“按下瞬间只触发一次”，脚本需要自己保存上一帧状态。

C# 常用属性和方法：

```csharp
Input.MouseX;              // 鼠标 X，窗口像素坐标
Input.MouseY;              // 鼠标 Y，窗口像素坐标
Input.MouseDeltaX;         // 本帧鼠标 X 位移
Input.MouseDeltaY;         // 本帧鼠标 Y 位移
Input.ScrollX;             // 本帧滚轮横向增量
Input.ScrollY;             // 本帧滚轮纵向增量
Input.IsAltDown;           // 任意 Alt 是否按住
Input.IsControlDown;       // 任意 Ctrl 是否按住
Input.IsShiftDown;         // 任意 Shift 是否按住
Input.IsKeyDown("W");      // 键盘 W
Input.IsKeyDown("Space");  // 空格
Input.IsMouseButtonDown("left");
```

Python 常用属性和方法：

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

### 常用键名

C# 的 `Input.IsKeyDown(string key)` 会按 `Silk.NET.Input.Key` 枚举名解析，大小写不敏感。常用键名：

- 字母：`A` 到 `Z`，例如 `W`、`A`、`S`、`D`。
- 数字：推荐用 `Number0` 到 `Number9`。底层也有 `D0` 到 `D9`。
- 方向键：`Up`、`Down`、`Left`、`Right`。
- 功能键：`F1` 到 `F12`。
- 控制键：`Space`、`Enter`、`Escape`、`Tab`、`Backspace`、`Delete`。
- 修饰键：`ShiftLeft`、`ShiftRight`、`ControlLeft`、`ControlRight`、`AltLeft`、`AltRight`。
- 小键盘：`Keypad0` 到 `Keypad9`、`KeypadAdd`、`KeypadSubtract`、`KeypadMultiply`、`KeypadDivide`、`KeypadEnter`。

Python 桥接会探测常用键位，并支持大小写写法，例如 `input.is_key_down("w")` 和 `input.is_key_down("W")` 都可以。当前 Python 默认探测的键包括：

`W A S D Q E R F Z X C V Space Enter Escape Tab Backspace Delete Up Down Left Right Number0-Number9 D0-D9 F1-F12 ShiftLeft ShiftRight ControlLeft ControlRight AltLeft AltRight`。

如果 Python 脚本需要更多键位，需要在 `PythonScriptInstance` 的 `ProbedKeys` 中增加对应 `Silk.NET.Input.Key` 名称。

### 按住移动

C#：

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

Python：

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

### 单次触发

因为 `IsKeyDown` 是按住状态，下面示例用脚本变量保存上一帧状态，实现“按一下 Space 只触发一次”。

C#：

```csharp
bool spaceWasDown = false;

if (IsUpdate)
{
    bool spaceDown = Input.IsKeyDown("Space");
    if (spaceDown && !spaceWasDown)
    {
        Entity.Speak("Space 被按下");
    }

    spaceWasDown = spaceDown;
}
```

Python：

```python
space_was_down = False

def update(entity, scene, input, audio, delta_seconds):
    global space_was_down

    space_down = input.is_key_down("Space")
    if space_down and not space_was_down:
        entity.speak("Space 被按下")

    space_was_down = space_down
```

### 鼠标和射线拾取

C#：

```csharp
if (IsUpdate && Input.IsMouseButtonDown("left"))
{
    RuntimeRay ray = Scene.Camera.MousePointToRay(Input);
    if (Scene.Camera.RaycastEntity(ray, out RuntimeRaycastHit hit))
    {
        Console.WriteLine($"hit {hit.Entity.Name}");
    }
}
```

Python：

```python
def update(entity, scene, input, audio, delta_seconds):
    if input.is_mouse_button_down("left"):
        ray = scene.camera.mouse_point_to_ray(input)
        hit = scene.camera.raycast_entity(ray)
        if hit is not None:
            print("hit", hit["entity"]["name"])
```

鼠标按钮名称：

- `left` / `mouseleft` / `button0` / `0`
- `right` / `mouseright` / `button1` / `1`
- `middle` / `mousemiddle` / `button2` / `2`

### 滚轮控制

C#：

```csharp
if (IsUpdate && MathF.Abs(Input.ScrollY) > 0.001f)
{
    Entity.SetScale(
        Entity.Scale.X + (Input.ScrollY * 0.02f),
        Entity.Scale.Y + (Input.ScrollY * 0.02f),
        Entity.Scale.Z + (Input.ScrollY * 0.02f));
}
```

Python：

```python
def update(entity, scene, input, audio, delta_seconds):
    if abs(input.scroll_y) > 0.001:
        s = entity.scale[0] + input.scroll_y * 0.02
        entity.set_scale(s, s, s)
```

## PMX 动作 API

C#：

```csharp
Entity.ApplyMotion("assets/motions/idle.vmd");
Entity.AddMotionLayer("assets/motions/wave.vmd", weight: 0.5f);
Entity.SetMotionLayerWeight("assets/motions/wave.vmd", 1.0f);
Entity.RemoveMotionLayer("assets/motions/wave.vmd");
Entity.ClearMotion();
```

Python：

```python
entity.apply_motion("assets/motions/idle.vmd")
entity.add_motion_layer("assets/motions/wave.vmd", weight=0.5)
entity.set_motion_layer_weight("assets/motions/wave.vmd", 1.0)
entity.remove_motion_layer("assets/motions/wave.vmd")
entity.clear_motion()
```

## 人物说话 API

`Speak` 是非阻塞调用。需要播放完成后继续逻辑时，使用完成回调。

C#：

```csharp
Entity.Speak("你好");
Entity.Speak("你好", speakerId: 0, speed: 1.0f, volume: 1.0f);
Entity.Speak("播放完后转身", speakerId: 0, speed: 1.0f, volume: 1.0f, onCompleted: () =>
{
    Entity.RotateY(180);
});

Entity.SpeakWithCallback("播放完触发脚本事件", "after_intro");

if (IsSpeechEvent && SpeechCallbackName == "after_intro")
{
    Entity.SetPosition(0, 0, 0);
}
```

Python：

```python
entity.speak("你好", speaker_id=0, speed=1.0, volume=1.0)
entity.speak("播放完触发回调", speaker_id=0, speed=1.0, volume=1.0, on_completed="after_intro")

def after_intro(entity, scene, input, audio):
    entity.rotate_y(180)
```

## 碰撞体 API

碰撞体是轻量级 Collider，用于射线拾取和脚本碰撞判断。当前不会做 PMX 三角面检测，也不会创建物理引擎动态刚体。

每个实体可以绑定多个碰撞体：

- `capsule`：胶囊体。
- `box`：有向盒体。

所有碰撞体的 `Position` / `Rotation` 都是相对于实体本地坐标。实体移动或旋转时，碰撞体会一起变换。

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

兼容旧单胶囊接口：

```csharp
Entity.SetCapsuleCollider(radius: 0.35f, height: 1.7f, centerY: 0.85f, axis: "y");
Entity.DisableCollider();
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

Python 快照字段：

```python
for collider in entity.colliders:
    print(collider["name"], collider["shape"], collider["position"])
```

水面交互也使用同一套碰撞体。水面实体开启 `Enable water interaction` 后，运行时会检测其它实体的碰撞体是否接触水面区域，接触时生成视觉波纹。该检测只负责波纹触发，不提供浮力、推挤或刚体动力学。

## 相机和射线 API

相机支持类似 Unity 的 `ScreenPointToRay`。

C#：

```csharp
RuntimeRay ray = Scene.Camera.ScreenPointToRay(Input.MouseX, Input.MouseY);
RuntimeRay mouseRay = Scene.Camera.MousePointToRay(Input);

if (ray.TryIntersectPlaneY(0.0f, out Vector3 groundPoint))
{
    Entity.SetPosition(groundPoint.X, groundPoint.Y, groundPoint.Z);
}

if (Scene.Camera.RaycastEntity(ray, out RuntimeRaycastHit hit, fallbackRadius: 0.5f))
{
    Console.WriteLine($"hit {hit.Entity.Name}, collider={hit.ColliderName}, shape={hit.ColliderShape}");
}
```

射线拾取规则：

- 启用了 `Colliders[]` 的实体会遍历所有 Collider，返回最近命中。
- 未启用 Collider 的实体会使用中心包围球 fallback。
- `RuntimeRaycastHit` 包含命中的实体、Collider Id、Collider 名称、Collider 形状、距离和命中点。

Python：

```python
ray = scene.camera.screen_point_to_ray(input.mouse_x, input.mouse_y)
ground = ray.intersect_plane_y(0.0)
if ground is not None:
    entity.set_position(ground[0], ground[1], ground[2])

hit = entity.raycast(ray)
if hit is not None:
    print(hit["distance"], hit["point"])
```

## 相机控制模式

C#：

```csharp
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
Scene.Camera.UseCinematicFollowMode("Player", offsetY: 2, offsetZ: 6);
Scene.Camera.UseOrbitalFollowMode("Player", distance: 6.0f, height: 1.5f, yawSpeed: 20.0f);
Scene.Camera.UseCustomMode();
```

Python：

```python
scene.camera.use_editor_orbit_mode()
scene.camera.use_tps_mode("Player", distance=5.0, height=1.5)
scene.camera.use_lock_on_mode("Player", "Enemy", distance=5.0, height=1.6)
scene.camera.use_fps_mode("Player", eye_height=1.65)
scene.camera.use_free_fly_mode(move_speed=5.0, mouse_sensitivity=0.15)
scene.camera.use_custom_mode()
```

自定义模式下脚本每帧调用 `SetLookAt` / `set_look_at` 控制相机。

## 多相机和 Render Texture API

场景支持多个相机和多个 Render Texture。Render Texture 的脚本引用格式是 `rt:<名称>`，可以赋给支持贴图的对象，也可以赋给 PMX 材质。

C#：

```csharp
// 切换窗口主相机。
Scene.Camera.SetMainCamera("Battle Camera");

// 修改任意相机的位置和目标点。
Scene.Camera.SetCameraLookAt(
    "MiniMap Camera",
    positionX: 0, positionY: 20, positionZ: 0,
    targetX: 0, targetY: 0, targetZ: 0);

// 把 Render Texture 绑定到指定相机。
Scene.Camera.BindRenderTextureCamera("MiniMapRT", "MiniMap Camera");

// 作为 2D Sprite 贴图。
RuntimeSpriteControl? miniMap = Scene.GetSprite("MiniMap");
miniMap?.SetRenderTexture("MiniMapRT");

// 作为 PMX 材质贴图。可以按材质下标或材质名设置。
Entity.SetMaterialRenderTexture(0, "MiniMapRT");
Entity.SetMaterialTexture("Body", "project:assets/textures/body_alt.png");
Entity.ClearMaterialTextureOverride(0);
```

Python：

```python
def start(entity, scene, input, audio):
    # 快照属性。
    print(scene.main_camera)
    print(scene.camera.camera_names)
    print(scene.camera.render_texture_names)

    # 切换窗口主相机。
    scene.camera.set_main_camera("Battle Camera")

    # 修改任意相机的位置和目标点。
    scene.camera.set_camera_look_at(
        "MiniMap Camera",
        0, 20, 0,
        0, 0, 0)

    # 把 Render Texture 绑定到指定相机。
    scene.camera.bind_render_texture_camera("MiniMapRT", "MiniMap Camera")

    # 作为 2D Sprite 贴图。
    mini_map = scene.get_sprite("MiniMap")
    if mini_map is not None:
        mini_map.set_render_texture("MiniMapRT")

    # 作为 PMX 材质贴图。material 参数可以是材质下标或材质名。
    entity.set_material_render_texture(0, "MiniMapRT")
    entity.set_material_texture("Body", "project:assets/textures/body_alt.png")
    entity.clear_material_texture_override(0)

    # 只需要拿到引用字符串时，也可以使用辅助函数。
    rt_ref = scene.render_texture("MiniMapRT")  # rt:MiniMapRT
```

当前 `Render Texture` 会渲染 3D 场景对象，不包含运行器 GUI、加载遮罩和 Debug.DrawRay。编辑器预览里的坐标轴、碰撞体线框也不会写入 Render Texture。

## Debug.DrawRay

C#：

```csharp
RuntimeRay ray = Scene.Camera.MousePointToRay(Input);
Scene.Debug.DrawRay(ray.Origin, ray.Direction, length: 20.0f, durationSeconds: 0.05f);
```

Python：

```python
ray = scene.camera.mouse_point_to_ray(input)
scene.debug.draw_ray(ray.origin, ray.direction, length=20.0, duration=0.05)
```

## GUI API

C#：

```csharp
RuntimeGuiControl? control = Scene.GetGuiControl("StartButton");
if (control is not null)
{
    control.Text = "开始";
    control.SetPosition(40, 80);
    control.Hide();
}
```

Python：

```python
control = scene.get_gui_control("StartButton")
if control is not None:
    control.set_text("开始")
    control.set_position(40, 80)
    control.hide()
```

## 场景切换

C#：

```csharp
Scene.LoadScene("scenes/next.scene.json");
```

Python：

```python
scene.load_scene("scenes/next.scene.json")
```

脚本切换场景时，`GamePlayer` 会显示同一套加载遮罩，并触发目标场景的加载入口脚本。

## 存档 API

存档文件默认保存到工程目录下的 `saves/`。API 传入的是存档文件名或 `saves/` 下的相对路径，例如 `slot1.json`、`chapter1/slot1.json`。运行器会阻止 `../` 这类逃出 `saves/` 的路径。

C#：

```csharp
if (IsStart)
{
    var data = new Dictionary<string, object>
    {
        ["playerX"] = Entity.Position.X,
        ["playerY"] = Entity.Position.Y,
        ["playerZ"] = Entity.Position.Z
    };

    Scene.Save.WriteJson("slot1.json", data);
}

if (IsUpdate && Scene.Save.Exists("slot1.json"))
{
    string rawJson = Scene.Save.ReadText("slot1.json");
    Console.WriteLine(rawJson);
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

支持的接口：

- `WriteText` / `write_text(file_name, text)`
- `ReadText` / `read_text(file_name, fallback="")`
- `WriteJson` / `write_json(file_name, value)`
- `ReadJson<T>` / `read_json(file_name, fallback=None)`
- `Exists` / `exists(file_name)`
- `Delete` / `delete(file_name)`
