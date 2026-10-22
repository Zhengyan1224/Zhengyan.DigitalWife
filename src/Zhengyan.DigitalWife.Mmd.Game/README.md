# Mmd.Game：场景渲染与 MMD 交互运行层

这个库把底层 MMD 数据变成可显示、可交互的场景，是 GameEditor、PC GamePlayer、部分 Android 渲染组件与示例的基础。

它负责图形组件、窗口/游戏循环、输入和音频等能力；游戏工程格式与游戏脚本由上层项目管理。只想制作作品请从 [GameEditor](../Zhengyan.DigitalWife.GameEditor/README.md)开始。

## 提供哪些组件

| 分组 | 代表入口 |
| --- | --- |
| 游戏骨架 | `Game / GameComponent / GameOptions` |
| 图形 | `GraphicsDevice / IRenderer`、OpenGL 与 Vulkan 后端 |
| 相机与输入 | `OrbitCamera / InputManager`、键鼠/手柄/Touch |
| MMD 角色 | `PmxModelComponent / MmdCharacter / MmdCharacterGroup` |
| 动作与绑定 | 动作层、RelationTransformUpdater、口型更新器 |
| 画面组件 | 天空盒、水面、粒子、图片平面、镜面、线条 |
| 离屏与阴影 | Render Target、PMX 阴影 Pass、反射纹理 |
| 音频 | AudioClip / AudioSource / AudioListener |

渲染后端由统一接口选择。OpenGL 的源码 shader 与 Vulkan 的 SPIR-V 契约不同；不要把一个后端的 shader 文件直接当作另一个后端的资源。

## 先跑一个现成示例

需要 .NET 10 SDK 和可用图形驱动，在仓库根目录：

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.MmdQuickStart
```

预期显示内置角色、动作与场景。它不需要 LLM 服务或语音模型。带控制面板的版本：

```text
dotnet run --project samples/Zhengyan.DigitalWife.Samples.MmdDemo
```

这两个示例比从零配置渲染循环更适合首次接触本库。

## 在 C# 应用中创建游戏

在自己的项目引用本库后，可以继承 Game。下面只是创建窗口的最小结构；模型加载见 QuickStart：

```csharp
using Zhengyan.DigitalWife.Mmd.Game;

using var game = new MyGame();
game.Run();

sealed class MyGame : Game
{
    public MyGame() : base(new GameOptions { Title = "My MMD Game" }) { }
    protected override void LoadContent() { }
}
```

引擎组件用 AddComponent 加入生命周期。复杂场景的反射、阴影、相机和资源释放需要宿主管理，不能只创建模型后忽略加载/销毁流程。

## 资源和平台

构建时会复制 `assets/mmd/engine/Resources/` 中的 shader、Toon、天空盒、粒子、字典等资源。独立分发时需要保留对应资源目录。

PC OpenGL 可选 OpenCL 蒙皮，Vulkan 可选 Compute；失败可回退 CPU。Android 不启用 OpenCL，其窗口和音频使用平台适配。Linux 原生依赖见[主 README](../../README.md#linux-运行依赖)。

PMX 物理、通用 Collider、水面视觉波纹用途不同。多镜面、多相机和阴影会增加渲染成本，需要按目标设备调整质量。

## 文档与验证

```text
dotnet build src/Zhengyan.DigitalWife.Mmd.Game
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests
```

- [项目总览](../../README.md)
- [渲染后端设计](../../docs/rendering-backends.md)
- [引擎资源目录](../../assets/mmd/engine/Resources/README.md)
- [MMD QuickStart](../../samples/Zhengyan.DigitalWife.Samples.MmdQuickStart/README.md)
- [MMD Demo](../../samples/Zhengyan.DigitalWife.Samples.MmdDemo/README.md)
