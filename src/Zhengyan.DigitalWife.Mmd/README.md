# Mmd：MMD 模型、动作与姿态运行库

这个库处理 PMX 模型、VMD 动作，以及骨骼、Morph、IK、蒙皮和 PMX 物理。它提供模型数据与姿态计算，不会单独打开游戏窗口。

**PMX 是模型，VMD 是动作。** 加载 PMX 时应保留它引用的贴图文件。普通游戏制作者用 [GameEditor](../Zhengyan.DigitalWife.GameEditor/README.md) 导入；想在 C# 中自行渲染角色，可从 [Mmd.Game](../Zhengyan.DigitalWife.Mmd.Game/README.md) 和 [QuickStart](../../samples/Zhengyan.DigitalWife.Samples.MmdQuickStart/README.md)开始。

## 核心能力

- PMX / VMD 解析。
- 模型骨骼、材质、Morph、IK 与附加变换。
- VMD 插值、多动作层与姿态求值。
- BDEF / SDEF / QDEF 等蒙皮路径。
- PMX 刚体与关节物理桥接。
- 可选计算加速和姿态诊断。

## 最小解析示例

在引用本库的 .NET 10 程序中：

```csharp
using Zhengyan.DigitalWife.Mmd;

PmxParsing? model = PmxParsing.ParsingByFile("assets/character.pmx");
if (model is null)
    throw new InvalidOperationException("PMX 无法解析。");
Console.WriteLine("PMX 已读取，下一步交给姿态和渲染层。");
```

这里的模型路径需要自己提供。此示例只读取模型，不显示画面。完整角色运行示例使用 `PmxModel`、`VmdAnimation` 或 `PmxPoseEvaluator`，参考源码与 QuickStart。

## 与 Mmd.Core 的关系

[Mmd.Core](../Zhengyan.DigitalWife.Mmd.Core/README.md)链接部分解析与姿态源码，让 Android / 共享运行时不用引用全部桌面依赖。共享求值器不意味着每个模型和 GPU 都已验证一致，复杂物理与模型特性仍需要比较。

## 构建与检查

仓库根目录：

```text
dotnet build src/Zhengyan.DigitalWife.Mmd
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests -- --analyze "assets/mmd/samples/GameData/Character/Body/Body.pmx"
```

原生 Bullet / 计算库的加载取决于目标平台与构建产物。模型能解析不代表原生物理或图形加速已成功初始化。

- [项目总览](../../README.md)
- [模型姿态对比工具](../../tests/Zhengyan.DigitalWife.PmxParity.Tests/README.md)
