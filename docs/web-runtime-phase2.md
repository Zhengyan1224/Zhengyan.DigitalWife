# Web Runtime 阶段 2：WebGL2 最小渲染器

> 状态：**已实现**
>
> 完成日期：2026-10-31
>
> 对应设计文档：`docs/web-runtime-design.md` 的阶段 2

## 1. 阶段目标

阶段 2 在阶段 1 的 Canvas、DPI/resize 同步和 `requestAnimationFrame` 宿主之上，接入一个可运行的 WebGL2 最小渲染器。它用于验证浏览器 GPU 上下文、着色器、顶点缓冲、纹理采样、viewport 更新以及资源释放的完整生命周期。

本阶段只验证渲染后端的最小闭环，不加载 `.dwgame`、PMX、VMD、场景文件或预编译脚本，也不改变现有 PC/Android 渲染器和运行时行为。

## 2. 实现边界

### 已实现

- 从 Canvas 创建 WebGL2 context，使用关闭抗锯齿和深度缓冲的最小配置。
- 编译 WebGL2 GLSL ES 3.00 顶点和片元着色器，并在失败时报告：
  - `webgl2-renderer.js` 模块名；
  - 顶点或片元 shader 阶段；
  - 驱动返回的日志；
  - 带行号的完整 shader 源码。
- 创建并使用 VAO、顶点 buffer、2×2 RGBA texture 和 sampler 状态。
- 创建独立 sampler、最小 framebuffer 和 scissor 状态接口；示例四边形绘制到 Canvas 默认 framebuffer。
- 绘制一个使用内置纹理的 `TRIANGLE_STRIP` 四边形。
- 在 Canvas 尺寸或设备像素比变化后重新设置 WebGL viewport。
- 在运行时停止时删除 VAO、buffer、texture、sampler、framebuffer 和 program，移除 context lost 事件监听器。
- 将 WebGL2 版本、驱动 renderer 名称和 GLSL 版本返回给 Blazor 状态面板。
- 浏览器不支持 WebGL2、shader 编译失败或上下文丢失时，把错误传递到 .NET 状态面板。

### 有意延后的内容

- Web 版完整 `IRenderer`/`IRendererFactory` 适配。
- 材质系统、透明排序和多 pass 场景渲染；Framebuffer 和 scissor 已提供最小生命周期，完整材质与深度功能顺延到 PMX 阶段。
- 图片资产下载、缓存和 `.dwgame` 资源清单。
- PMX/VMD 加载、场景节点、相机和脚本。
- WebGPU 后端。

当前 WebGL2 原型使用独立的 Web API，不直接实现桌面图形接口。这是因为现有 `IRenderer` 依赖 `IWindow`、Silk.NET 和桌面 render target 类型；强行在阶段 2 引用这些类型会把浏览器上下文和原生窗口生命周期混在一起。后续建立跨平台图形抽象后，再提供正式的 `IRendererFactory` 适配层。

## 3. 代码位置

- `src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/webgl2-renderer.js`
  - WebGL2 context、shader、VAO、buffer、texture 和绘制命令。
- `src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-runtime.js`
  - 将 WebGL2 renderer 挂到已有帧循环；处理 resize、stop、错误转发。
- `src/Zhengyan.DigitalWife.GamePlayer.Web/Runtime/WebRuntimeHost.cs`
  - 启动时调用 `enableWebGl2`，接收 renderer 信息并更新 .NET 状态。
- `src/Zhengyan.DigitalWife.GamePlayer.Web/Runtime/WebGl2Info.cs`
  - WebGL2 能力信息的互操作模型。
- `src/Zhengyan.DigitalWife.GamePlayer.Web/Runtime/WebRuntimeSnapshot.cs`
  - 增加 `RendererReady` 和 `RendererName` 状态。
- `src/Zhengyan.DigitalWife.GamePlayer.Web/Pages/Home.razor`
  - 显示 renderer 名称、Canvas 尺寸和初始化错误；renderer 就绪后隐藏占位层。

## 4. 运行时流程

1. Blazor 首次渲染 Canvas。
2. `WebRuntimeHost.StartAsync` 导入 `web-runtime.js` 并启动 resize observer 与帧循环。
3. `web-runtime.js` 调用 `enableWebGl2`。
4. `webgl2-renderer.js` 创建 WebGL2 context，编译 shader，上传内置纹理并创建顶点资源。
5. 每个 `requestAnimationFrame` 先执行 WebGL2 绘制，再异步通知 .NET 更新帧计数。
6. resize observer 更新 Canvas 像素尺寸，并调用 renderer 的 `resize` 设置 viewport。
7. 页面销毁或宿主停止时，删除 GPU 资源和事件监听器。

渲染器不通过每帧 .NET JS interop 绘图；绘制发生在 JavaScript 帧循环中，避免把 GPU 命令拆成每帧的 Blazor 调用。

## 5. 验证记录

已执行：

```text
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj --no-restore -v minimal
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-runtime.js
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/webgl2-renderer.js
```

结果：Web 项目构建通过，0 个警告、0 个错误；两个 JavaScript 模块语法检查通过。

开发服务器烟囱检查使用 `http://127.0.0.1:5053/`，返回 HTTP 200；发布入口能引用 `web-runtime.js` 和 `webgl2-renderer.js`。当前执行环境没有 Chrome、Edge、Chromium 或 Firefox 可执行文件，因此尚未完成真实浏览器 GPU 画面截图、resize 视觉检查和 shader 错误注入检查。这些检查需要在带 WebGL2 浏览器的环境中执行。

完整解决方案构建仍可能被仓库中与本阶段无关的项目缺少 `obj/project.assets.json` 阻断；阶段 2 的 Web 项目及其共享运行时核心已单独构建通过。

## 6. 下一阶段输入

阶段 3 应从当前代码继续，先建立 Web 资源加载和静态目录约定，再接入最小 PMX/VMD 场景。开始前需要保持以下边界：

- 不重新创建 Web 宿主和 WebGL2 context 生命周期。
- 不修改 PC/Android 后端行为。
- 先决定资源 URL、缓存、哈希校验和缺失资源诊断格式。
- 先用一个最小模型或 fixture 验证顶点/索引/纹理上传，再扩展完整场景。
- 真实浏览器 GPU smoke test 应在阶段 3 之前补齐，至少覆盖初始化、resize、停止释放和 WebGL2 不可用错误。
