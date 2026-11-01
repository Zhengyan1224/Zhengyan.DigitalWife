# Web Runtime 阶段 0：边界冻结与验证记录

> 状态：**已实现**
>
> 完成日期：2026-10-30
>
> 对应主设计文档：[`docs/web-runtime-design.md`](web-runtime-design.md)
>
> 本阶段只冻结设计边界和验证对象，不创建 Web 宿主、不修改 PC/Android 运行时代码。

## 1. 阶段目标

阶段 0 的目标是把 Web 实现从讨论方案变成可以直接执行的约束：

- 确认第一阶段浏览器范围。
- 确认第一渲染后端。
- 确认脚本发布和加载方式。
- 确认资源发布方式。
- 确认首个最小测试场景和 DemoGame04 集成场景。
- 盘点当前共享 API 和平台依赖。
- 明确第一阶段不做的功能。
- 为阶段 1 的 Web 宿主空壳准备明确入口。

## 2. 已冻结的决策

| 项目 | 阶段 0 决策 | 状态 |
|---|---|---|
| 第一阶段目标平台 | 先支持桌面 Chromium 系浏览器，优先 Chrome/Edge；移动浏览器和 Firefox/Safari 在基线稳定后加入 | 已确定 |
| 第一渲染后端 | WebGL2 | 已确定 |
| 后续渲染后端 | 保留 WebGPU，不能阻塞 WebGL2 | 已确定 |
| 脚本执行方式 | GameEditor 导出时预编译 Web 程序集，运行时加载；Web 端不运行 Roslyn 编译 `.csx` | 已确定 |
| 第一阶段资源发布 | 先支持静态目录和 HTTP 资源；内存 `.dwgame` 包加载作为后续扩展 | 已确定 |
| 第一阶段脚本信任模型 | 脚本程序集视为受信任游戏代码，先不做不可信插件沙箱 | 已确定 |
| 第一阶段物理 | 使用共享托管 fallback；不要求 Bullet 原生 WebAssembly 桥接 | 已确定 |
| 第一阶段语音 | 不要求本地 ASR/TTS；基础音频播放单独实现 | 已确定 |
| 第一阶段 Python | 不支持浏览器本地 Python worker | 已确定 |
| 第一阶段 AI/网络 | 只保留可选接口；需要服务端或用户配置的远程端点 | 已确定 |
| 第一阶段离线能力 | 在线静态资源优先；Service Worker 和离线缓存后续加入 | 已确定 |

## 3. 测试对象

### 3.1 最小渲染测试场景

阶段 1 需要创建一个专门的最小 fixture：

```text
tests/fixtures/web/minimal-scene/
  main.scene.json
  assets/
    models/
      Body/
        Body.pmx
        <最小必要纹理>
  manifest.json
```

最小场景的固定内容：

- 一个 PMX 模型实体。
- 一个主相机。
- 一个方向光或默认环境光。
- 无脚本。
- 无音频。
- 无粒子。
- 无水面、反射和后处理。
- 不依赖 Bullet、OpenCL、Vulkan 或本地语音。

选择这个 fixture 的原因：它只验证 Canvas、WebGL2、资源加载、PMX 顶点/纹理上传、相机和基础绘制，不会把脚本、语音、复杂材质和大场景问题混在一起。

阶段 0 只确定路径和内容，fixture 文件在阶段 1 创建。

### 3.2 DemoGame04 集成测试场景

选定场景：

```text
D:\Projects\CSharp\MMD\GameEditorProjects\DemoGame04\scenes\main.scene.json
```

该场景当前用于验证 Web 运行时接近真实游戏包的加载情况，包含：

- 2 个 PMX 模型实体：`musicclassroom_g` 和 `Body`。
- 2 个 C# 脚本：`scripts/musicclassroom_g.csx` 和 `scripts/Body.csx`。
- 模型边缘和阴影设置。
- `musicclassroom_g` 的樱花粒子配置。
- 水面、反射和水下效果配置。
- Mesh Collider 和导航相关脚本行为。
- 现有 PC/Android 预编译脚本产物。

当前 DemoGame04 的脚本产物已经存在：

```text
compiled/desktop/scripts/Body.dll
compiled/desktop/scripts/musicclassroom_g.dll
compiled/android/scripts/Body.dll
compiled/android/scripts/musicclassroom_g.dll
```

阶段 1 不直接拿完整 DemoGame04 作为第一个渲染调试目标；阶段 3 以后再把它作为集成回归场景。这样可以先验证最小绘制闭环，再验证复杂资源和脚本。

## 4. 当前代码依赖盘点

### 4.1 可以优先复用的共享层

| 项目 | 当前作用 | Web 处理 |
|---|---|---|
| `GamePlayer.Runtime.Core` | `RuntimeScene`、实体、相机、动画和运行时状态 | 优先直接复用 |
| `GameProjects.Core` | 项目模型和场景配置类型 | 优先直接复用 |
| `Mmd.Core` | PMX/VMD 解析、姿态求解、托管 fallback 物理 | 优先直接复用 |
| `RuntimeCameraInput` | 相机输入快照 | Web 输入适配器填充同一数据结构 |
| `IRenderer` | 后端渲染边界 | 保留接口，新增 WebGL2 实现 |
| `IRenderBackendServices` | 后端资源和 pass 工厂 | 继续扩展为浏览器后端 |
| `PmxGpuResources` 相关抽象 | PMX GPU 数据边界 | 复用数据模型，重写 Web GPU 资源实现 |

### 4.2 需要隔离或替换的桌面/移动依赖

| 依赖 | 当前来源 | Web 处理 |
|---|---|---|
| `Silk.NET.Windowing` | 桌面窗口和 OpenGL 上下文 | 替换为 Canvas/JavaScript 宿主 |
| `Silk.NET.OpenGLES` | 原生 OpenGL ES 绑定 | 建立 WebGL2 绑定，不直接复用窗口初始化 |
| `Silk.NET.Vulkan` / Vulkan patches | Vulkan 后端和 Android 修补 | 第一阶段排除 |
| `Silk.NET.OpenAL` / PortAudio | 桌面音频设备 | Web Audio 适配器 |
| `Microsoft.CodeAnalysis.CSharp.Scripting` | 桌面运行时编译 `.csx` | Web 端排除，改为导出时编译 |
| `Evergine.Bullet` | 桌面/Android 物理桥接 | 第一阶段使用托管 fallback |
| `Silk.NET.OpenCL` | GPU/计算辅助路径 | 第一阶段排除 |
| `SherpaOnnx` | 本地 TTS/ASR | 后续 Web/远程适配 |
| `Whisper.net` | 本地 ASR | 后续 Web/远程适配 |
| Python worker | 桌面脚本能力 | 第一阶段不支持 |
| Win32/X11/macOS native | 桌面 Sprite、托盘、透明窗口 | Web 不支持 |
| `File`/`Directory`/临时目录 | PC/Android 资源和包缓存 | 抽象成 HTTP/Memory/IndexedDB 资源层 |
| `ImGui.NET` 桌面控制器 | 桌面 GUI 和调试面板 | 第一阶段只保留运行时必要 GUI，编辑器 UI 后续处理 |

### 4.3 当前脚本依赖结论

`DemoGame04/scripts/Body.csx` 当前使用：

- `System.Numerics`。
- `RuntimeNavigationBakeResult`。
- `PointerClickGesture`。
- 鼠标和触摸输入。
- 屏幕点到射线转换。
- 物理射线检测。
- 导航寻路。

这类能力适合进入 `ScriptApi.Core`，但输入对象需要 Web 实现。

`DemoGame04/scripts/musicclassroom_g.csx` 当前包含：

- GUI 事件。
- 语音调用示例。
- Speech callback。
- 场景更新事件。

第一阶段可以让该脚本被导出器标记为“需要可选 Speech 能力”，但不要求本地 Web TTS 已经可用。脚本加载器需要在能力缺失时给出可读诊断，而不是因为语音 API 不存在而破坏整个场景。

## 5. 阶段 0 验收结果

- [x] 第一阶段目标浏览器范围已经确定。
- [x] WebGL2 已确定为第一渲染后端。
- [x] WebGPU 已确定为后续可选后端。
- [x] Web 脚本只采用导出时预编译程序集。
- [x] 已明确不在第一阶段支持 Web 端 Roslyn、Python、本地 ASR/TTS 和 Bullet 原生桥接。
- [x] 已确定最小 Web 渲染 fixture 的内容和路径。
- [x] 已确定 DemoGame04 集成场景路径。
- [x] 已完成共享层和平台依赖盘点。
- [x] 已明确静态目录优先、内存 `.dwgame` 后续的资源策略。
- [x] 未修改现有 PC/Android 运行时代码。

## 6. 阶段 1 的输入约束

阶段 1 可以直接开始，但范围必须限制在 Web 宿主空壳：

1. 创建 `GamePlayer.Web` 项目。
2. 创建 Canvas 页面和 WebAssembly 启动入口。
3. 建立浏览器日志和错误显示。
4. 建立 `requestAnimationFrame` 主循环。
5. 实现 Canvas resize 和 device pixel ratio 处理。
6. 暂时使用空场景或纯色帧，不接入完整 DemoGame04。
7. 不修改 PC/Android 的默认行为。
8. 阶段 1 完成后，在主设计文档“阶段 1”标题和验收项旁标记“已实现”。

## 7. 阶段 0 变更清单

本阶段新增或修改：

```text
新增：
  docs/web-runtime-phase0.md

更新：
  docs/web-runtime-design.md

未修改：
  src/
  tests/
  samples/
  tools/
  GameEditorProjects/DemoGame04/
```

## 8. 恢复提示

下一次继续实现时，先阅读本文件和主设计文档的阶段 1。不要重新讨论第一阶段是否使用 WebGL2 或是否在 Web 端运行 Roslyn，这两个决策已经冻结。
