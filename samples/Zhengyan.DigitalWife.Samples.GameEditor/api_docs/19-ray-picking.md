---
id: ray-picking
title: 射线与拾取
category: 相机
objects:
  - RuntimeRay
  - RuntimeRaycastHit
keywords:
  - ray
  - picking
  - raycast
  - collider
---

# 射线与拾取

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 射线与拾取 |
| 分类 | 相机 |
| 主要对象 | ``RuntimeRay``, ``RuntimeRaycastHit`` |
| C# 入口 | `Scene.Camera.ScreenPointToRay, RaycastEntity` |
| Python 入口 | `scene.camera.screen_point_to_ray, entity.raycast` |
| 说明 | 射线生成、平面/球体/Collider 相交、场景拾取和 Debug.DrawRay。 |

## API 内容

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

场景级射线检测：

```csharp
RuntimeRay ray = Scene.Camera.MousePointToRay(Input);
if (Scene.Physics.Raycast(ray, out RuntimeRaycastHit sceneHit, maxDistance: 100.0f))
{
    Console.WriteLine($"scene hit {sceneHit.Entity.Name}");
}
```

```python
ray = scene.camera.mouse_point_to_ray(input)
scene_hit = scene.physics.raycast(ray, max_distance=100.0)
if scene_hit is not None:
    print("scene hit", scene_hit["entityName"])
```

射线检测规则：

- 优先检测实体显式绑定的 Collider，返回最近命中。
- 只有 `pmx_model` 在没有显式 Collider 时会使用中心包围球 fallback。
- `water_surface`、`particle_system`、`empty_object`、`textured_plane` 如果需要被射线命中，应在编辑器中添加 Collider。
- 当前不会对 PMX 三角面做逐面相交检测。
- C# 提供 `Scene.Camera.RaycastEntity(...)` 做场景级拾取。
- C# / Python 均可使用 `Scene.Physics.Raycast(...)` / `scene.physics.raycast(...)` 做场景级 Collider 检测；贴地移动请优先看 `Physics / Grounding API`。

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
