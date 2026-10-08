# Android 对话气泡跟随人物

DemoGame01 的 `Body2.csx` / `Body3.csx` 已调用 `AttachToEntity(Entity.Id, true)`，
并设置世界偏移 `(0, 0.20, 0)`、屏幕偏移 `(0, -16)`。
Android 原来的 Canvas 绘制代码忽略绑定数据，将所有气泡固定为水平居中、`top = Height * 0.56`。

修复后，OpenGL ES 和 Vulkan 都通过同一个 `AndroidSceneGame.TryGetBubblePosition` 接口计算位置：

- 按实体 ID 或名称解析当前场景中的目标，每帧使用模型的变换矩阵。
- 与 PC 一样，用模型局部包围盒顶部中心作为 `useModelTopAnchor=true` 的锚点；关闭该选项时用模型原点。
  这里匹配 PC 的模型顶部语义，不额外绑定某个具体头部骨骼。
- 应用世界偏移，再使用当前主相机的 View/Projection 投影到相机视口，最后应用屏幕偏移。
- 处理 GLES framebuffer 与 Canvas 的 Y 原点差异，支持主相机自定义视口及窗口尺寸变化。
- 默认以气泡底部中心对齐锚点，因此面板位于人物上方。补齐屏幕/世界锚定、Pivot 和相对布局位置接口。
- 目标被删除或位于相机后方、裁剪深度以外时隐藏附着气泡，避免回退为固定位置的 GUI。

Canvas 随已有的每帧 overlay 刷新流程更新位置，定位过程不提交 GPU 工作。
未修改 DemoGame01 脚本；更新播放器即可使用原有 `.dwgame`。

验证包括模型平移/缩放/旋转、原点锚定、相机移动/透视缩放、窗口缩放、视口偏移、
自定义 Pivot、屏幕与世界锚定，以及目标移除/不可投影的情况。
20 项运行时回归测试全部通过，DemoGame01 的 11 个原始脚本使用 Android 引用集合编译成功。
Release ARM64 APK 构建成功，v2/v3 签名验证通过，安装包为
`artifacts/android/GamePlayer-arm64-20261008-bubbles-Signed.apk`，包含此前的 Vulkan 加载器修复。
真实设备上的画面仍需复测。
