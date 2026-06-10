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

也可以在 `Project -> Package / Publish` 中把工程目录导出为 `.dwgame` 发布包，然后让 `GamePlayer` 直接加载包文件：

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.GamePlayer/Zhengyan.DigitalWife.Samples.GamePlayer.csproj -- <game-package.dwgame>
```

如果正式使用时不希望显示控制台窗口，见 [GamePlayer README](../Zhengyan.DigitalWife.Samples.GamePlayer/README.md#隐藏控制台--无终端启动)。

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
3. 在 `Scenes` 面板新建、删除或切换场景。
4. 在 `Assets` 面板导入 PMX、WAV/OGG、VMD、图片，或添加空对象、粒子、水面、3D 贴图矩形面。
5. 在 `Hierarchy` 选择实体，在 `Inspector` 编辑 Transform、脚本、碰撞体和类型专属设置。
6. 保存后用 `GamePlayer` 运行工程目录。

## 打包发布

开发阶段建议继续使用工程目录形式，便于直接修改 `game.project.json`、场景、资源和脚本。发布阶段可以在 `Project -> Package / Publish` 中导出 `.dwgame` 包，降低用户直接修改资源和脚本的概率。

导出选项：

- `Output package`：输出包路径。未写扩展名时会自动补 `.dwgame`。
- `Encrypt package`：启用 AES-GCM 加密。加密后运行时必须提供相同密码。
- `Split package`：按指定大小把包切分为 `.dwgame.001`、`.dwgame.002`、`.dwgame.003` 等分包。
- `Include saves`：是否把工程目录下的 `saves/` 一起打进包。通常正式发布不建议勾选。

加载命令：

```powershell
# 加载未加密包
dotnet run --project samples/Zhengyan.DigitalWife.Samples.GamePlayer/Zhengyan.DigitalWife.Samples.GamePlayer.csproj -- D:\Games\DemoGame.dwgame

# 加载加密包
dotnet run --project samples/Zhengyan.DigitalWife.Samples.GamePlayer/Zhengyan.DigitalWife.Samples.GamePlayer.csproj -- D:\Games\DemoGame.dwgame --package-password "your-password"

# 加载分包，可以传 .dwgame 或第一个 .001 分包
dotnet run --project samples/Zhengyan.DigitalWife.Samples.GamePlayer/Zhengyan.DigitalWife.Samples.GamePlayer.csproj -- D:\Games\DemoGame.dwgame.001
```

包加载时，`GamePlayer` 会复用原有工程目录加载流程，因此现有 PMX、音频、脚本、TTS、天空盒、图标等工程相对路径不需要改变。未加密 `.dwgame` 首次启动会解包到用户本地包缓存目录，后续启动会按包文件 hash 直接复用缓存，避免每次重复解包；加密包为了避免明文资源长期留在磁盘上，仍会解包到运行时临时目录并在退出时尝试清理。脚本存档不会写进解包目录，而是写入用户数据目录。

注意：加密可以防止普通用户直接解包和修改内容，但不能作为绝对安全边界。运行器本身必须能解密资源，所以密码或密钥最终仍要在运行环境中提供。

## Scenes 面板

`Scenes` 面板用于管理工程内的多个场景文件：

- `Editor scene`：切换当前编辑的场景。切换前编辑器会先保存当前场景文件，再加载目标场景到 Viewport。
- `GamePlayer startup scene`：设置游戏启动时加载的默认场景。它和当前编辑场景是分开的，切换编辑场景不会自动改变启动场景。
- `New Scene`：根据输入名称创建新的 `scenes/<name>.scene.json`，并切换到该场景。
- `Delete Active Scene`：删除当前场景文件。工程至少保留一个场景，所以最后一个场景不能删除。
- 场景名称在 `Inspector -> Scene name` 中修改；场景文件路径由编辑器自动生成并保存在 `game.project.json` 的 `Scenes` 列表中。

`GamePlayer` 启动时加载 `game.project.json` 的 `DefaultScene`。脚本切换场景仍使用工程相对路径，例如 `Scene.LoadScene("scenes/battle.scene.json")` 或 Python 的 `scene.load_scene("scenes/battle.scene.json")`。

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

脚本自定义 shader 建议放在工程目录 `assets/shaders` 下，并在脚本中通过 `assets/shaders/*.vert` / `assets/shaders/*.frag` 或 `project:assets/shaders/*.frag` 引用。当前脚本层支持给 PMX 模型和 3D 贴图矩形面绑定自定义 shader；具体属性、uniform 和 C#/Python 示例见 `script_api.html` 的 `Entity API -> 自定义 Shader`。

## 实体类型

- `pmx_model`：PMX 模型实体，支持渲染、动作、动作层、口型、TTS、脚本、绑定关系和多个碰撞体。
- `empty_object`：空对象，不渲染，但支持 Transform、脚本和多个碰撞体。
- `textured_plane`：3D 矩形面实体，使用图片作为纹理，可作为广告牌、提示牌、特效面片或远景装饰。开启 `Billboard` 后会始终面向相机。
- `particle_system`：粒子系统实体。
- `water_surface`：水面实体，可开启 `Enable water interaction`，当其它实体的碰撞体或开启了粒子水体交互的粒子接触水面范围时会产生轻量级波纹。

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

`Project -> Window / Runtime` 可设置运行器窗口标题、图标、分辨率、全屏、可拉伸、Timing Mode，以及 `Use OpenCL`。`Use OpenCL` 控制 GamePlayer 的 PMX 蒙皮是否优先使用 OpenCL；如果初始化失败，会自动回退 CPU 路径。

场景 Inspector 可设置：

- 多相机、主相机、Camera Viewport、Render Texture、相机位置、目标点、投影模式、FOV、正交大小、近远裁剪面。
- 灯光方向、环境光、清屏颜色。
- 加载界面背景色、背景图、透明度和加载进度条样式。
- 场景加载入口脚本。
- GUI 控件，包括按钮、标签、复选框、下拉框、文本框和进度条。
- 天空盒。默认内置天空盒路径是 `app:Resources/Skybox/autumn_field_puresky.jpg`，构建后会从程序输出目录的 `Resources/Skybox/` 读取。

相机支持多种脚本控制模式，包括编辑器环绕、TPS、肩部、锁定、FPS、自由飞行、RTS、俯视、等距、横版、固定、电影跟随和自定义模式。相机射线 API 类似 Unity 的 `Camera.ScreenPointToRay(...)`。

GUI 控件和加载进度条都支持 `Layout mode`。`absolute` 表示按编辑器输入的像素位置和大小直接显示；`relative` 表示以 `Project -> Window / Runtime` 中配置的 Width / Height 为基准，运行时根据实际窗口尺寸缩放位置、大小和字体大小。这样可以在 1280x720 下排版，运行到 1920x1080 或其它窗口尺寸时自动适配。

## 3D 贴图矩形面、天空盒和水面交互

`textured_plane` 是场景中的 3D 对象，不是屏幕空间 GUI。它支持 Transform、脚本和多个碰撞体，纹理路径支持工程相对路径、绝对路径、`project:` 和 `app:`。如果勾选 `Billboard`，矩形面会在渲染时自动朝向当前相机。`Receive shadow` 控制它是否接收 PMX shadow map 阴影；`Mirror reflection` 会把它当作平面镜面，在绘制前用镜像相机额外渲染一次场景，可用于墙面镜子、镜面地板或反射屏幕。脚本层可通过 `PlaneMirrorReflectionEnabled` / `PlaneMirrorReflectionStrength` 或 Python 的 `set_plane_mirror_reflection_enabled(...)` / `set_plane_mirror_reflection_strength(...)` 控制 3D 贴图矩形面的镜面反射。

水面交互分两类：

- 普通实体：依赖 `Colliders[]`，按碰撞体近似包围范围检测是否接触水面区域。
- 粒子系统：开启 `Enable water interaction` 后，按每个活跃粒子的当前位置和当前显示尺寸做近似球检测；如果 `Kill particle on water contact` 开启，入水粒子会立即消失，否则会继续穿过水面。

水面实体自己的 `Particle ripple min interval` 和 `Particle ripple merge distance` 用来控制粒子触水时的波纹密度，避免雨、瀑布这类高密度粒子把水面刷满。`Particle ripple merge distance = 0` 表示不按空间网格合并触水点，只按单个粒子做节流；数值越大，相邻粒子越容易合并成同一个波纹区域。水面 shader 当前最多同时显示 48 个活动波纹。这个效果是视觉交互，不是完整流体模拟，也不会产生浮力或真实物理反馈。

`Mirror reflection` 控制水面是否启用平面镜面反射。开启后 GameEditor/GamePlayer 会在水面绘制前用镜像相机把场景额外渲染到离屏纹理，水面 shader 再采样这张反射纹理并叠加法线扰动和 Fresnel；关闭时水面会退回更偏普通水色/环境渐变的渲染。脚本层可通过水面实体的 `MirrorReflectionEnabled` 或 Python 的 `set_mirror_reflection_enabled(...)` 在运行时切换。

阴影说明：GameEditor/GamePlayer 使用单张方向光 shadow map 替代旧的平面投影地面阴影。PMX 的 `Enable shadow` 控制是否投射并接收 shadow map 阴影；3D 贴图矩形面的 `Receive shadow` 控制是否接收阴影。shadow map 需要实际的接收面，例如场景 PMX 模型里的地面或 3D 贴图矩形面，不会像旧平面投影阴影那样自动投到固定高度平面。旧的 `DrawShadowInMainPass` 主要用于兼容其它旧示例程序，GameEditor/GamePlayer 通常不需要修改它。

性能注意：每个启用镜面反射的水面或 3D 贴图矩形面至少会多一次场景渲染，当前反射纹理默认使用当前视口 1/2 分辨率。复杂场景、多个水面、多个镜面或多个相机视口会显著增加 GPU 开销。当前实现会跳过其它启用镜面反射的水面和平面，避免递归反射和上一帧纹理反馈；严格裁剪平面暂未作为首版目标。

推荐参数模板：

- 雨天
  - 粒子：`Enable water interaction = true`，`Kill particle on water contact = true`
  - 水面：`Particle ripple min interval = 0.05 - 0.12`，`Particle ripple merge distance = 0.3 - 0.6`
- 瀑布
  - 粒子：`Enable water interaction = true`，`Kill particle on water contact = false` 或 `true`
  - 水面：`Particle ripple min interval = 0.02 - 0.08`，`Particle ripple merge distance = 0.6 - 1.2`
- 喷泉 / 点缀水花
  - 粒子：`Enable water interaction = true`，`Kill particle on water contact = false`
  - 水面：`Particle ripple min interval = 0.08 - 0.2`，`Particle ripple merge distance = 0.2 - 0.5`

经验上，`Particle ripple min interval` 越小，单位时间波纹越密；`Particle ripple merge distance` 越大，相邻粒子越容易被合并成同一个波纹区域。如果你希望雨点/水花更密集，优先降低这两个值；如果 GPU 压力或画面太乱，再提高它们。

`GameEditor` 的水面 Inspector 里提供了 `Rain Preset`、`Waterfall Preset`、`Fountain Preset` 三个快捷按钮，会直接填入一组适中的粒子波纹参数，便于快速起步再微调。

粒子 Inspector 里也提供了 `Rain Contact`、`Waterfall Contact`、`Fountain Contact` 三个快捷按钮，用来一键设置粒子系统自己的 `Enable water interaction` 和 `Kill particle on water contact`，适合和水面的预设按钮配套使用。

天空盒使用等距柱状全景图（equirectangular panorama）作为 2D 纹理。场景 Inspector 的 `Skybox` 可设置是否启用、纹理路径、曝光和颜色 Tint。内置示例图来自 Poly Haven 的 CC0 资源 `autumn_field_puresky`，已放在 `assets/mmd/engine/Resources/Skybox/autumn_field_puresky.jpg`，构建时复制到 `Resources/Skybox/`。

## 多相机和 Render Texture

场景 Inspector 的 `Cameras` 可以添加多个相机，并通过 `Main camera` 选择用于窗口渲染的主相机。每个相机都可以启用 `Viewport`，设置它在窗口中的渲染区域；多个启用 Viewport 的相机会分别渲染到不同屏幕区域，可用于分屏、多视角、监控画面等。没有任何相机启用 Viewport 时，GamePlayer 使用主相机全屏渲染。

`Render Textures` 可以创建离屏渲染目标，绑定到任意相机并设置尺寸、清屏色。每个 Render Texture 的引用格式是：

```text
rt:<RenderTextureName>
```

可以把 `rt:<name>` 用作 2D Sprite、粒子系统贴图、3D 贴图矩形面的纹理，也可以在脚本中赋给 PMX 材质。编辑器中的贴图字段旁边提供 Render Texture 下拉框；选择后会自动写入 `rt:<name>`。

脚本示例：

```csharp
Scene.Camera.SetMainCamera("Battle Camera");
Scene.Camera.SetCameraViewport("Battle Camera", 0, 0, 960, 720, "relative");
Scene.Camera.EnableCameraViewport("Battle Camera", true);
Scene.Camera.SetCameraViewport("MiniMap Camera", 960, 0, 320, 240, "relative");
Scene.Camera.EnableCameraViewport("MiniMap Camera", true);
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

## LLM Skills

`Project -> LLM / OpenAI-compatible` 中的 `Enable skills tools` 可以让 GamePlayer 在调用 LLM 时自动注册一组内置 function call 工具。开启后，LLM 可以读取项目 `skills/` 目录、读取/写入项目文件、搜索文本，并在项目目录内执行命令。

用户自定义 skills 放在游戏项目目录的 `skills/` 下，每个 skill 使用独立子目录。推荐目录结构：

```text
skills/
  quest_writer/
    SKILL.md
    scripts/
    resources/
```

`SKILL.md` 建议使用常见 skills 规范的 YAML front matter，例如：

```markdown
---
name: quest_writer
description: Generate short quest text for NPC dialogue.
---

# Quest Writer
```

启用后，C# 的 `Scene.Llm.ChatAsync/StreamChatAsync/StartChat/StartChatWithTools` 和 Python 的 `scene.llm.start_chat/start_chat_with_tools` 会自动带上内置 skills 工具。Python 的同步 `scene.llm.chat/stream_chat/stream_messages` 由 Python worker 直接请求 HTTP，目前不会执行 GamePlayer 的内置 skills 工具；运行时 UI 建议使用后台式 `start_chat`。

内置工具包括 `skill_list`、`skill_read`、`skill_list_files`、`skill_read_file`、`skill_write_file`、`skill_search_files` 和 `skill_run_command`。文件路径和命令工作目录会限制在游戏项目目录内，但命令执行仍然是受信任本地能力，只应在你信任当前项目和模型输出时开启。

## TTS

`Project -> Voice / TTS` 配置 `GamePlayer` 的人物说话能力。启用后脚本可调用 `Entity.Speak(...)` 或 `entity.speak(...)`，运行器会合成语音、播放音频并驱动 PMX 口型。

`Speech playback backend` 支持：

- `OpenAL`：默认模式，复用 `GamePlayer` 引擎音频。
- `PortAudio`：人物说话改走 `PortAudio` 播放。

模型路径支持：

- 绝对路径。
- `project:`：从工程目录开始，例如 `project:assets/tts/model.onnx`。
- `app:`：从 `GamePlayer` 输出目录开始，例如 `app:Models/tts/model.onnx`。
- 普通相对路径：先按程序输出目录解析，不存在时再按工程目录解析。推荐显式使用 `project:` 或 `app:`。

`Preload on scene load` 会在场景加载时预加载 TTS，减少首次 `Speak` 延迟。
