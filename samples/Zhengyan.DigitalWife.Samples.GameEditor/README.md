# Zhengyan.DigitalWife.Samples.GameEditor

`GameEditor` 是一个基于 `Zhengyan.DigitalWife.Mmd.Game` 的简易 3D 游戏编辑器示例。它可以用 GUI 创建工程、导入 PMX / VMD / 音频 / 图片资源，编辑场景、GUI、粒子、水面、TTS、窗口参数和脚本绑定；保存后由 `GamePlayer` 读取工程目录并运行。

脚本 API 的详细说明见 [script_api.md](./script_api.md)。

## 启动

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.GameEditor/Zhengyan.DigitalWife.Samples.GameEditor.csproj
```

默认工程目录：

```text
samples/Zhengyan.DigitalWife.Samples.GameEditor/bin/Debug/net10.0/GameEditorProjects/DemoGame
```

保存工程后运行：

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
    particles/
    sprites/
    tts/
  scripts/
    *.csx
    *.py
```

`game.project.json` 保存工程入口、场景列表、默认场景、脚本语言偏好、窗口参数和 TTS 配置。`scenes/*.scene.json` 保存具体场景，包括相机、光照、加载界面、入口脚本、GUI 控件、2D 精灵、实体、音频和动作资源。

## 基本流程

1. 在 `Project` 面板选择或输入工程目录，点击 `Use Directory`。
2. 点击 `New Project` 或 `Load`。
3. 在 `Assets` 面板导入 PMX、WAV/OGG、VMD、图片，或添加粒子、水面实体。
4. 在 `Hierarchy` 选择实体，在 `Inspector` 编辑 Transform、动作、关系绑定、粒子、水面和脚本。
5. 在 `Inspector` 的场景区域配置相机、光照、加载界面、场景入口脚本和 GUI 控件。
6. 点击 `Save`，再用 `GamePlayer` 运行工程目录。

## Project 面板

`Project` 面板负责工程级配置。

- `Project directory`：工程目录。可以用旁边的 `Paste` 粘贴路径。
- `Use Directory`：切换编辑器当前工程目录，不会自动加载。
- `Load`：读取 `game.project.json` 和默认场景。
- `Save`：保存工程、场景和脚本模板。
- `Project name`：工程名。
- `Script runtime`：新增实体脚本时默认使用 `csharp` 或 `python`。

## Window / Runtime

`Window / Runtime` 会写入 `game.project.json` 的 `window` 节点，`GamePlayer` 启动时自动应用。

- `Window title`：`GamePlayer` 窗口标题。
- `Window icon`：任务栏图标路径。支持绝对路径、`project:`、`app:` 和普通相对路径。
- `Width` / `Height`：窗口分辨率。
- `Fullscreen`：是否全屏。
- `Resizable`：窗口是否可拉伸。
- `Timing Mode`：`time_synchronized` 使用真实时间推进；`frame_rate_dependent` 使用帧率相关步进，低 FPS 时动画会变慢。
- `Apply To Editor Window`：只用于在编辑器里预览窗口设置；运行器会在加载工程时自动应用。

脚本也可以运行时修改这些参数，详见 [script_api.md](./script_api.md#窗口-api)。

## Voice / TTS

`Voice / TTS` 配置 `GamePlayer` 的人物说话能力。启用后脚本可以调用 `Entity.Speak(...)` 或 `entity.speak(...)`，运行器会合成语音、播放音频并驱动 PMX 口型。`Speak` 不是阻塞调用；如果要在播放结束后继续执行逻辑，请使用完成回调，详见 [script_api.md](./script_api.md#人物说话-api)。

常用配置：

- `Enable runtime TTS`：启用运行时 TTS。
- `TTS provider`：当前支持 `sherpa-onnx`。
- `TTS model kind`：例如 `vits`。
- `TTS model path` / `tokens path` / `lexicon path` / `data directory` / `dict directory` / `vocoder path`：模型相关路径。
- `Inference provider`：例如 `cpu`。是否支持 GPU 取决于底层 ONNX Runtime / SherpaOnnx 运行包和部署环境。
- `TTS threads`：推理线程数。
- `Default speaker ID` / `Default speech speed` / `Default speech volume`：默认说话参数。
- `Preload on scene load`：场景加载时预加载 TTS 模型。
- `Warm up text`：预热文本，可减少首次 `Speak` 延迟。
- `Enable lip sync`：启用口型。

路径规则：

- 绝对路径：直接使用。
- `project:`：从工程目录开始，例如 `project:assets/tts/model.onnx`。
- `app:`：从 `GamePlayer.exe` 输出目录开始，例如 `app:Models/tts/model.onnx`。
- 普通相对路径：先按程序输出目录解析，不存在时再按工程目录解析。推荐显式使用 `project:` 或 `app:`。

## Assets 面板

`Assets` 面板负责导入资源和创建基础实体。

- `Copy imported files into project`：启用后会把资源复制到工程目录。PMX 会复制整个模型目录，避免贴图缺失。
- `PMX path` + `Add PMX Entity`：添加 PMX 模型实体。
- `Audio path` + `Add WAV/OGG`：添加音频资源。
- `Motion path` + `Add VMD Motion`：添加动作资源。
- `Sprite path` + `Add Sprite`：添加 2D 精灵。
- `Particle preset` + `Add Particle Entity`：添加粒子系统实体。
- `Add Water Surface`：添加水面实体。

支持资源格式：

- 模型：`.pmx`
- 动作：`.vmd`
- 音频：`.wav`、`.ogg`
- 图片：PNG/JPG/BMP/DDS 等 `Texture2D` 可加载格式

## Viewport

`Viewport` 显示当前场景预览。编辑器内置坐标轴显示，便于定位原点和方向。

相机操作：

- 鼠标右键拖动：环绕。
- 鼠标中键拖动：平移。
- 鼠标滚轮：缩放。
- `W/A/S/D/Q/E`：键盘平移/缩放。

## 场景 Inspector

未选中实体时，`Inspector` 上方仍会显示场景级设置。

相机设置：

- `Camera position`：相机位置。
- `Camera target`：相机看向的目标点。
- `Projection`：`perspective` 或 `orthographic`。
- `FOV`：透视投影视场角。
- `Orthographic size`：正交投影尺寸。
- `Near clip` / `Far clip`：近远裁剪面。

光照设置：

- `Light direction`
- `Ambient color`
- `Ambient strength`
- `Clear color`

加载界面：

- `Background color`：加载遮罩底色。
- `Background image`：加载背景图片路径。
- `Image opacity`：背景图片透明度。

场景入口脚本：

- `Scene Loading Scripts` 可以挂载 C# 或 Python 脚本。
- `loading_started`：场景开始加载时调用。
- `loading_progress`：每个加载步骤后调用，提供 `progress` 和 `message`。
- `loading_completed`：场景加载完成、遮罩移除前调用。

## GUI Controls

`GUI Controls` 区域可以向场景添加 2D GUI 控件。

支持类型：

- `button`
- `label`
- `checkbox`
- `dropdown`

可编辑属性：

- `Visible`：是否显示。
- `Name` / `Type` / `Text`
- `Word wrap`：文本超出宽度时自动换行。
- `Position` / `Size`
- `Target entity`：事件派发目标实体，使用下拉框选择。
- `Event name`：按钮默认 `clicked`，复选框和下拉框建议 `changed`。
- `Style`：背景色、悬停色、按下色、文字色、边框色、边框粗细、圆角、水平对齐、垂直对齐。
- `Checked`：复选框状态。
- `Items` / `Selected index`：下拉框选项和当前选中项。

`Viewport` 会直接预览 GUI 控件样式。运行时事件由 `GamePlayer` 派发给目标实体脚本，详见 [script_api.md](./script_api.md#gui-事件)。

## 2D Sprites

2D 精灵绘制在 3D 场景之上、GUI 控件之下，适合做 HUD、图标、背景装饰。

可编辑属性：

- `Name`
- `Path`
- `Visible`
- `Position`
- `Size`
- `Rotation`
- `Opacity`
- `Draw order`

脚本可以通过 `Scene.GetSprite(...)` 或 `scene.get_sprite(...)` 控制精灵位置、尺寸、显示和透明度。

## 实体 Inspector

选中实体后可编辑实体通用属性：

- `Name`
- `Type`
- `Asset`
- `Position`
- `Rotation`
- `Scale`
- `Play animation`
- `Edge`
- `Shadow`
- `Draw shadow in main pass`

PMX 实体额外支持：

- `Playback speed`
- `Loop motion`
- `Reset physics on loop`
- `Motion Layers`
- `PMX Relation Binding`

粒子实体额外支持：

- 预设、数量、生成范围、速度、扰动、加速度、生命周期、大小、颜色、透明度、贴图、模拟速度。
- 粒子系统在加载时会按实体 Transform 预热初始化，避免云雾等长生命周期粒子先出现在默认位置。

水面实体额外支持：

- `Size`
- `Alpha`
- `Animation speed`
- `Normal tiling`
- `Deep color`
- `Reflection tint`
- `Sky reflection strength`

## 动作与动作层

先在 `Assets` 面板导入 `.vmd`，再在 PMX 实体的 `Motion Layers` 添加动作层。

动作层属性：

- `Layer path`
- `Weight`
- `Reset physics on loop`

脚本可以动态播放、叠加、调整权重和清空动作，详见 [script_api.md](./script_api.md#pmx-动作-api)。

## PMX 绑定关系

`PMX Relation Binding` 可以将一个 PMX 实体绑定到另一个 PMX 实体，根据同名骨骼同步姿态，适合衣服、配件、身体分离模型等场景。

可编辑属性：

- `Enable relation`
- `Relation PMX` / `Relation entity`
- `Bind component transform`
- `Bind lighting`

脚本可调用 `BindRelation(...)` / `bind_relation(...)` 和 `ClearRelationBinding()` / `clear_relation()` 动态添加或解除关系。

## 相机射线

运行时支持类似 Unity 的 `camera.ScreenPointToRay(Input.mousePosition)`。

C# 示例：

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

Python 示例：

```python
def update(entity, scene, input, audio, delta_seconds):
    if input.is_mouse_button_down("left"):
        ray = scene.camera.screen_point_to_ray(input.mouse_x, input.mouse_y)
        hit = ray.intersect_plane_y(0.0)
        if hit is not None:
            entity.set_position(hit[0], hit[1], hit[2])
```

更多射线和拾取 API 见 [script_api.md](./script_api.md#相机和射线-api)。

## 保存与运行

保存工程：

```text
Project -> Save
```

运行工程：

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.GamePlayer/Zhengyan.DigitalWife.Samples.GamePlayer.csproj -- <project-directory>
```

脚本调用 `Scene.LoadScene(...)` 或 `scene.load_scene(...)` 切换场景时，`GamePlayer` 会显示同一套加载界面，并触发目标场景的 `Scene Loading Scripts`。
