# Windows Vulkan 桌面精灵动态轮廓同步

DemoGame08 在 Windows Vulkan 桌面精灵中，人物、相机或对话气泡移动时会出现边缘缺口和类似拖影的旧轮廓，停止后逐渐恢复。

## 原因

`VulkanRenderer.TryReadBackBufferRgba` 的异步 staging 环返回前几帧已完成的画面。`DesktopSpriteHitTestComponent` 原先将它交给 Windows 的 `SetWindowRgn`，而这个区域同时限制窗口显示和鼠标命中。当前画面被旧位置的轮廓裁切，因此表现为动态缺口、锯齿和拖影，涉及所有画面内容。

Linux X11 分支已经仅更新 `ShapeInput`，保持可视窗口矩形稳定。异步 alpha 数据只参与点击穿透，显示轮廓由当前画面与合成器处理，因此不会通过同一机制裁切新的画面。Windows OpenGL 的读回同步返回当前画面，也没有这一帧差。

## 修改范围

仅当桌面精灵点击穿透启用，且平台为 Windows、后端为 Vulkan 时，排队读取当前帧。正常帧提交包含 MSAA resolve 和 staging copy，等该帧原有 fence 完成后才读取 alpha 并更新窗口区域，然后呈现这个画面。呈现后使用 `DwmFlush` 等待 Windows 合成提交，避免后续帧的区域更新超前排队中的画面。

- 保留原本的帧环 fence，未替换上传资源的提交 fence。
- 保留 MSAA 及 RGBA/BGRA、行方向转换。
- Windows OpenGL、Linux Vulkan/OpenGL、普通 Vulkan 和关闭点击穿透的路径保持原样。
- 仅此分支在呈现前等待 GPU 完成，减少了该分支的 CPU/GPU 流水线并行，以确保视觉轮廓正确。

## 验证

在 Intel HD Graphics 520 上使用隐藏原生窗口测试：

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.GameEditor.Tests -- --test "Windows Vulkan sprite"
dotnet run --project tests/Zhengyan.DigitalWife.GameEditor.Tests -- --test "Windows sprite other"
```

覆盖快速跳变的图形和气泡位置、旧轮廓消失、当前可见像素未被窗口区域裁掉、4×/1×/4× MSAA 半透明覆盖、窗口尺寸变化；并验证普通 Vulkan 仍返回已完成的上一帧，Windows OpenGL 仍在 Draw 阶段更新当前区域。
