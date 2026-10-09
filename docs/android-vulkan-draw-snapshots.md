# Android Vulkan 闪烁与逐材质上传开销（2026-10-08）

用户确认 dlerror 修复后 Vulkan 可以启动，但相邻帧中人物和镜面会交替消失或显示不完整，帧率仍明显低于 GLES。

## 代码中发现的问题

1. `VeldridPmxMainPassRenderer` 构造时把默认帧描述符记为 slot 0，却绑定 `FrameUniformBuffer.NativeResource`（加载当时的 slot）。Android 分帧加载实体，构造可能发生在 slot 1/2。这导致没有阴影贴图的绘制路径在 slot 0 取到其他帧的矩阵。
2. 主材质、描边、阴影和反射视角在同一帧中反复覆盖同一个 uniform buffer。原先用 `CommandList.UpdateBuffer` 串联拷贝和绘制；Veldrid 每次拷贝都会结束当前 render pass，产生大量分段。已有上传后屏障没有明确覆盖下一次覆盖之前的读写依赖。
3. Veldrid 4.9 的 `VkFramebuffer` 外部 subpass dependency 只有 color output 阶段、没有源访问掩码，也不包含 early/late depth tests。通道中断后继续载入深度、或再次使用同一深度附件，需要相应的内存依赖。

这些是代码核查确认的错误；尚未在用户手机上验证各项对实际闪烁的贡献。

## 修复

- `VeldridUniformArena` 按三组帧栅栏管理可复用分页。每个模型/材质/相机绘制分配独立且对齐的参数区间，直到对应提交完成才复用。
- 主材质、描边、地面阴影、阴影深度、纹理平面均通过动态 uniform 偏移绑定快照。主画面与镜面参数不会覆盖彼此；这些路径不再为了上传参数结束 render pass。
- 描述符缓存按实际 buffer 和贴图绑定建立，删除加载时错误绑定的默认 slot 描述符，也避免每帧清空其他 slot 的帧描述符。
- 对仍通过命令列表上传的其他绘制路径，先结束通道并建立覆盖前依赖，再执行拷贝和上传后可见性屏障。
- Android 的私有 Veldrid 构建副本补齐 color/depth 外部依赖；链接前后检查均验证补丁存在，不修改 NuGet 缓存。
- 保留 `WaitForPreviousFrameBeforeUpdate`、三组 frame fence、Compute 栅栏和屏障。没有通过取消等待来提升帧率。

同步依据：[Khronos Vulkan Guide — Synchronization Examples](https://github.com/KhronosGroup/Vulkan-Guide/blob/main/chapters/synchronization_examples.adoc)，其中包括 depth attachment 跨 render pass 复用、WAR/WAW 和 host upload 的示例。

## 验证与手机复测

回归覆盖实际 PMX 主通道与阴影通道、从非零 slot 创建资源、90 连续帧、不同相机/材质快照、深度遮挡、三个 slot 的复用和参数分页扩容。另外保留 180 帧异步 Compute → vertex fetch 测试。

启动日志标记：`startupPatch=3; uniforms=draw-snapshots; depthSync=color-and-depth`。

场景就绪后约每 5 秒输出一次 `Vulkan frame profile`：FPS、CPU 等待提交 fence、更新、录制、提交/呈现耗时，以及录制阶段中阴影、局部阴影、反射的子项。它们是 CPU 测量，不是 GPU 时间戳；`fence`/`present` 含等待时间。场景切换或暂停重置统计。

Android 设备未连接，不能据桌面回归声明红米上的闪烁已消失，也不能给出手机上的 FPS 提升数值。更新播放器后可沿用现有 `.dwgame`，复测 DemoGame01/05/06；若仍卡顿，`Vulkan frame profile` 行可用于进一步区分更新、GPU 等待与呈现瓶颈。

## 本次结果

- 21/21 回归通过；最后恢复加载期管线预热后，相关 Vulkan 3/3 再次通过。
- 补丁版 Veldrid 原生设备创建与压缩 GC 压力校验通过。
- Android Release ARM64 构建、APK v2/v3 签名验证通过；APK 内实际 Veldrid/vk/NativeLibraryLoader 补丁检查通过，AOT 模块数为 0。
- 构建仍有原有 SDL 页面对齐及重复打包的 4 项警告。
- 安装包：`artifacts/android/GamePlayer-arm64-20261008-vulkan-sync-Signed.apk`，57,832,145 字节。
- SHA256：`6798C468726BF8DEE3ED5B3B253DA1DB8E821D2BEB60E7692976AE51E3126883`。
