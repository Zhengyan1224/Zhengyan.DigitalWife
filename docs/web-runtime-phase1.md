# Web Runtime 阶段 1：Web 宿主空壳

> 状态：**已实现**
>
> 完成日期：2026-10-30
>
> 对应主设计文档：[`docs/web-runtime-design.md`](web-runtime-design.md)
>
> 阶段范围：只建立浏览器宿主、Canvas 生命周期和帧循环，不接入 WebGL2、游戏资源或 DemoGame04。

## 1. 实现内容

新增项目：

```text
src/Zhengyan.DigitalWife.GamePlayer.Web/
```

项目使用 .NET 10 Blazor WebAssembly 独立应用 SDK，并引用：

```text
src/Zhengyan.DigitalWife.GamePlayer.Runtime.Core/
```

这个引用用于验证共享运行时核心可以进入 WebAssembly 项目。阶段 1 没有加载场景或调用共享核心的文件系统逻辑。

主要文件：

```text
Program.cs
  Blazor WebAssembly 启动、RootComponent 和 WebRuntimeHost 注册

Runtime/WebRuntimeHost.cs
  JS 模块生命周期、Canvas 尺寸回调、帧回调、错误状态和资源释放

Runtime/WebRuntimeSnapshot.cs
  阶段 1 的只读运行时状态

wwwroot/js/web-runtime.js
  ResizeObserver、window resize、requestAnimationFrame、浏览器异常和日志桥接

Pages/Home.razor
  Canvas 页面、状态面板和宿主启动/停止生命周期

wwwroot/css/app.css
  空 Canvas 页面和状态面板样式
```

## 2. 运行时流程

```text
Blazor 页面首次渲染
  -> Home.razor 获取 Canvas ElementReference
  -> WebRuntimeHost.StartAsync
  -> import wwwroot/js/web-runtime.js
  -> 创建 DotNetObjectReference
  -> 注册 ResizeObserver 和 window resize
  -> 同步 Canvas width/height 与 devicePixelRatio
  -> requestAnimationFrame(frame)
  -> JS 调用 WebRuntimeHost.OnAnimationFrame
  -> 更新帧数、delta、Canvas 尺寸和状态面板
```

阶段 1 的 JavaScript 模块没有调用 `canvas.getContext()`。这样可以把 WebGL2 上下文创建完整留给阶段 2 的 `WebGl2Renderer`，避免宿主空壳提前占用 Canvas 的绘图上下文。

## 3. 生命周期和释放规则

- `Home.razor` 第一次渲染后只启动一次宿主。
- `WebRuntimeHost` 防止重复启动。
- 页面销毁时先停止 `requestAnimationFrame`，再释放 `DotNetObjectReference` 和 JS 模块。
- 浏览器导航或断开时捕获 `JSDisconnectedException`。
- .NET 回调失败时停止后续帧调度，避免不断产生异常。
- ResizeObserver 不可用时仍保留 window resize 监听。

## 4. 状态面板

当前页面显示：

- Web 宿主是否运行。
- `requestAnimationFrame` 帧数。
- Canvas 物理像素宽度和高度。
- `devicePixelRatio`。
- 最近一次帧间隔。
- 启动错误或浏览器未处理异常。

状态面板只用于阶段 1 验证。阶段 2 以后可以把这些信息迁移到统一的运行时诊断系统。

## 5. 解决方案和 NuGet 变更

Web 项目已加入：

```text
Zhengyan.DigitalWife.sln
Zhengyan.DigitalWife.slnx
```

由于仓库启用了中央 NuGet 版本管理，新增的 Blazor 包版本放在 `Directory.Packages.props`：

```text
Microsoft.AspNetCore.Components.WebAssembly              10.0.2
Microsoft.AspNetCore.Components.WebAssembly.DevServer    10.0.2
```

项目文件只保留不带版本的 `PackageReference`，符合仓库现有 CPM 规则。

## 6. 验证记录

### 6.1 Web 项目构建

执行：

```text
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj -v minimal
```

结果：

```text
0 warnings
0 errors
Blazor output generated
```

### 6.2 JavaScript 语法检查

执行：

```text
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-runtime.js
```

结果：通过。

### 6.3 开发服务器静态入口

使用 `dotnet run` 启动后请求 `/`，结果：

- HTTP 状态码：200。
- 返回 `lang="zh-CN"` 和 Web GamePlayer 页面标题。
- 返回 Blazor WebAssembly 启动脚本。
- 返回 `js/web-runtime.js` 的 import map 和完整性映射。
- 返回阶段 1 的 CSS。

### 6.4 当前未完成的验证

执行环境没有 Chrome、Edge、Firefox 或其他浏览器可执行文件，因此尚未执行真实浏览器交互验证。以下项目需要在有浏览器的环境补做：

- 页面首次加载后 Canvas 是否显示。
- `requestAnimationFrame` 帧数是否持续增加。
- 调整浏览器窗口后 Canvas 物理像素尺寸是否更新。
- 浏览器异常是否显示在状态面板。
- 页面导航后帧循环和 ResizeObserver 是否释放。

这不阻塞阶段 1 的代码构建，但阶段 2 开始前必须补上至少一个 Chromium 自动化烟囱测试。

### 6.5 整体解决方案构建边界

单独 Web 项目构建已经通过。使用 `dotnet build Zhengyan.DigitalWife.slnx --no-restore` 验证整体解决方案时，仓库中若干既有 sample/tool/test 项目因没有 `obj/project.assets.json` 报 `NETSDK1004`；Web 项目本身及其共享核心项目成功构建。

后续若要把整体解决方案作为 CI 检查，应先为整个解决方案执行一次还原，再区分已有项目还原问题与 Web 运行时问题。

## 7. 阶段 2 输入

阶段 2 从当前宿主直接继续：

1. 在 `wwwroot/js/web-runtime.js` 增加 Canvas WebGL2 context 创建和销毁接口，或建立独立 WebGL2 bridge 模块。
2. 新增 `WebGl2Renderer`，实现 `IRenderer` 的最小清理、viewport、present 和 resize 能力。
3. 不在 `Home.razor` 中加入 GPU 资源细节，把渲染器生命周期留给 Web runtime 服务。
4. 先渲染测试 quad，再接入 PMX。
5. 阶段 2 完成后，在主设计文档的阶段 2 标题和验收项旁添加“已实现”。
