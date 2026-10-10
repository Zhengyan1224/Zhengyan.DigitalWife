# Android Vulkan 大块黑屏：呈现同步（2026-10-10）

用户在 DemoGame05 导出的 `.dwgame` 中观察到规则矩形区域交替变成背景色。
这次检查确认了呈现路径中的同步缺口；截图形态与未完成的图像被呈现相符，
但目前没有连接 Android 真机，尚不能确认这是手机上所有闪烁的唯一原因。

## 原因与修复

Veldrid 4.9 的 `VkGraphicsDevice.SubmitCommandsCore` 没有发出渲染完成信号量，
`SwapBuffersCore` 的 `VkPresentInfoKHR` 也没有等待信号量。
引擎在下一帧更新前等待 graphics fence，只保护动态资源的更新，不能替代本帧呈现等待。
队列提交的调用顺序同样不能作为渲染完成的保证。

Android 的私有 Veldrid 构建副本现在在呈现前向 graphics queue 加入 signal-only
submission，其信号覆盖队列中之前提交的绘制及解析/拷贝操作；present queue 等待该信号。
正常帧不新增 `WaitForIdle` 或 CPU fence 等待。原有逐次绘制参数快照、Compute 屏障和帧栅栏保留。

信号量按实际交换链图像索引复用，不能按引擎的三个 frame slot 复用。
Veldrid 在获取图像后等待 acquire fence，因此再次获取同一图像时，其上一次 present wait 已退休。
重建、释放交换链时回收信号量，并等待 present queue，覆盖与 graphics queue 不同的情况。
设备丢失后的清理允许 `ErrorDeviceLost`，以便释放资源并进入已有的设备重建路径。

同时修复获取图像的两个分支：

- `SuboptimalKHR` 仍表示成功获取图像，走正常的 acquire fence 等待及重置，继续使用；显式 resize 或 `ErrorOutOfDateKHR` 时重建。
- `ErrorOutOfDateKHR` 后调用 `RecreateAndReacquire`，重新获取并等待新图像。原来只调用 `CreateSwapchain` 就继续渲染。

实现位于 `tools/VeldridAndroidPatch`。运行时代码从 C# 复制到私有 Veldrid 程序集，
不依赖构建工具程序集，不修改 NuGet 缓存。链接前后均检查呈现和资源释放钩子。

依据：[Khronos Vulkan Guide — Swapchain Semaphore Reuse](https://github.com/KhronosGroup/Vulkan-Guide/blob/main/chapters/swapchain_semaphore_reuse.adoc)。

## 验证

`--verify-present <patched Veldrid.dll>` 执行补丁输出的实际 IL，将原生 WSI 调用替换为
故意延迟完成的验证模型，覆盖 240 帧、4 个非顺序图像索引、重复获取、信号量复用、
重建与释放、原生错误及设备丢失清理，也执行实际 `AcquireNextImage` 的成功、suboptimal 和 out-of-date 分支。
该验证不能代替 Android GPU 驱动上的画面与性能复测。

原有 Vulkan 回归 4/4 通过，包含实际 GPU 蒙皮、CPU 回退上传、PMX 参数快照、反射与深度读取。
补丁版原生 Vulkan 设备创建/内存分配在压缩 GC 下的验证通过。

Release ARM64 APK 构建成功（0 错误，保留原有 SDL 页面大小/重复打包的 4 项警告）。
签名 v2/v3 验证通过；从 APK 中实际提取的 Veldrid 通过同步钩子检查及 240 帧验证，
vk/NativeLibraryLoader 的已有补丁也通过检查，AOT 模块为 0。

- APK：`artifacts/android/GamePlayer-arm64-20261010-vulkan-present-Signed.apk`
- 大小：57,836,241 字节。
- SHA256：`8C11777BA87515443F4DFB38667B810810F463979CCB984A87F631B39F95E656`。

## 手机复测

安装修复版播放器，继续使用现有 DemoGame05 `.dwgame`，无需重新导出项目。
启动日志应包含 `startupPatch=4; ...; presentSync=per-image-semaphore`。
检查连续运行、相机移动、暂停恢复与画面尺寸变化，并观察 `Vulkan frame profile` 的帧率。
由于没有连接设备，本次不报告手机 FPS 或断言黑屏已经在真机消失。
