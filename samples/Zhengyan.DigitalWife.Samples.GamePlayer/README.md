# Zhengyan.DigitalWife.Samples.GamePlayer

`GamePlayer` 是 `GameEditor` 的配套运行器。它读取工程目录，加载场景、PMX、VMD 动作层、粒子、水面、音频、GUI 控件和脚本。

## 启动

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.GamePlayer/Zhengyan.DigitalWife.Samples.GamePlayer.csproj -- <project-directory>
```

不传 `<project-directory>` 时，默认读取：

```text
bin/Debug/net10.0/GameEditorProjects/DemoGame
```

## 加载界面

启动加载和脚本调用 `Scene.LoadScene(...)` / `scene.load_scene(...)` 切换场景时，都会进入同一套加载流程：

- 显示黑色全屏遮罩和进度条。
- 按步骤加载场景、实体、音频、TTS 预热、脚本和 PMX 绑定关系。
- 加载完成后再移除遮罩。

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

场景支持 `button`、`label`、`checkbox` 和 `dropdown` 控件。按钮点击时会派发 `clicked`，复选框和下拉框变化时建议派发 `changed`。

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
def gui_event(entity, scene, input, audio, control_id, event_name):
    if event_name == "clicked":
        entity.speak("按钮被点击了")
        scene.get_gui_control(control_id).hide()
```

脚本可以按控件 `Id` 或 `Name` 获取 GUI 控件，并动态控制显示、坐标、尺寸、文字、复选状态和下拉选项：

```csharp
RuntimeGuiControl? button = Scene.GetGuiControl("Button 1");
if (button is not null)
{
    button.Text = "继续";
    button.SetPosition(40, 80);
    button.SetSize(220, 44);
    button.Visible = true;
    button.SetChecked(false);
    button.SetItems("普通", "困难", "专家");
    button.SetSelectedIndex(0);
}
```

```python
button = scene.get_gui_control("Button 1")
if button is not None:
    button.set_text("继续")
    button.set_position(40, 80)
    button.set_size(220, 44)
    button.set_checked(False)
    button.set_items(["普通", "困难", "专家"])
    button.set_selected_index(0)
    button.show()
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
- `RemoveMotionLayer` / `remove_motion_layer`
- `Speak` / `speak`
- `StopSpeaking` / `stop_speaking`
- `BindRelation` / `bind_relation`
- `ClearRelationBinding` / `clear_relation`

`scene` 支持 `GetEntity(...)`、`GetGuiControl(...)`、`GetSprite(...)`、`Window` 和 `LoadScene(...)`。

GUI 控件支持：

- C#：`Text`、`Visible`、`WordWrap`、`Checked`、`SelectedIndex`、`SelectedItem`、`SetPosition`、`SetSize`、`SetWordWrap`、`SetChecked`、`SetItems`、`SetSelectedIndex`、`Show`、`Hide`
- Python：`set_text`、`set_visible`、`set_position`、`set_size`、`set_word_wrap`、`set_checked`、`set_items`、`set_selected_index`、`show`、`hide`

`audio` 支持 `Play`、`Pause`、`Stop`、`SetVolume`。
