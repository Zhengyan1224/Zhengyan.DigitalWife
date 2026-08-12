---
id: camera
title: Camera API
category: 相机
objects:
  - RuntimeCamera
  - RuntimeRay
keywords:
  - camera
  - viewport
  - ray
  - look_at
  - fps
  - cursor
  - vmd
---

# Camera API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Camera API |
| 分类 | 相机 |
| 主要对象 | ``RuntimeCamera``, ``RuntimeRay`` |
| C# 入口 | `Scene.Camera` |
| Python 入口 | `scene.camera` |
| 说明 | 相机属性、控制模式、视口、Render Texture、屏幕射线和拾取。 |

## API 内容

相机支持多相机、主相机、投影参数、相机控制模式、射线、Render Texture 绑定。

## VMD 相机动画

每台场景相机都能独立绑定一个 VMD。GameEditor 在 `Cameras -> <camera> -> VMD animation` 中设置路径、播放、循环、速度和当前帧。VMD 以 30 FPS 推进，相机位置、目标点、FOV 和透视/正交标记会随动作更新。

```csharp
Scene.Camera.SetCameraVmd("Main Camera", "assets/motions/camera.vmd", loop: true, playbackSpeed: 1.0f);
Scene.Camera.PauseCameraVmd("Main Camera");
Scene.Camera.SeekCameraVmd("Main Camera", 120.0f);
Scene.Camera.SetCameraVmdLoop("Main Camera", true);
Scene.Camera.SetCameraVmdPlaybackSpeed("Main Camera", 0.75f);
Scene.Camera.PlayCameraVmd("Main Camera", restart: false);
Scene.Camera.ClearCameraVmd("Main Camera");
```

```python
scene.camera.set_camera_vmd("Main Camera", "assets/motions/camera.vmd", loop=True, playback_speed=1.0)
scene.camera.pause_camera_vmd("Main Camera")
scene.camera.seek_camera_vmd("Main Camera", 120.0)
scene.camera.set_camera_vmd_loop("Main Camera", True)
scene.camera.set_camera_vmd_playback_speed("Main Camera", 0.75)
scene.camera.play_camera_vmd("Main Camera", restart=False)
scene.camera.clear_camera_vmd("Main Camera")
```

`SetCameraVmd` 会把对应相机控制模式切换为 `vmd`，从而避免鼠标或跟随控制器覆盖动作结果。不同相机可以加载不同 VMD；Render Texture 和多 Viewport 相机也会使用各自的动画状态。

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
Scene.Camera.UseFpsControlMode("Player", eyeHeight: 1.65f, mouseSensitivity: 0.12f);
Scene.Camera.UseLockedFpsMode("Player", eyeHeight: 1.65f, mouseSensitivity: 0.12f);
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
scene.camera.use_fps_control_mode("Player", eye_height=1.65, mouse_sensitivity=0.12)
scene.camera.use_locked_fps_mode("Player", eye_height=1.65, mouse_sensitivity=0.12)
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

`UseFirstPersonMode` / `UseFpsMode` 是第一人称跟随相机，鼠标视角是否需要右键由 `SetMouseLook(...)` 决定。`UseFpsControlMode` / `UseLockedFpsMode` 是 FPS 控制相机：GamePlayer 会自动锁定鼠标光标，并用 `MouseDeltaX/Y` 驱动视角旋转；切换到其它相机模式时会释放由相机控制器锁定的光标。

### FPS 控制相机示例
```csharp
if (IsStart)
{
    Scene.Camera.UseFpsControlMode(Entity.Name, eyeHeight: 1.65f, mouseSensitivity: 0.12f);
}

if (IsUpdate && Input.IsKeyDown("Escape"))
{
    Scene.Camera.UseEditorOrbitMode();
}
```

```python
def start(entity, scene, input, audio):
    scene.camera.use_fps_control_mode(entity.name, eye_height=1.65, mouse_sensitivity=0.12)

def update(entity, scene, input, audio, delta_seconds):
    if input.is_key_down("escape"):
        scene.camera.use_editor_orbit_mode()
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
