# PmxParity.Tests：PMX / VMD 特性与姿态对比

这是检查 MMD 共享数值行为的可执行工具。它可以运行自检、分析 PMX 特性，以及比较两份姿态快照。

它不显示模型，也不替代真机渲染。需要看模型画面时用 [MmdDemo](../../samples/Zhengyan.DigitalWife.Samples.MmdDemo/README.md)。

## 运行自检

仓库根目录，安装 .NET 10 SDK：

```text
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests
```

自检涵盖 VMD 插值、快照格式、场景生命周期与时间模式。

## 分析模型

```text
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests -- --analyze "assets/mmd/samples/GameData/Character/Body/Body.pmx"
```

打印顶点、材质、骨骼、Morph、刚体/关节和蒙皮类型，帮助定位模型包含哪些特性。

## 比较姿态快照

下面是命令格式，需提供实际快照文件：

```text
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests -- <expected.pose> <actual.pose> [tolerance]
```

默认容差为 1e-4。快照应由 PmxRuntimeDiagnostics 产生，双方使用相同模型、帧和设置，否则差异不一定来自算法。

数值一致不代表贴图、shader、驱动或显示结果一致；完整平台验证还需截图/画面与原生物理比较。

- [项目总览](../../README.md)
- [Mmd.Core](../../src/Zhengyan.DigitalWife.Mmd.Core/README.md)
- [Android 回归](../Zhengyan.DigitalWife.AndroidRuntime.Tests/README.md)
