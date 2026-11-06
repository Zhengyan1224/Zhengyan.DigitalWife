# AndroidRuntime.Tests：共享逻辑与 Android 适配回归

这个桌面可执行程序验证 Android 与共享运行层的行为。部分 Android 原生边界使用测试替身，部分图形检查使用本机 GPU。

**在 PC 上通过，不代表手机的麦克风、扬声器、IME、驱动和实际触摸已经验证。** APK 仍需要真机测试。

## 运行

需要 .NET 10 SDK，完整套件的部分检查还需要 Python、原生库和可用图形设备。仓库根目录：

```text
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests
```

当前是可执行回归程序，不是 dotnet test 的常规测试项目。

按名称过滤：

```text
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "Android local speech"
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "Script touch"
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "Script platform"
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "Android navigation"
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "Android script"
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "MMD IK"
```

## 覆盖范围

- 场景加载、PMX 动作与物理时间。
- IK 单轴、多轴限位和 PMX 坐标转换；Nene + Lamb 全程整数帧、半帧的视线与 Web 姿态对照（需要本地 Nene 模型，见[修复记录](../../docs/mmd-ik-rotation-limits.md)）。
- Vulkan 帧资源、Compute、上传和绘制快照。
- 预编译 C# API、脚本兼容、工程与加密/分包。
- LLM、工具、Skills、记忆、取消和诊断。
- RealtimeVoice 服务协议与播放完成。
- 本地 Sherpa ASR / TTS、唤醒词、静音期限、取消与旧回调隔离。
- GUI 按钮目标与事件顺序、气泡布局、Touch。
- 对照 PC 的角色朝向、三角形采样、台阶连通、连续寻路和导航诊断。
- C# / Python 平台标识和文档示例编译。

## Release 裁剪检查

先构建 Android Release，再传裁剪输出目录：

```text
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --verify-linked-script-apis src/Zhengyan.DigitalWife.GamePlayer.Android/obj/Release/net10.0-android/android-arm64/linked
```

这能发现动态脚本需要的成员被裁剪，但不会启动 APK。

## 其它诊断入口

| 参数格式 | 用途 |
| --- | --- |
| --scene-script-project <directory-or-dwgame> | 只读 DemoGame01 的 Body1 源码及 Android 预编译脚本，各重建人物 5 次，验证站立/等待动作初始化与播放；使用内存动画和禁用语音的替身，不启动 Android 渲染器 |
| --compile-project <directory> | 用 Android 实际元数据入口编译工程脚本 |
| --navigation-project <directory> | 只读 DemoGame04 的模型与脚本，在临时 dwgame 包中对照 PC 验证连续触摸寻路和转向 |
| --local-speech-project <directory> | 读取真实工程 Sherpa 模型，检查本地 TTS、ASR 与唤醒（PC native 推理，不请求 LLM） |
| --physics-project <directory> <motion.vmd> | 使用实际工程比较物理时间行为 |
| --skinning-project <directory> <motion.vmd> | 比较实际工程蒙皮与姿态 |
| --vulkan-project-models <directory> | 检查实际工程模型的 Vulkan 运行 |

文件和目录参数必须实际存在。诊断资源开销可能较大，先用小工程定位。

- [项目总览](../../README.md)
- [Android 构建与真机部署](../../src/Zhengyan.DigitalWife.GamePlayer.Android/README.md)
- [当前实现与验证范围](../../docs/android-render-parity.md)
