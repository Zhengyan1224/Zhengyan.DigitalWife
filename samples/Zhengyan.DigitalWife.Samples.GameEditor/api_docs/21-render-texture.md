---
id: render-texture
title: 多相机与 Render Texture
category: 渲染
objects:
  - RuntimeCamera
  - RuntimeSpriteControl
keywords:
  - render texture
  - multi camera
  - viewport
---

# 多相机与 Render Texture

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 多相机与 Render Texture |
| 分类 | 渲染 |
| 主要对象 | ``RuntimeCamera``, ``RuntimeSpriteControl`` |
| C# 入口 | `Scene.Camera.SetCameraRenderTexture` |
| Python 入口 | `scene.camera.set_camera_render_texture` |
| 说明 | 多相机、分屏、Render Texture、Sprite/材质引用运行时纹理。 |

## API 内容

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
