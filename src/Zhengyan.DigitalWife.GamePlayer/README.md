# Zhengyan.DigitalWife.GamePlayer

`GamePlayer` 是 `GameEditor` 的配套运行器。它可以读取开发期工程目录，也可以读取发布期 `.dwgame` 游戏包，加载场景、PMX、VMD 动作层、粒子、水面、音频、GUI 控件和脚本。

## 启动

```powershell
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer/Zhengyan.DigitalWife.GamePlayer.csproj -- <project-directory>
```

不传 `<project-directory>` 时，默认读取：

```text
bin/Debug/net10.0/GameEditorProjects/DemoGame
```

也可以直接加载 GameEditor 导出的 `.dwgame` 包：

```powershell
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer/Zhengyan.DigitalWife.GamePlayer.csproj -- D:\Games\DemoGame.dwgame
```

加密包需要提供密码：

```powershell
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer/Zhengyan.DigitalWife.GamePlayer.csproj -- D:\Games\DemoGame.dwgame --package-password "your-password"
```

也可以用环境变量提供密码，避免密码出现在命令历史中：

```powershell
$env:DW_GAME_PACKAGE_PASSWORD = "your-password"
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer/Zhengyan.DigitalWife.GamePlayer.csproj -- D:\Games\DemoGame.dwgame
```

分包发布时文件名类似 `DemoGame.dwgame.001`、`DemoGame.dwgame.002`、`DemoGame.dwgame.003`。启动时可以传 `.dwgame` 基础路径，也可以传第一个 `.001` 文件：

```powershell
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer/Zhengyan.DigitalWife.GamePlayer.csproj -- D:\Games\DemoGame.dwgame.001
```

包加载时，GamePlayer 会按原有工程目录流程加载。未加密 `.dwgame` 首次启动会解包到用户本地包缓存目录，后续启动会按包文件 hash 直接复用缓存，避免每次重复解包；加密包为了避免明文资源长期留在磁盘上，仍会解包到运行时临时目录并在退出时尝试清理。包模式下 `Scene.Save` / `scene.save` 不会写入解包目录，而是写入用户数据目录，例如 Windows 下的 `%LOCALAPPDATA%\Zhengyan.DigitalWife\GamePlayer\Saves\<GameName>`。

### 隐藏控制台 / 无终端启动

开发调试时推荐保留控制台，方便查看加载日志、脚本错误和平台兼容性提示。正式使用时可按平台选择无终端启动方式：

- Windows：如果希望双击运行时不弹出控制台，可将 `GamePlayer` 项目的 `OutputType` 从 `Exe` 改为 `WinExe` 后再发布。需要调试日志时建议保留一个控制台构建或把日志写入文件。
- Linux：不要从终端直接 `dotnet run` 启动；可通过 `.desktop` 启动器、系统启动项或后台命令启动。示例：`nohup dotnet Zhengyan.DigitalWife.GamePlayer.dll /path/to/project >/tmp/digitalwife-gameplayer.log 2>&1 &`，随后可 `disown` 脱离当前 shell。
- macOS：建议打包为 `.app` 后通过 Finder 或 `open` 启动；不要从 Terminal 直接运行。若直接用 `dotnet` 启动，终端窗口属于启动方式本身，GamePlayer 无法隐藏父终端。

### 应用程序图标

Windows 构建会使用仓库内的 `assets/mmd/samples/GameData/Logo/favicon.ico` 作为 `GamePlayer.exe` 的应用程序图标。该图标通过项目文件的 `ApplicationIcon` 配置嵌入到 Windows 可执行文件中。

Linux 可执行文件本身不会像 Windows `.exe` 一样嵌入桌面图标。发布到 Linux 桌面时，建议把 `assets/mmd/samples/GameData/Logo/favicon.png` 安装到用户图标目录，并创建 `.desktop` 启动器：

```bash
mkdir -p ~/.local/share/icons/hicolor/256x256/apps
cp assets/mmd/samples/GameData/Logo/favicon.png ~/.local/share/icons/hicolor/256x256/apps/zhengyan-digitalwife-gameplayer.png

mkdir -p ~/.local/share/applications
cat > ~/.local/share/applications/zhengyan-digitalwife-gameplayer.desktop <<'EOF'
[Desktop Entry]
Type=Application
Name=Zhengyan DigitalWife GamePlayer
Exec=/opt/Zhengyan.DigitalWife.GamePlayer/Zhengyan.DigitalWife.GamePlayer /path/to/game-project-or-package
Icon=zhengyan-digitalwife-gameplayer
Terminal=false
Categories=Game;
EOF

update-desktop-database ~/.local/share/applications 2>/dev/null || true
gtk-update-icon-cache ~/.local/share/icons/hicolor 2>/dev/null || true
```

请把 `Exec=` 改成你实际发布后的 GamePlayer 可执行文件路径和游戏工程/包路径。如果使用 `dotnet Zhengyan.DigitalWife.GamePlayer.dll` 方式启动，也可以把 `Exec=` 写成对应的 `dotnet ...dll <project-or-package>` 命令。

macOS 不使用 `.desktop`。正式分发时应打包为 `.app`，并在 bundle 的 `Info.plist` / `Resources` 中配置应用图标；`favicon.png` 可作为生成 `.icns` 的源图片。

## 加载界面

启动加载和脚本调用 `Scene.LoadScene(...)` / `scene.load_scene(...)` 切换场景时，都会进入同一套加载流程：

- 显示黑色全屏遮罩和进度条。
- 按步骤加载场景、实体、音频、TTS 预热、脚本和 PMX 绑定关系。
- 加载完成后再移除遮罩。

## LLM Skills

GamePlayer 支持在 LLM function call 基础上启用两组内置工具：
- `Enable skills tools`：注册 `skill_*` 工具，用于读取项目 `skills/`、项目文件、文本搜索和项目目录内命令执行。
- `Enable memory tools`：注册 `memory_*` 工具，用于读写本地长期记忆，文件保存在 save 目录下的 `memory/`。

这两个开关位于 GameEditor 的 `Project -> LLM / OpenAI-compatible`，也可以在 `game.project.json` 的 `llm.enableSkills` 和 `llm.enableMemory` 中配置。

启用后，GamePlayer 会读取项目目录下的 `skills/`：

```text
skills/
  quest_writer/
    SKILL.md
    scripts/
    resources/
```

每个 skill 建议使用 `SKILL.md`，并按常见 skills 规范写 `name` 和 `description` front matter。运行时会向 LLM 注册 `skill_list`、`skill_read`、`skill_list_files`、`skill_read_file`、`skill_write_file`、`skill_search_files` 和 `skill_run_command`。文件路径和命令工作目录会限制在当前游戏项目目录内；如果加载的是 `.dwgame` 包，则项目目录是 GamePlayer 当前解包/缓存后的运行目录。

C# 的 `Scene.Llm.ChatAsync/StreamChatAsync/StartChat/StartChatWithTools` 和 Python 的 `scene.llm.start_chat/start_chat_with_tools` 会自动带上当前已启用的内置工具。Python 的同步 `scene.llm.chat/stream_chat/stream_messages` 由 Python worker 直接请求 HTTP，目前不会执行 GamePlayer 的内置工具。

`skill_run_command` 仍然是受信任本地能力，虽然工作目录被限制在项目目录内，但命令本身会在本机执行。只在你信任当前项目和模型输出时开启 skills。

## TTS 路径规则

`game.project.json` 的 `voice` 节点支持 TTS 配置。路径字段包括：

- `modelPath`
- `tokensPath`
- `lexiconPath`
- `dataDirectory`
- `dictDirectory`
- `vocoderPath`
- `ruleFars`
- `ruleFsts`
- `lipSync.dictionaryDirectory`

路径解析规则：

- 绝对路径直接使用，例如 `D:/Models/tts/model.onnx`。
- `app:` 前缀表示从程序输出目录开始，例如 `app:Models/tts/model.onnx` 会解析到 `GamePlayer.exe` 所在目录下的 `Models/tts/model.onnx`。
- `project:` 前缀表示从工程目录开始，例如 `project:assets/tts/model.onnx`。
- 无前缀的相对路径默认先按程序输出目录解析；如果文件不存在，再尝试按工程目录解析。建议新配置显式使用 `app:` 或 `project:`。
- `ruleFsts` 可配置多个路径，用英文逗号分隔，例如 `app:Models/phone-zh.fst,app:Models/date-zh.fst`。

示例：

```json
{
  "voice": {
    "enabled": true,
    "playbackBackend": "OpenAL",
    "ttsProvider": "sherpa-onnx",
    "modelKind": "vits",
    "modelPath": "project:assets/tts/model.onnx",
    "tokensPath": "project:assets/tts/tokens.txt",
    "defaultSpeakerId": 0,
    "defaultSpeed": 1.0,
    "defaultVolume": 1.0,
    "preloadOnSceneLoad": true,
    "warmUpText": "你好",
    "lipSync": {
      "enabled": true,
      "dictionaryDirectory": "app:Resources/SpeechLipSyncDictionaries",
      "dictionaryLanguage": "Chinese"
    }
  }
}
```

`preloadOnSceneLoad` 默认为 `true`。启用后，场景加载阶段会同步加载 TTS 模型并用 `warmUpText` 做一次短文本合成，避免首次 `Speak` 时才加载模型导致明显延迟。

`playbackBackend` 支持：

- `OpenAL`：默认模式，复用 `GamePlayer` 自身的引擎音频后端。
- `PortAudio`：TTS 改用 `PortAudio` 播放；场景音频和背景音乐仍然走引擎 `OpenAL`。

## 人物说话

C#：

```csharp
if (IsStart)
{
    Entity.Speak("你好，我是小雨", speakerId: 0, speed: 1.0f, volume: 1.0f);
}
```

Python：

```python
def start(entity, scene, input, audio):
    entity.speak("你好，我是小雨", speaker_id=0, speed=1.0, volume=1.0)
```

运行器会后台合成 TTS，然后在主线程播放音频并驱动 PMX 口型。口型依赖 PMX 模型存在对应 morph，默认映射是「あ/い/う/え/お」。

## GUI 控件事件

场景支持 `button`、`label`、`checkbox`、`dropdown` 和 `textbox` 控件。按钮点击时会派发 `clicked`，复选框、下拉框和文本框变化时建议派发 `changed`。

C#：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    Entity.Speak("按钮被点击了");
    Scene.GetGuiControl(GuiControlId)?.Hide();
}
```

Python：

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name == "clicked":
        entity.speak("按钮被点击了")
        scene.get_gui_control(control_id).hide()
```

脚本可以按控件 `Id` 或 `Name` 获取 GUI 控件。C# GUI 事件全局变量包含 `GuiControlId` 和 `GuiControlName`；Python 新签名包含 `control_id` 和 `control_name`，旧的 `gui_event(..., control_id, event_name)` 仍兼容。脚本可动态控制显示、坐标、尺寸、文字、复选状态和下拉选项：

```csharp
RuntimeGuiControl? button = Scene.GetGuiControl("Button 1");
if (button is not null)
{
    button.Text = "继续";
    button.Value = "继续";
    button.SetPosition(40, 80);
    button.SetSize(220, 44);
    button.Visible = true;
    button.SetMultiline(true);
    button.SetChecked(false);
    button.SetItems("普通", "困难", "专家");
    button.SetSelectedIndex(0);
}
```

```python
button = scene.get_gui_control("Button 1")
if button is not None:
    button.set_text("继续")
    button.set_value("继续")
    button.set_position(40, 80)
    button.set_size(220, 44)
    button.set_multiline(True)
    button.set_checked(False)
    button.set_items(["普通", "困难", "专家"])
    button.set_selected_index(0)
    button.show()
```

文本框输入值保存在 `Text`，也可以通过 `Value` 读取。多行文本框使用 ImGui `InputTextMultiline`，Windows、Linux、macOS 走同一套运行时逻辑：

```csharp
if (IsGuiEvent && GuiEventName == "changed")
{
    RuntimeGuiControl? input = Scene.GetGuiControl(GuiControlId);
    if (input is not null && input.Type == "textbox")
    {
        Entity.Speak(input.Value);
    }
}
```

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name == "changed":
        textbox = scene.get_gui_control(control_id)
        if textbox is not None and textbox.type == "textbox":
            entity.speak(textbox.value)
```

文本框还会暴露 `selected_text`、`selection_start`、`selection_end`、`cursor_position`。脚本可以把选中内容写入系统剪贴板，也可以按当前选区做替换：

```csharp
RuntimeGuiControl? textbox = Scene.GetGuiControl("PromptInput");
if (textbox?.HasSelection == true)
{
    Input.SetClipboardText(textbox.SelectedText);
}

textbox?.ReplaceSelection(Input.ClipboardText);
```

```python
textbox = scene.get_gui_control("PromptInput")
if textbox is not None and textbox.has_selection:
    input.set_clipboard_text(textbox.selected_text)

if textbox is not None:
    textbox.replace_selection(input.clipboard_text)
```

脚本层也能读取主手柄快照。当前会暴露 `HasGamepad` / `has_gamepad`、手柄名称和索引、左右摇杆 XY、左右扳机值，以及 `IsGamepadButtonDown(...)` / `is_gamepad_button_down(...)`：

```csharp
if (Input.HasGamepad)
{
    Entity.Translate(Input.LeftStickX * 3.0f * (float)DeltaSeconds, 0.0f, -Input.LeftStickY * 3.0f * (float)DeltaSeconds);
    if (Input.IsGamepadButtonDown("A"))
    {
        Entity.Speak("手柄 A 被按住");
    }
}
```

```python
def update(entity, scene, input, audio, delta_seconds):
    if not input.has_gamepad:
        return

    entity.translate(input.left_stick_x * 3.0 * delta_seconds, 0.0, -input.left_stick_y * 3.0 * delta_seconds)
    if input.is_gamepad_button_down("A"):
        entity.speak("手柄 A 被按住")
```

## 窗口、Timing 和 2D 精灵

GamePlayer 会读取 `game.project.json` 的 `window` 节点并应用：

- `iconPath`：任务栏图标路径，未配置时使用默认图标。
- `width` / `height`：窗口分辨率。
- `fullscreen`：是否全屏。
- `resizable`：窗口是否可拉伸。
- `timingMode`：`time_synchronized` 或 `frame_rate_dependent`。

C# 脚本可以动态控制窗口：

```csharp
Scene.Window.SetSize(1280, 720);
Scene.Window.SetFullscreen(false);
Scene.Window.SetResizable(true);
Scene.Window.SetTimingMode("time_synchronized");
```

Python：

```python
scene.window.set_size(1280, 720)
scene.window.set_fullscreen(False)
scene.window.set_resizable(True)
scene.window.set_timing_mode("time_synchronized")
```

2D 精灵绘制在 3D 场景之上、GUI 控件之下。脚本可以按 `Id` 或 `Name` 获取并控制：

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

```python
logo = scene.get_sprite("Logo")
if logo is not None:
    logo.set_position(40, 40)
    logo.set_size(256, 128)
    logo.set_opacity(0.85)
    logo.show()
```

## PMX 绑定关系

PMX 实体支持绑定到另一个 PMX 实体，按同名骨骼同步姿态。可选同步组件 transform 和 lighting。

C#：

```csharp
if (IsStart)
{
    Entity.BindRelation("body", bindComponentTransform: true, bindLighting: false);
}
```

Python：

```python
def start(entity, scene, input, audio):
    entity.bind_relation("body", bind_component_transform=True, bind_lighting=False)
```

解除绑定：

```csharp
Entity.ClearRelationBinding();
```

```python
entity.clear_relation()
```

## 常用脚本 API

`entity` 支持：

- `SetPosition` / `set_position`
- `Translate` / `translate`
- `SetScale` / `set_scale`
- `RotateX` / `rotate_x`
- `RotateY` / `rotate_y`
- `RotateZ` / `rotate_z`
- `ApplyMotion` / `apply_motion`
- `AddMotionLayer` / `add_motion_layer`
- `SetMotionLayerWeight` / `set_motion_layer_weight`
- `SetMotionLayerResetPhysicsOnLoop` / `set_motion_layer_reset_physics_on_loop`
- `PlayMotion` / `play_motion`
- `PauseMotion` / `pause_motion`
- `StopMotion` / `stop_motion`
- `ResetMotion` / `reset_motion`
- `ResetMotionPhysics` / `reset_motion_physics`
- `SeekMotionFrame` / `seek_motion_frame`
- `SeekMotionTime` / `seek_motion_time`
- `PlayMotionLayer` / `play_motion_layer`
- `PauseMotionLayer` / `pause_motion_layer`
- `SetMotionLayerFrame` / `set_motion_layer_frame`
- `SetMotionLayerTime` / `set_motion_layer_time`
- `RemoveMotionLayer` / `remove_motion_layer`
- `EnableWaterInteraction` / `set_water_interaction_enabled`
- `KillOnWaterContact` / `set_kill_on_water_contact`
- `Speak` / `speak`
- `StopSpeaking` / `stop_speaking`
- `BindRelation` / `bind_relation`
- `ClearRelationBinding` / `clear_relation`

`scene` 支持 `GetEntity(...)`、`GetGuiControl(...)`、`GetSprite(...)`、`Window` 和 `LoadScene(...)`。

GUI 控件支持：

- C#：`Text`、`Visible`、`WordWrap`、`Checked`、`SelectedIndex`、`SelectedItem`、`SetPosition`、`SetSize`、`SetWordWrap`、`SetChecked`、`SetItems`、`SetSelectedIndex`、`Show`、`Hide`
- Python：`set_text`、`set_visible`、`set_position`、`set_size`、`set_word_wrap`、`set_checked`、`set_items`、`set_selected_index`、`show`、`hide`

`audio` 支持 `Play`、`Pause`、`Stop`、`SetVolume`、`SetLoop`。Python 对应 `play`、`pause`、`stop`、`set_volume`、`set_loop`。

## 粒子触水示例

粒子系统和水面都支持脚本调参。只有双方都开启交互时，粒子触水才会出波纹。

粒子系统侧：

- `EnableWaterInteraction` / `set_water_interaction_enabled`
- `KillOnWaterContact` / `set_kill_on_water_contact`

水面侧：

- `WaterInteractionEnabled` / `set_water_interaction_enabled`
- `WaterInteractionRadius` / `set_water_interaction_radius`
- `WaterInteractionStrength` / `set_water_interaction_strength`
- `ParticleRippleMinIntervalSeconds` / `set_particle_ripple_min_interval_seconds`
- `ParticleRippleMergeDistance` / `set_particle_ripple_merge_distance`
- `MirrorReflectionEnabled` / `set_mirror_reflection_enabled`

C#：

```csharp
RuntimeEntity? rain = Scene.GetEntity("Rain FX");
RuntimeEntity? pond = Scene.GetEntity("Pond");

if (rain is not null && pond is not null)
{
    rain.EnableWaterInteraction = true;
    rain.KillOnWaterContact = true;

    pond.WaterInteractionEnabled = true;
    pond.WaterInteractionRadius = 0.9f;
    pond.WaterInteractionStrength = 0.75f;
    pond.ParticleRippleMinIntervalSeconds = 0.08f;
    pond.ParticleRippleMergeDistance = 0.45f;
    pond.MirrorReflectionEnabled = true;
}
```

Python：

```python
rain = scene.get_entity("Rain FX")
pond = scene.get_entity("Pond")

if rain is not None and pond is not None:
    rain.set_enable_water_interaction(True)
    rain.set_kill_on_water_contact(True)

    pond.set_water_interaction_enabled(True)
    pond.set_water_interaction_radius(0.9)
    pond.set_water_interaction_strength(0.75)
    pond.set_particle_ripple_min_interval_seconds(0.08)
    pond.set_particle_ripple_merge_distance(0.45)
    pond.set_mirror_reflection_enabled(True)
```

配置模板：

- 雨天
  - `KillOnWaterContact = true`
  - `ParticleRippleMinIntervalSeconds = 0.05 - 0.12`
  - `ParticleRippleMergeDistance = 0.3 - 0.6`
- 瀑布
  - `KillOnWaterContact = false` 或 `true` 都可，取决于你是否希望粒子穿水
  - `ParticleRippleMinIntervalSeconds = 0.02 - 0.08`
  - `ParticleRippleMergeDistance = 0.6 - 1.2`
- 喷泉 / 点缀水花
  - `KillOnWaterContact = false`
  - `ParticleRippleMinIntervalSeconds = 0.08 - 0.2`
  - `ParticleRippleMergeDistance = 0.2 - 0.5`

`ParticleRippleMergeDistance = 0` 表示不按空间网格合并触水点，只按单个粒子做节流；数值越大，相邻粒子越容易合并。水面 shader 当前最多同时显示 48 个活动波纹。`MirrorReflectionEnabled` 用于切换水面的平面镜面反射：开启时会用镜像相机把场景额外渲染到离屏纹理并由水面采样，关闭后水面会更偏普通水色和环境渐变。

性能注意：每个启用镜面反射的水面至少会多一次场景渲染，当前反射纹理默认使用当前视口 1/2 分辨率。复杂场景、多个水面或多个相机视口会显著增加 GPU 开销。当前实现优先支持水平水面。
