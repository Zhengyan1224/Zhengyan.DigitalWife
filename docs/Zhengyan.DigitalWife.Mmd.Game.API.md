# Zhengyan.DigitalWife.Mmd.Game API 详细说明

本文面向基于 `Zhengyan.DigitalWife.Mmd.Game` 做二次开发的开发者，重点说明最常用的 API 入口、生命周期和典型组合方式。

## 命名空间总览

| 命名空间 | 主要类型 | 说明 |
| --- | --- | --- |
| `Zhengyan.DigitalWife.Mmd.Game` | `Game`、`GameOptions`、`GameTime`、`GameComponent`、`DrawableGameComponent` | 应用主循环和组件模型 |
| `Zhengyan.DigitalWife.Mmd.Game.Graphics` | `GraphicsDevice`、`OrbitCamera`、`Texture2D` | 图形设备、相机、纹理 |
| `Zhengyan.DigitalWife.Mmd.Game.Input` | `InputManager` | 键盘、鼠标、滚轮输入 |
| `Zhengyan.DigitalWife.Mmd.Game.Audio` | `AudioEngine`、`AudioClip`、`AudioSource` | OpenAL 音频层 |
| `Zhengyan.DigitalWife.Mmd.Game.Pmx` | `PmxModelComponent`、`MmdCharacter`、`MmdCharacterGroup`、`MotionLayerDefinition` | PMX 模型与角色管理 |
| `Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater` | `RelationTransformUpdater`、`SpeechTransformUpdater`、`ITransformUpdater` | 姿态更新器 |
| `Zhengyan.DigitalWife.Mmd.Game.Components` | `OrbitCameraController`、`WaterSurfaceComponent`、`ParticleSystemComponent` | 常用场景组件 |
| `Zhengyan.DigitalWife.Mmd.Game.Speech` | `SpeechDictionarySet`、`KanaDictionary`、`VowelDictionary` | 口型字典 |

## `Game` 基座

`Game` 是所有 `Mmd.Game` 应用的入口。

### 最小骨架

```csharp
using System.Numerics;
using Silk.NET.Maths;
using Zhengyan.DigitalWife.Mmd.Game;

internal sealed class MyGame : Game
{
    public MyGame()
        : base(new GameOptions
        {
            Title = "My Game",
            WindowSize = new Vector2D<int>(1280, 720),
            UseOpenCL = true,
            EnableAudio = true
        })
    {
    }

    protected override void LoadContent()
    {
    }
}
```

### 生命周期

- `Initialize()`
  适合做非资源依赖初始化。
- `LoadContent()`
  适合创建模型、相机、组件和音频资源。
- `Update(GameTime gameTime)`
  适合写应用级逻辑。
- `Draw(GameTime gameTime)`
  适合写全局渲染逻辑。
- `UnloadContent()`
  释放你自己持有的资源。

### 常用属性

- `Options`
- `GraphicsDevice`
- `Input`
- `Audio`
- `IsAudioAvailable`
- `AudioStatusMessage`
- `Window`
- `Components`
- `Title`

## `GameOptions`

最常用配置：

- `Title`
- `WindowSize`
- `VSync`
- `Samples`
- `ClearColor`
- `UseOpenCL`
- `EnableAudio`
- `AnimationTimingMode`

`AnimationTimingMode`：

- `FrameRateDependent`
- `TimeSynchronized`

建议大多数面向实际机器部署的项目使用 `TimeSynchronized`。

## 组件模型

### `GameComponent`

不负责绘制，只参与更新。

### `DrawableGameComponent`

既参与更新，也参与绘制，额外提供：

- `Visible`
- `DrawOrder`

### 添加组件

```csharp
OrbitCameraController controller = AddComponent(new OrbitCameraController(camera));
```

### 移除组件

```csharp
RemoveComponent(controller);
```

## 相机与输入

### `OrbitCamera`

常用成员：

- `SetLookAt(Vector3 position, Vector3 target)`
- `Orbit(float deltaYawDegrees, float deltaPitchDegrees)`
- `Pan(float deltaPixelsX, float deltaPixelsY)`
- `Dolly(float delta)`
- `View`
- `Projection`
- `Position`
- `Target`
- `Front`
- `Up`
- `Right`
- `Fov`

### `OrbitCameraController`

常用配置：

- `OrbitSensitivity`
- `PanSensitivity`
- `ZoomSensitivity`
- `KeyboardPanSpeed`
- `CanProcessPointerInput`
- `CanProcessKeyboardInput`

## PMX 模型组件

### `PmxModelComponent`

这是最核心的可视化模型组件。

#### 构造

```csharp
PmxModelComponent model = new(
    modelPath: "GameData/Character/Body/Body.pmx",
    motionPath: "GameData/Motion/Basic/basic_walk.vmd");
```

#### 常用属性

- `Camera`
- `Position`
- `Scale`
- `Rotation`
- `IsPlaying`
- `PlaybackSpeed`
- `LoopMotion`
- `EnablePhysical`
- `EnableEdge`
- `EnableShadow`
- `DrawShadowInMainPass`
- `LightColor`
- `AmbientLightColor`
- `AmbientLightStrength`
- `LightDirection`
- `ShadowColor`
- `ModelPath`
- `MotionPath`
- `Model`

#### 常用方法

- `Load(string modelPath, string? motionPath = null)`
- `ApplyMotion(string? motionPath)`
- `ClearMotion()`
- `ResetAnimation()`
- `SetMotionLayers(IEnumerable<MotionLayerDefinition> motionLayers)`
- `AddMotionLayer(string motionPath, float weight = 1.0f)`
- `RemoveMotionLayer(string motionPath)`
- `TrySetMotionLayerWeight(string motionPath, float weight)`
- `SetMotionLayerWeight(string motionPath, float weight)`
- `TrySetMotionLayerResetPhysicsOnLoop(string motionPath, bool resetPhysicsOnLoop)`
- `SetMotionLayerResetPhysicsOnLoop(string motionPath, bool resetPhysicsOnLoop)`
- `GetMotionLayers()`
- `AddTransformUpdater(ITransformUpdater updater)`
- `RemoveTransformUpdater(ITransformUpdater updater)`
- `CreateRelationTransformUpdater(...)`
- `CreateSpeechTransformUpdater(...)`

## 动作层

### `MotionLayerDefinition`

```csharp
new MotionLayerDefinition("GameData/Motion/Basic/basic_walk.vmd", 1.0f)
new MotionLayerDefinition("GameData/Motion/Basic/basic_wait.vmd", 1.0f, resetPhysicsOnLoop: false)
```

- `MotionPath`：动作文件路径
- `Weight`：混合权重
- `ResetPhysicsOnLoop`：该动作层循环到起点时是否重置物理。`null` 表示沿用模型当前默认值。

### 示例

```csharp
model.SetMotionLayers(
[
    new MotionLayerDefinition("GameData/Motion/Basic/basic_walk.vmd", 1.0f, resetPhysicsOnLoop: false),
    new MotionLayerDefinition("GameData/Motion/Basic/basic_run.vmd", 0.0f, resetPhysicsOnLoop: true)
]);

model.SetMotionLayerWeight("GameData/Motion/Basic/basic_walk.vmd", 0.25f);
model.SetMotionLayerWeight("GameData/Motion/Basic/basic_run.vmd", 0.75f);
model.SetMotionLayerResetPhysicsOnLoop("GameData/Motion/Basic/basic_walk.vmd", false);
```

## 角色组管理

### `MmdCharacter`

对 `PmxModelComponent` 的角色级封装。

常用成员：

- `Name`
- `ModelComponent`
- `LoadMotion()`
- `SetMotionLayers()`
- `AddMotionLayer()`
- `RemoveMotionLayer()`
- `SetMotionLayerWeight()`
- `ClearMotion()`
- `ResetAnimation()`
- `BindRelationTo()`
- `AttachSpeech()`
- `DetachSpeech()`
- `DetachRelation()`

### `MmdCharacterGroup`

适合统一管理多个角色。

常用方法：

- `AddCharacter(...)`
- `SetActive(int index)`
- `RemoveCharacterAt(int index)`
- `FindByName(string name)`
- `BindRelation(MmdCharacter target, MmdCharacter relation, ...)`
- `AttachSpeech(MmdCharacter character, SpeechDictionarySet dictionaries, ...)`

## 关系绑定

### `RelationTransformUpdater`

用于让一个 PMX 的骨骼姿态跟随另一个 PMX。

常用属性：

- `BindComponentTransform`
- `BindLighting`
- `RelationComponent`

示例：

```csharp
RelationTransformUpdater relation = characters.BindRelation(outfit, body, bindComponentTransform: true);
relation.BindLighting = true;
```

## 口型驱动

### `SpeechDictionarySet`

```csharp
SpeechDictionarySet dictionaries = SpeechDictionarySet.LoadFromDirectory("Resources/SpeechLipSyncDictionaries");
```

也可以显式指定语言：

```csharp
SpeechDictionarySet chinese = SpeechDictionarySet.LoadFromDirectory(
    "Resources/SpeechLipSyncDictionaries",
    SpeechDictionaryLanguage.Chinese);
```

### `SpeechTransformUpdater`

常用方法：

- `Start(string text, TimeSpan? framePeriod = null, bool isLoop = false)`
- `Stop(bool resetFace = true)`
- `SetVowelMorph(string vowel, string morphName)`

默认元音键：

- `あ`
- `い`
- `う`
- `え`
- `お`

示例：

```csharp
SpeechTransformUpdater speech = characters.AttachSpeech(body, dictionaries);
speech.Start("ohayou gozaimasu", TimeSpan.FromMilliseconds(180));
```

字典语言选择：

- `SpeechDictionaryLanguage.Japanese` -> `kanadic.txt`
- `SpeechDictionaryLanguage.Chinese` -> `zh_kanadic.txt`

中文模式说明：

- 中文字典使用近似谐音映射到日语假名
- `KanaDictionary` 支持多字符词条的最长匹配
- 最终仍然通过 `voweldic.txt` 折叠到 `あ / い / う / え / お`

## 水面组件

### `WaterSurfaceComponent`

常用属性：

- `Position`
- `Rotation`
- `Scale`
- `Alpha`
- `AnimationSpeed`
- `NormalTiling`
- `DeepColor`
- `ReflectionTint`
- `SkyReflectionStrength`

示例：

```csharp
AddComponent(new WaterSurfaceComponent(camera, 120.0f)
{
    Position = new Vector3(0.0f, -0.08f, 0.0f),
    Alpha = 0.45f,
    AnimationSpeed = 0.03f
});
```

## 粒子系统

### `ParticleSystemSettings`

用于完整描述一个粒子系统的参数。

### `ParticleSystemPresets`

内置预设：

- `Rain()`
- `Snow()`
- `Sakura()`
- `Cloud()`
- `Waterfall()`
- `Stream()`
- `Fire()`

### `ParticleSystemComponent`

示例：

```csharp
AddComponent(new ParticleSystemComponent(camera, ParticleSystemPresets.Sakura())
{
    Position = new Vector3(0.0f, 7.0f, 1.6f),
    Opacity = 0.55f
});
```

## 音频

### `AudioEngine`

`Game.Audio` 可用于加载和播放音频。

### 示例

```csharp
if (Audio is not null)
{
    AudioClip clip = Audio.LoadClip("GameData/BGM/Lamb.ogg");
    AudioSource source = Audio.CreateSource(clip);
    source.Looping = true;
    source.Play();
}
```

## 资源要求

运行时默认需要输出目录中存在：

- `Resources/`
- `GameData/`

详细规则见：

- [资源、渲染与跨平台说明](Zhengyan.DigitalWife.Mmd.Game.Rendering-And-Assets.md)
