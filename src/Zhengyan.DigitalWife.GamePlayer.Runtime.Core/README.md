# GamePlayer.Runtime.Core：共享场景运行逻辑

这个 .NET 10 类库负责平台宿主需要的场景数据、实体、相机、动画、碰撞和导航逻辑。Android 播放器通过它组织场景，再由平台和渲染层处理窗口、输入、图形与音频。

普通用户不需要启动或配置这个库。制作游戏用 [GameEditor](../Zhengyan.DigitalWife.GameEditor/README.md)，运行游戏用 [GamePlayer](../Zhengyan.DigitalWife.GamePlayer/README.md)。

## 主要组成

| 类型 | 用途 |
| --- | --- |
| `RuntimeScene / RuntimeSceneManager` | 场景与加载/切换生命周期 |
| `RuntimeEntity` | 实体数据、变换和运行状态 |
| `RuntimeCamera / RuntimeCameraInput` | 相机配置、视口与输入映射 |
| `RuntimeLighting` | 场景灯光数据 |
| `RuntimeSceneAnimationController` | 推进场景动画 |
| `RuntimePhysics / RuntimeScenePhysics` | Collider 与射线/地面查询 |
| `RuntimeSceneNavigation` | 导航数据和路径查询 |
| `RuntimeDebug` | 调试可视化数据 |

依赖 [GameProjects.Core](../Zhengyan.DigitalWife.GameProjects.Core/README.md) 和 [Mmd.Core](../Zhengyan.DigitalWife.Mmd.Core/README.md)，不直接引用桌面窗口库。

## 如何使用

从 Android 宿主或本仓库的测试了解生命周期调用。宿主负责传入资源解析、每帧时间和输入，并建立对应渲染组件。只创建 RuntimeScene 不会自动创建窗口或显示角色。

桌面 GamePlayer 还保留更完整的脚本运行对象；本库不是“桌面全部 API 的一比一复制”。Android 通过 [Android.Scripting](../Zhengyan.DigitalWife.GamePlayer.Android.Scripting/README.md)把部分能力暴露为脚本契约。

## 构建与验证

仓库根目录：

```text
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Runtime.Core
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests
```

场景 Collider 是查询与交互几何；PMX 模型内部的刚体物理属于 MMD 层。不要把两种用途混成同一个物理世界。

- [项目总览](../../README.md)
- [Android 当前能力](../../docs/android-render-parity.md)
