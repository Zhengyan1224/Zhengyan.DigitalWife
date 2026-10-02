# Android Mono 加载崩溃与 Vulkan 同步修复

## Mono `memory-manager.c:85` 断言

`chunk->pos + size <= GINT_TO_UINT(chunk->size)` 对应 Mono 无锁内存池的边界分配缺陷：
64 位平台的块头对齐后多占 8 字节，旧代码计算页数时没有计入这部分空间。
例如 4 KB 页下申请 4072 字节，新块实际只有 4064 字节可用，会直接 SIGABRT。
官方修复见 [dotnet/runtime#129843](https://github.com/dotnet/runtime/pull/129843)。
这不是普通托管异常，`try/catch` 无法阻止进程退出，也不能仅凭此断言推断游戏资源耗尽内存。

本次核对的 [.NET 10 分支](https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/metadata/memory-manager.c)
仍使用旧算法。Android Player 默认关闭 `RunAOTCompilation` 和 `AndroidEnableProfiledAot`，
Release 使用 Mono JIT，避开存在该缺陷的 AOT 异步展开路径。
这是播放器构建层面的规避，并非对 Mono 原生代码的修复；安装新 APK 后可直接验证现有 dwgame 包。
后续确认 Android 运行时包含上游修复并完成设备回归后，可显式设置 `-p:RunAOTCompilation=true` 验证 AOT。
JIT 可能增加首次执行开销，需要在目标手机上观察加载时间。

## Vulkan 同步

- Android 不再在每次 Present 前调用 `Device.WaitForIdle()`。
- PMX 的 CPU 顶点上传仍使用跨帧共享缓冲区，因此在更新之前等待上一帧的 graphics submission fence。
  这不是无限并行的多帧流水线；仍保留必要的共享数据保护。
- Vulkan Compute 添加前序读取/写入、Compute 写入到 Transfer 读取、Transfer 写入到 CPU 读取的显式屏障。
  Veldrid 的 `CopyBuffer` 已提供 Transfer 写入到 VertexInput 读取的屏障。
  Compute 和绘制提交均使用同一 graphics queue，队列中的屏障也覆盖后续提交。
  稳定运行时不再逐模型阻塞 CPU 等待 Compute；前三次输出校验、CPU 输出路径和槽位复用仍按需等待。
- 材质、阴影、粒子、天空盒、水面和 UI 的动态参数上传后增加 Transfer 到 graphics shader 读取的屏障。
  Veldrid 原有的 VertexInput 屏障不涵盖 uniform / storage buffer 在着色器中的读取，
  这类缺口可能在减少 CPU 等待后表现为参数错帧或闪烁。
- 截图提交始终关联当前 frame fence，避免回读时丢失帧资源复用保护。
- 释放场景资源前等待已提交工作完成，避免后台/前台切换或场景重载释放 GPU 正在使用的资源。

Veldrid 4.9 没有公开的 Compute 屏障 API，兼容层读取其 `VkCommandList.CommandBuffer` 公共属性，
并通过现有 Vulkan 绑定发出屏障；该属性已声明 Android trimming 保留依赖。
升级 Veldrid 时需重新验证此兼容层。

## 验证与设备日志

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --compile-project D:\Projects\CSharp\MMD\GameEditorProjects\DemoGame06
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android -c Release -r android-arm64 -t:SignAndroidPackage -p:AndroidPackageFormats=apk
adb logcat -v threadtime ZhengyanGamePlayer:I mono-rt:E AndroidRuntime:E libc:F '*:S'
```

Vulkan 回归在有 Vulkan 设备时，无窗口执行 180 帧交替姿态的真实 Compute 和离屏绘制，
每帧复用同一 uniform buffer 绘制两种材质并逐帧检查回读图像，
覆盖三个 Compute 槽位反复复用、跨提交的顶点可见性和帧内材质参数可见性。
无 Vulkan 设备时会明确输出 SKIP。
播放器还会在每个加载步骤开始前记录名称、完成后记录耗时，便于定位无法捕获的原生崩溃。

本次桌面验证：Android runtime 回归 13/13 通过（含实际 Vulkan 离屏回归），
DemoGame06 的 3 个绑定脚本全部编译通过。当前未连接 Android 设备。
上述 APK 命令使用单一 `RuntimeIdentifier`；本机 Android SDK 的多 RID 打包路径会在
传递项目依赖中查找未生成的 RID 子目录，单 RID 构建可避开该工具链问题。

ARM64 Release APK 已构建成功（0 错误），位于
`src/Zhengyan.DigitalWife.GamePlayer.Android/bin/Release/net10.0-android/android-arm64/com.zhengyan.digitalwife.gameplayer-Signed.apk`。
包大小 54.8 MiB，APK v2/v3 签名校验通过，包内 AOT 原生程序集数量为 0；
生成配置为 `aotassemblies=false`、`androidaotmode=none`、`androidenableprofiledaot=false`。
构建仍报告已有 SDL 原生库重复及 16 KB 页对齐警告。

真机需验证 DemoGame06 的 GLES/Vulkan 加载、动作切换、连续运行、切换场景及后台恢复。
帧率对比应使用相同场景、分辨率、画质、动作和手机温度；桌面回归不能代替 Redmi/Mali 的性能测量。
