# Zhengyan.DigitalWife.Mmd.Game

`Zhengyan.DigitalWife.Mmd.Game` 是基于 `Silk.NET`、`OpenGL ES`、`OpenAL` 的跨平台 MMD 游戏层。它把窗口、输入、音频、相机、PMX 组件、动作层、角色关系、口型驱动、水面与粒子等常用能力包装成面向应用的 API。

## 主要 API

### 应用骨架

- `Game`
- `GameOptions`
- `GameTime`
- `GameComponent`
- `DrawableGameComponent`
- `AnimationTimingMode`

### 相机与输入

- `OrbitCamera`
- `OrbitCameraController`
- `InputManager`

### PMX / 角色

- `PmxModelComponent`
- `MotionLayerDefinition`
- `MotionLayerInfo`
- `MmdCharacter`
- `MmdCharacterGroup`

### 变换更新器

- `ITransformUpdater`
- `TransformUpdaterManager`
- `RelationTransformUpdater`
- `SpeechTransformUpdater`
- `TransformUpdaterStage`

### 场景组件

- `WaterSurfaceComponent`
- `ParticleSystemComponent`
- `ParticleSystemSettings`
- `ParticleSystemPresets`
- `ParticleSystemPresetStore`

### 口型字典

- `SpeechDictionarySet`
- `KanaDictionary`
- `VowelDictionary`

## 最小示例

```csharp
using System.Numerics;
using Silk.NET.Maths;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

using MyGame game = new();
game.Run();

internal sealed class MyGame : Game
{
    private readonly OrbitCamera _camera = new();

    public MyGame()
        : base(new GameOptions
        {
            Title = "My Digital Wife Game",
            WindowSize = new Vector2D<int>(1280, 720),
            ClearColor = new Vector4(0.08f, 0.09f, 0.12f, 1.0f),
            AnimationTimingMode = AnimationTimingMode.TimeSynchronized
        })
    {
    }

    protected override void LoadContent()
    {
        _camera.SetLookAt(new Vector3(0.0f, 2.0f, 8.0f), Vector3.Zero);
        AddComponent(new OrbitCameraController(_camera));

        AddComponent(new PmxModelComponent("GameData/Character/Body/Body.pmx", "GameData/Motion/Basic/basic_walk.vmd")
        {
            Camera = _camera,
            Scale = new Vector3(0.2f),
            Position = new Vector3(0.0f, 0.0f, 1.6f),
            LoopMotion = true,
            EnablePhysical = true,
            EnableEdge = true,
            EnableShadow = true
        });
    }
}
```

## 使用角色组管理多个模型

```csharp
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;

MmdCharacterGroup characters = new(this, _camera);

MmdCharacter body = characters.AddCharacter("GameData/Character/Body/Body.pmx", name: "Body");
MmdCharacter outfit = characters.AddCharacter("GameData/Character/MaidOutfit/maid.pmx", name: "Outfit");

RelationTransformUpdater relation = characters.BindRelation(outfit, body, bindComponentTransform: true);
relation.BindLighting = true;
```

## 使用动作层

```csharp
body.SetMotionLayers(
[
    new MotionLayerDefinition("GameData/Motion/Basic/basic_walk.vmd", 1.0f, resetPhysicsOnLoop: false),
    new MotionLayerDefinition("GameData/Motion/Basic/basic_run.vmd", 0.0f, resetPhysicsOnLoop: true)
]);

body.SetMotionLayerWeight("GameData/Motion/Basic/basic_walk.vmd", 0.4f);
body.SetMotionLayerWeight("GameData/Motion/Basic/basic_run.vmd", 0.6f);
body.SetMotionLayerResetPhysicsOnLoop("GameData/Motion/Basic/basic_walk.vmd", false);
```

## 使用口型驱动

```csharp
using Zhengyan.DigitalWife.Mmd.Game.Speech;

SpeechDictionarySet dictionaries = SpeechDictionarySet.LoadFromDirectory("Resources/SpeechLipSyncDictionaries");
SpeechTransformUpdater speech = characters.AttachSpeech(body, dictionaries);

speech.Start("ohayou gozaimasu", TimeSpan.FromMilliseconds(180), isLoop: false);
```

按语言选择基础字典：

```csharp
SpeechDictionarySet japanese = SpeechDictionarySet.LoadFromDirectory(
    "Resources/SpeechLipSyncDictionaries",
    SpeechDictionaryLanguage.Japanese);

SpeechDictionarySet chinese = SpeechDictionarySet.LoadFromDirectory(
    "Resources/SpeechLipSyncDictionaries",
    SpeechDictionaryLanguage.Chinese);
```

说明：

- `SpeechDictionaryLanguage.Japanese` 会加载 `kanadic.txt`
- `SpeechDictionaryLanguage.Chinese` 会加载 `zh_kanadic.txt`
- 中文模式支持多字符词条的最长匹配，更适合常见中文短句口型驱动

## 使用水面和粒子

```csharp
AddComponent(new WaterSurfaceComponent(_camera, 120.0f)
{
    Position = new Vector3(0.0f, -0.08f, 0.0f),
    Alpha = 0.45f
});

AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Sakura())
{
    Position = new Vector3(0.0f, 7.0f, 1.6f)
});
```

## 资源规则

运行时默认从输出目录查找：

- `Resources/`
- `GameData/`

在本仓库中，示例项目会自动把：

- `assets/mmd/engine/Resources/` 复制为输出目录的 `Resources/`
- `assets/mmd/samples/GameData/` 复制为输出目录的 `GameData/`

## 补充文档

- [MMD Game API 详细说明](../../docs/Zhengyan.DigitalWife.Mmd.Game.API.md)
- [QuickStart 说明](../../docs/Zhengyan.DigitalWife.Samples.MmdQuickStart.md)
- [资源与渲染说明](../../docs/Zhengyan.DigitalWife.Mmd.Game.Rendering-And-Assets.md)
