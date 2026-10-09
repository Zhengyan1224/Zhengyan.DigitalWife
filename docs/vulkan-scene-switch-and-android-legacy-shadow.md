# Vulkan 场景切换与 Android 旧地面阴影（2026-10-09）

## 用户报告

- 编辑器使用 Intel HD Graphics 520 / Vulkan，切换 DemoGame01 场景时先出现 Compute 校验失败、回退 CPU，随后在 `vkMapMemory → ChunkAllocator → GetFreeStagingBuffer → UploadPose` 中发生原生访问冲突。
- Android 在关闭 `Draw shadow in main pass (legacy)` 后仍显示旧版平面投影阴影。

## 修改

### 帧间切场景

编辑器 ImGui 的按钮处理在 `Draw` 内进行。旧逻辑立即保存、销毁并载入模型，当前场景的命令此时尚未提交。`SwitchScene` 现在仅记录请求，在下一次 `Update` 执行，等待已提交 GPU 工作后再切换场景。对连续请求使用最新目标；无需修改项目格式。

### CPU 上传与内存分配错误

`VeldridGpuBuffer` 的非 Dynamic 缓冲区不再使用 `GraphicsDevice.UpdateBuffer` 的隐式全局暂存分配。每个缓冲区拥有可复用 staging、命令列表和 fence，复用前等待上次上传完成，GPU 队列上补齐覆盖前与绘制读取前的依赖。CPU 回退继续写入原来的顶点缓冲区，Compute 路径也可继续使用该缓冲区。

Veldrid 4.9 的 `ChunkAllocator` 丢弃 `vkAllocateMemory` 和 `vkMapMemory` 的返回值。新增公共构建补丁检查两者：分配失败立即抛出带 VkResult 的托管错误，不调用 map；映射失败先释放分配，再抛出错误。补丁用于编辑器、桌面播放器和 Android 私有构建副本。它能避免未检查失败造成的后续无效访问，但日志本身没有提供原始 VkResult，不能据此断言用户那次一定是显存耗尽。

### 旧阴影

AndroidSceneGame 原先在 `DrawShadowInMainPass == false` 时额外调用 `DrawGroundShadowPass`，导致关闭开关仍绘制旧阴影。移除该调用，由模型自身的 legacy 开关控制；正常 shadow map 的生成与接收仍保留。

模型检查有效 shadow map 时改用后端通用的 `Texture.IsValid`。Vulkan 贴图没有 OpenGL 的非零 TextureId，旧判断会误以为没有 shadow map。

## 验证

- 原生 Vulkan 回归包含先 GPU 蒙皮、再向同一缓冲区连续上传 100012 顶点 CPU 姿态，读取每帧结果验证可见性。
- DemoGame01 实际 Body（52521 顶点）和 MaidOutfit（100012 顶点）三轮加载、姿态更新、校验及释放通过；此检查只读项目，不执行编辑器保存或导入。
- 分配/映射错误注入验证检查不会映射失败分配，并在映射失败时释放内存。
- 编辑器回归覆盖切换请求在更新前保持当前场景、更新时才应用，以及多次往返切换。

实际完整编辑器 UI 切场景和手机阴影画面仍需用户复测；无窗口原生模型测试不等同于完整的编辑器交互复现。

本次执行结果：编辑器回归 5/5、运行时回归 22/22 通过，DemoGame01 两个实际角色模型的 6 次加载周期通过，Vulkan 分配/映射错误注入及 12 次原生设备创建 GC 压力校验通过。编辑器 Debug 构建无警告、无错误。

Android Release ARM64 构建成功，保留原有 SDL 的 4 项打包警告。实际 APK 内 Veldrid 内存检查、同步补丁和两个加载器补丁均通过检查，签名 v2/v3 验证通过，AOT 模块为 0。

- APK：`artifacts/android/GamePlayer-arm64-20261009-scene-shadow-Signed.apk`
- 大小：57,832,145 字节。
- SHA256：`98079BFEC8F0B8EA1CC4A6248E5F88F59A8C484404DD152B618A99257B3EBA3D`。
