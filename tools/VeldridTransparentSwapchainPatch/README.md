# VeldridTransparentSwapchainPatch：桌面 Vulkan 透明窗口修补

这是 GameEditor / GamePlayer 构建时自动调用的工具，为当前 Veldrid 构建副本补充透明 Swapchain 和相关内存安全处理。用于桌面精灵模式及底层兼容维护。

普通用户只需构建播放器，不需要单独执行它。它不负责游戏场景编辑，也不将所有窗口强制变为透明。

## 原理与边界

透明窗口需要图形 Swapchain、系统合成器和输入区域共同支持。修补只解决其中的后端选择问题；Linux Wayland 原生点击穿透并未因此自动接入。

工具对指定 DLL 原地写入，因此只应传构建输出副本，不要修改 NuGet 缓存或未知版本的第三方文件。支持范围以当前依赖与回归验证为准。

## 开发入口

仓库根目录：

```text
dotnet build tools/VeldridTransparentSwapchainPatch
```

维护者手动调用格式：

```text
dotnet run --project tools/VeldridTransparentSwapchainPatch -- <build-copy-of-Veldrid.dll>
```

常规项目构建会自动修补输出目录和发布目录。修改工具后验证普通窗口、桌面精灵、OpenGL/Vulkan 和目标平台的行为。

- [项目总览](../../README.md)
- [渲染后端](../../docs/rendering-backends.md)
- [PC GamePlayer](../../src/Zhengyan.DigitalWife.GamePlayer/README.md)
