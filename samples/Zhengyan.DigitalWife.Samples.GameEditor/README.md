# Zhengyan.DigitalWife.Samples.GameEditor

`GameEditor` 是基于 `Zhengyan.DigitalWife.Mmd.Game` 的简易 3D 游戏编辑器示例。它可以通过 GUI 创建工程、导入 PMX / VMD / WAV / OGG / 图片资源，编辑场景、多相机、Render Texture、天空盒、灯光、GUI、粒子、水面、2D 精灵、3D 贴图矩形面、TTS、脚本和碰撞体。保存后由 `GamePlayer` 读取工程目录并运行。

脚本 API 详见 [script_api.md](./script_api.md)。

## 启动

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.GameEditor/Zhengyan.DigitalWife.Samples.GameEditor.csproj
```

默认工程目录：

```text
samples/Zhengyan.DigitalWife.Samples.GameEditor/bin/Debug/net10.0/GameEditorProjects/DemoGame
```

运行保存后的工程：

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.GamePlayer/Zhengyan.DigitalWife.Samples.GamePlayer.csproj -- <project-directory>
```

不传 `<project-directory>` 时，`GamePlayer` 会读取默认 DemoGame 目录。

## 工程结构

```text
DemoGame/
  game.project.json
  scenes/
    main.scene.json
  assets/
    models/
    audio/
    motions/
    sprites/
    textures/
    tts/
  scripts/
    *.csx
    *.py
```

`game.project.json` 保存工程入口、默认场景、脚本语言偏好、窗口参数和 TTS 配置。`scenes/*.scene.json` 保存具体场景，包括相机、灯光、加载界面、入口脚本、GUI 控件、2D 精灵、实体、音频和动作资源。

## 基本流程

1. 在 `Project` 面板选择工程目录并点击 `Use Directory`。
2. 点击 `New Project` 或 `Load`。
3. 在 `Assets` 面板导入 PMX、WAV/OGG、VMD、图片，或添加空对象、粒子、水面、3D 贴图矩形面。
4. 在 `Hierarchy` 选择实体，在 `Inspector` 编辑 Transform、脚本、碰撞体和类型专属设置。
5. 保存后用 `GamePlayer` 运行工程目录。

## Assets 面板

- `Add PMX Entity`：添加 PMX 模型实体。
- `Add Empty Object`：添加空对象。空对象不显示任何模型，但和 PMX 一样可以绑定脚本和多个碰撞体，适合作为触发器、拾取点、逻辑控制器或碰撞代理。
- `Add WAV/OGG`：添加音频资源。
- `Add VMD Motion`：添加动作资源。
- `Add Sprite`：添加 2D 精灵。
- `Add Textured Plane`：把当前图片路径添加为 3D 矩形面实体，可设置宽高、透明度、颜色和 Billboard。
- `Add Particle Entity`：添加粒子系统实体。
- `Add Water Surface`：添加水面实体。

如果勾选 `Copy imported files into project`，导入资源会立即复制到工程目录。PMX 会复制整个模型目录，避免贴图丢失。

即使导入时没有勾选复制，保存工程时编辑器也会扫描工程引用的外部资源并归档到工程目录下，包括 PMX、音频、VMD、2D Sprite、3D 贴图矩形面纹理、粒子贴图、天空盒贴图、加载背景图、窗口图标、TTS 模型/词表/字典等。保存后配置会改成工程相对路径。`app:` 内置资源、`Resources/...` 运行器资源和 `rt:<RenderTextureName>` 引用不会被复制。

## 实体类型

- `pmx_model`：PMX 模型实体，支持渲染、动作、动作层、口型、TTS、脚本、绑定关系和多个碰撞体。
- `empty_object`：空对象，不渲染，但支持 Transform、脚本和多个碰撞体。
- `textured_plane`：3D 矩形面实体，使用图片作为纹理，可作为广告牌、提示牌、特效面片或远景装饰。开启 `Billboard` 后会始终面向相机。
- `particle_system`：粒子系统实体。
- `water_surface`：水面实体，可开启 `Enable water interaction`，当其它实体的碰撞体接触水面范围时会产生轻量级波纹。

## 多碰撞体

每个实体都可以在 `Inspector -> Colliders` 下绑定多个碰撞体。碰撞体是轻量级运行时 Collider，不是 Bullet/物理引擎刚体模拟，也不是 PMX 三角面检测。

支持形状：

- `capsule`：胶囊体，可设置本地位置、本地旋转、半径、高度和轴向。
- `box`：立方体/长方体，可设置本地位置、本地旋转和尺寸。

规则：

- 碰撞体位置和旋转都是相对于绑定实体的本地坐标。
- 实体移动、旋转或缩放时，所有碰撞体会跟随实体变换。
- 多个碰撞体之间的相对位置保持不变。
- 编辑器中会用线框显示碰撞体；`GamePlayer` 默认不显示。
- 射线拾取会遍历实体所有碰撞体，命中最近的 Collider。
- 如果实体没有启用 Collider，射线拾取会回退到实体中心包围球。

旧项目里的单个 `Collision` 字段仍能读取；在编辑器里打开后会迁移为 `Colliders[]`。

## 脚本

实体可以绑定 C# `.csx` 或 Python `.py` 脚本。`GamePlayer` 会在场景加载完成后调用实体脚本的启动逻辑，并每帧调用更新逻辑。GUI 事件、TTS 播放完成事件、场景加载事件也会派发到脚本。

编辑器中添加脚本时会在工程目录的 `scripts/` 下创建模板文件。若在脚本 `Path` 输入框中粘贴或填写工程外部的 `.csx` / `.py` 文件，编辑器会在路径提交或保存工程时把它复制到当前游戏工程的 `scripts/` 目录，并把绑定路径改成工程相对路径。保存工程时会做一次轻量语法检查：C# 使用 Roslyn Script 语法解析，Python 使用本机 `python -m py_compile`，检查结果显示在编辑器左侧状态栏。

碰撞体脚本示例见 [script_api.md#碰撞体-api](./script_api.md#碰撞体-api)。

脚本可通过 `Scene.Save` / `scene.save` 进行存档和读档。存档文件默认写入工程目录的 `saves/`，调用时传入文件名即可，例如 `slot1.json`。详见 [script_api.md#存档-api](./script_api.md#存档-api)。

## 窗口、相机和加载界面

`Project -> Window / Runtime` 可设置运行器窗口标题、图标、分辨率、全屏、可拉伸和 Timing Mode。

场景 Inspector 可设置：

- 多相机、主相机、Render Texture、相机位置、目标点、投影模式、FOV、正交大小、近远裁剪面。
- 灯光方向、环境光、清屏颜色。
- 加载界面背景色、背景图和透明度。
- 场景加载入口脚本。
- GUI 控件。
- 天空盒。默认内置天空盒路径是 `app:Resources/Skybox/autumn_field_puresky.jpg`，构建后会从程序输出目录的 `Resources/Skybox/` 读取。

相机支持多种脚本控制模式，包括编辑器环绕、TPS、肩部、锁定、FPS、自由飞行、RTS、俯视、等距、横版、固定、电影跟随和自定义模式。相机射线 API 类似 Unity 的 `Camera.ScreenPointToRay(...)`。

## 3D 贴图矩形面、天空盒和水面交互

`textured_plane` 是场景中的 3D 对象，不是屏幕空间 GUI。它支持 Transform、脚本和多个碰撞体，纹理路径支持工程相对路径、绝对路径、`project:` 和 `app:`。如果勾选 `Billboard`，矩形面会在渲染时自动朝向当前相机。

水面交互依赖实体的 `Colliders[]`。开启水面实体的 `Enable water interaction` 后，`GameEditor` 和 `GamePlayer` 每帧会用碰撞体的近似包围范围检测是否接触水面区域；命中时按约 0.35 秒节流产生波纹。这个效果是视觉交互，不是完整流体模拟，也不会产生浮力或真实物理反馈。

天空盒使用等距柱状全景图（equirectangular panorama）作为 2D 纹理。场景 Inspector 的 `Skybox` 可设置是否启用、纹理路径、曝光和颜色 Tint。内置示例图来自 Poly Haven 的 CC0 资源 `autumn_field_puresky`，已放在 `assets/mmd/engine/Resources/Skybox/autumn_field_puresky.jpg`，构建时复制到 `Resources/Skybox/`。

## 多相机和 Render Texture

场景 Inspector 的 `Cameras` 可以添加多个相机，并通过 `Main camera` 选择用于窗口渲染的主相机。`Render Textures` 可以创建离屏渲染目标，绑定到任意相机并设置尺寸、清屏色。每个 Render Texture 的引用格式是：

```text
rt:<RenderTextureName>
```

可以把 `rt:<name>` 用作 2D Sprite、粒子系统贴图、3D 贴图矩形面的纹理，也可以在脚本中赋给 PMX 材质。编辑器中的贴图字段旁边提供 Render Texture 下拉框；选择后会自动写入 `rt:<name>`。

脚本示例：

```csharp
Scene.Camera.SetMainCamera("Battle Camera");
Scene.Camera.BindRenderTextureCamera("MiniMapRT", "MiniMap Camera");
Scene.GetSprite("MiniMap")?.SetRenderTexture("MiniMapRT");
Entity.SetMaterialRenderTexture(0, "PortraitRT");
Entity.SetMaterialTexture("Body", "project:assets/textures/body_alt.png");
```

PMX 脚本控制示例：

```csharp
if (Entity.IsPmxModel)
{
    Entity.IsPlaying = true;
    Entity.PlaybackSpeed = 1.05f;
    Entity.LoopMotion = true;
    Entity.ResetPhysicsOnMotionLoop = true;

    Entity.EnableEdge = true;
    Entity.EnableShadow = true;
    Entity.DrawShadowInMainPass = false;
}
```

```python
if entity.type == "pmx_model":
    entity.set_playing(True)
    entity.set_playback_speed(1.05)
    entity.set_loop_motion(True)
    entity.set_reset_physics_on_motion_loop(True)

    entity.set_edge_enabled(True)
    entity.set_shadow_enabled(True)
    entity.set_draw_shadow_in_main_pass(False)
```

## TTS

`Project -> Voice / TTS` 配置 `GamePlayer` 的人物说话能力。启用后脚本可调用 `Entity.Speak(...)` 或 `entity.speak(...)`，运行器会合成语音、播放音频并驱动 PMX 口型。

模型路径支持：

- 绝对路径。
- `project:`：从工程目录开始，例如 `project:assets/tts/model.onnx`。
- `app:`：从 `GamePlayer` 输出目录开始，例如 `app:Models/tts/model.onnx`。
- 普通相对路径：先按程序输出目录解析，不存在时再按工程目录解析。推荐显式使用 `project:` 或 `app:`。

`Preload on scene load` 会在场景加载时预加载 TTS，减少首次 `Speak` 延迟。
