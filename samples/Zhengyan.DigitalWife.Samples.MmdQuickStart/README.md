# Zhengyan.DigitalWife.Samples.MmdQuickStart

`Zhengyan.DigitalWife.Samples.MmdQuickStart` 是最小可运行的 MMD 示例工程，目标是展示如何基于 `Zhengyan.DigitalWife.Mmd.Game` 快速拉起一个场景。

## 演示内容

- 创建 `Game` 子类
- 初始化 `OrbitCamera`
- 加载角色 PMX、服装 PMX、场景 PMX
- 混合两段 VMD 动作
- 播放背景音乐
- 添加水面和粒子效果

## 运行

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.MmdQuickStart/Zhengyan.DigitalWife.Samples.MmdQuickStart.csproj
```

## 自动复制的资源

构建时会自动把以下内容复制到输出目录：

- `assets/mmd/samples/GameData/` -> `GameData/`
- `assets/mmd/engine/Resources/` -> `Resources/`

## 适合什么场景

- 你要新建自己的 MMD 项目模板。
- 你不需要 ImGui 调试面板，只要最小运行骨架。
- 你想快速理解 `GameOptions`、`OrbitCameraController`、`PmxModelComponent` 和 `MmdCharacterGroup` 的基本组合方式。

更详细的说明见：

- [QuickStart 说明](../../docs/Zhengyan.DigitalWife.Samples.MmdQuickStart.md)
