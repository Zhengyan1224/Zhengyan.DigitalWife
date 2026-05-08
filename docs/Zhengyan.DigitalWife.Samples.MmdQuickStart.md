# QuickStart 示例工程说明

`Zhengyan.DigitalWife.Samples.MmdQuickStart` 是最小可运行的 MMD 示例，目标是给二次开发者提供一个足够小、但又包含真实场景资源和常见组件的起点。

## 运行

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.MmdQuickStart/Zhengyan.DigitalWife.Samples.MmdQuickStart.csproj
```

## 示例包含的内容

- `QuickStartGame : Game`
- `OrbitCamera` 与 `OrbitCameraController`
- 身体 PMX、服装 PMX、教室场景 PMX
- 两段基础动作的权重混合
- `RelationTransformUpdater`
- `WaterSurfaceComponent`
- `ParticleSystemComponent`
- 背景音乐播放

## 默认加载的资源

来自输出目录：

- `GameData/Character/Body/Body.pmx`
- `GameData/Character/MaidOutfit/...`
- `GameData/Scene/Classroom/classroom.pmx`
- `GameData/Motion/Basic/basic_walk.vmd`
- `GameData/Motion/Basic/basic_run.vmd`
- `GameData/BGM/Lamb.ogg`
- `Resources/...`

这些资源在构建时由仓库中的：

- `assets/mmd/samples/GameData/`
- `assets/mmd/engine/Resources/`

自动复制到输出目录。

## 输入操作

| 输入 | 行为 |
| --- | --- |
| 鼠标右键拖动 | 旋转相机 |
| 鼠标中键拖动 | 平移相机 |
| 鼠标滚轮 | 缩放 |
| `W` / `S` | 推近 / 拉远 |
| `A` / `D` | 左右平移 |
| `Q` / `E` | 上下平移 |
| `Space` | 暂停或继续动作 |

## 关键代码结构

最核心的模式是：

```csharp
protected override void LoadContent()
{
    _camera.SetLookAt(new Vector3(0.0f, 2.3f, 8.0f), new Vector3(0.0f, 1.25f, 1.6f));
    AddComponent(new OrbitCameraController(_camera));

    _body = _characters.AddCharacter("GameData/Character/Body/Body.pmx", name: "Body");
    _body.SetMotionLayers(
    [
        new MotionLayerDefinition("GameData/Motion/Basic/basic_walk.vmd", 1.0f),
        new MotionLayerDefinition("GameData/Motion/Basic/basic_run.vmd", 0.0f)
    ]);

    _outfit = _characters.AddCharacter("GameData/Character/MaidOutfit/maid.pmx", name: "Outfit");
    _characters.BindRelation(_outfit, _body, bindComponentTransform: true);

    AddComponent(new WaterSurfaceComponent(_camera));
    AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Sakura()));
}
```

## 如何基于它创建自己的项目

### 1. 新建控制台项目

```powershell
dotnet new console -n MyMmdGame
```

### 2. 引用 `Mmd.Game`

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Zhengyan.DigitalWife.Mmd.Game\Zhengyan.DigitalWife.Mmd.Game.csproj" />
</ItemGroup>
```

### 3. 把资源复制到输出目录

```xml
<ItemGroup>
  <None Include="$(DigitalWifeMmdSampleDataDir)**\*">
    <Link>GameData\%(RecursiveDir)%(Filename)%(Extension)</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="$(DigitalWifeMmdEngineAssetsDir)**\*">
    <Link>Resources\%(RecursiveDir)%(Filename)%(Extension)</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

### 4. 从 `Game` 子类开始

推荐直接参考：

- `samples/Zhengyan.DigitalWife.Samples.MmdQuickStart/Program.cs`

## 适用场景

- 作为新项目模板
- 快速验证资源目录是否正确
- 学习 `PmxModelComponent`、`MmdCharacterGroup`、动作层和场景组件的基本组合方式
