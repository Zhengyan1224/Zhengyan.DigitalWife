# Mmd.Core：共享 PMX / VMD 解析与姿态求值

这是 MMD 的轻量核心程序集，供 Android 和共享场景运行时使用。它不创建窗口，也不实现图形后端。

## 包含的内容

通过链接 [Mmd](../Zhengyan.DigitalWife.Mmd/README.md) 中的源码复用：

- PMX / VMD 解析和二进制读取辅助。
- VMD 插值曲线。
- `IPmxPoseEvaluator / PmxPoseEvaluator`。
- `PmxRuntimeDiagnostics` 特性分析与姿态快照。

这种组织方式让共享代码保持同一个来源。修改链接源文件前请查看本项目 csproj 中的 Compile Link 列表，避免另写一份不同的算法。

## 适合谁

适合开发模型检查工具、共享姿态计算或移动端宿主。普通用户只需[编辑器](../Zhengyan.DigitalWife.GameEditor/README.md)或[播放器](../Zhengyan.DigitalWife.GamePlayer/README.md)，不需要直接运行本库。

本库与完整 Mmd 程序集有同名类型。某些宿主需要同时使用两者时会指定引用别名，不能把两个程序集中的对象直接混传。

## 构建与验证

仓库根目录：

```text
dotnet build src/Zhengyan.DigitalWife.Mmd.Core
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests
```

自检和快照比较验证数值行为；实际 PMX 物理、蒙皮输出和绘制仍要在目标宿主验证。

- [项目总览](../../README.md)
- [MMD 完整库](../Zhengyan.DigitalWife.Mmd/README.md)
