# GameEditor.Tests：编辑器工程与导出回归

这是可执行回归程序，用于检查编辑器视口、工程目录、场景切换、多场景保存和导出行为。它不是游戏编辑器本身。

## 怎么运行

需要 .NET 10 SDK 和相关原生/图形依赖。在仓库根目录：

```text
dotnet run --project tests/Zhengyan.DigitalWife.GameEditor.Tests
```

看到 PASS / FAIL 与最终退出码。当前测试是程序入口，不使用常规测试发现器，因此用 dotnet run，不能把 dotnet test 无失败输出当作这些检查已执行。

## 检查哪些内容

- OpenGL / Vulkan 视口位置与布局。
- 启动工程目录在 UI 初始化前可用。
- 延后场景切换不破坏当前场景。
- 保存/导出所有场景及脚本编译。

## 检查自己的工程导出

命令格式：

```text
dotnet run --project tests/Zhengyan.DigitalWife.GameEditor.Tests -- --export-project <project-copy-directory> <output.dwgame>
```

此模式会保存和导出，应使用工程副本。测试结果不证明包在所有 Android 设备都可运行，仍需目标播放器验证。

- [项目总览](../../README.md)
- [GameEditor](../../src/Zhengyan.DigitalWife.GameEditor/README.md)
- [Android 回归](../Zhengyan.DigitalWife.AndroidRuntime.Tests/README.md)
