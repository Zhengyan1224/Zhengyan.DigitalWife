# PC Vulkan 性能与同步检查（2026-11-09）

PC 和 Android 通过同一个 `VulkanRenderer`、PMX、水面、粒子和资源上传实现渲染。因此前一轮发现的额外阴影采样、无效涟漪计算和 CPU 姿态多次提交，在旧版 PC Vulkan 上也存在；对应的硬件阴影比较、涟漪预计算、姿态批量上传已经直接覆盖 PC，无需另写一套算法。硬件深度格式不支持线性比较或使用自定义 SPIR-V 时保留原有路径。

GamePlayer 和 GameEditor 的构建、发布都会对各自的 Veldrid 输出副本应用修补，空 pass 优化也已覆盖 PC。Android 的 FPS 提升不能直接作为 PC 的提升比例，GPU、窗口尺寸、MSAA 和实际场景负载不同。

## 本次补充

- PC ImGui 仍在 `RenderDrawData` 中通过 staging 更新顶点和索引。它会结束当前场景 render pass，并插入图形/传输屏障。改用独立的顶点、索引和参数快照，按三个帧 slot 的 fence 复用；每个 draw list 独立绑定区间，保留 `IdxOffset`、`VtxOffset`、纹理和裁剪规则。同一帧重复渲染不同尺寸的 UI 也不会覆盖前一次变换。
- 调试线、碰撞体和 gizmo 线条的上传改用相同的顶点与参数快照。增长时保留旧分页，避免丢失此前录制的图形引用；不引入额外的 GPU 等待。
- 原来颜色/深度的外部 subpass 依赖修补仅由 Android 构建调用。现在放入共同的修补代码，PC 同时覆盖颜色输出、早期/晚期深度测试及对应读写访问。
- PC 的原版呈现函数没有等待渲染完成信号量。复用已验证的每 swapchain 图像信号量方案，覆盖普通呈现、交换链重建/重新获取图像和释放。该项用于正确性，不将其当作 FPS 提升保证。

几何分配器为顶点和索引共用分页实现，但保持各自的 buffer usage 和帧 slot，旧的 `FrameVertices` 接口与上传统计保留。没有降低画质或移除 frame fence；`WaitForPreviousFrameBeforeUpdate`、`WaitForIdleAfterPresent` 默认均未启用。PC 项目中切换 Compute 模式的 `WaitForIdle` 属于资源重建操作，不是逐帧等待。

## 验证

- 使用 PC 播放器最终 Veldrid 程序集运行原生 Vulkan 回归，11/11 通过。
- 新增 ImGui/调试线 GPU 回读：18 帧、三个 frame slot、同帧两次 UI 渲染、两个 draw list、超过 64K 顶点的偏移、索引/顶点分页增长和裁剪保留；检查上传前后为同一个 render pass。
- 32 次空目标切换未产生无效 pass；4096 个回读像素验证部分颜色、深度、全清屏及最终清屏。
- 每图像呈现同步的 240 帧异步验证覆盖四个图像编号、重用、重建、释放及错误分支，没有逐帧 idle。
- PC GamePlayer、GameEditor Release 构建通过，并检查两个输出目录中的实际 Veldrid 修补。

维护者可用以下命令核对实际构建副本：

```powershell
dotnet tools/VeldridTransparentSwapchainPatch/bin/Release/net10.0/VeldridTransparentSwapchainPatch.dll --check src/Zhengyan.DigitalWife.GamePlayer/bin/Release/net10.0/Veldrid.dll
dotnet tools/VeldridTransparentSwapchainPatch/bin/Release/net10.0/VeldridTransparentSwapchainPatch.dll --check src/Zhengyan.DigitalWife.GameEditor/bin/Release/net10.0/Veldrid.dll
dotnet tools/VeldridTransparentSwapchainPatch/bin/Release/net10.0/VeldridTransparentSwapchainPatch.dll --verify-present src/Zhengyan.DigitalWife.GamePlayer/bin/Release/net10.0/Veldrid.dll
```

这里报告的是性能路径检查和正确性验证，尚未采集 PC 上同场景优化前后的 FPS，不给出 PC 提升百分比。
