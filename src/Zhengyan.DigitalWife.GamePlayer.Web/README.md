# Zhengyan.DigitalWife Web GamePlayer

这是 Zhengyan.DigitalWife 的浏览器版游戏播放器。它使用 **Blazor WebAssembly + .NET + WebGL2**，把游戏场景、PMX 模型、VMD 动画、预编译 C# 脚本和浏览器平台能力放到网页中运行。

这份文档面向第一次接触本项目的开发者。你不需要先了解 WebAssembly 或 WebGL，按照“准备环境 → 编译 → 启动 → 检查页面”的顺序即可看到示例场景。

## 1. 现在能做什么

当前 Web Player 已经具备以下能力：

- 使用共享运行时读取工程、场景、实体、相机、灯光和动画数据；
- 使用 WebGL2 绘制 PMX 模型、材质、纹理、透明效果、双面材质、边缘和基础阴影；
- 读取 VMD 动画，并复用共享的姿态、Morph、骨骼、IK 和轻量物理回退逻辑；
- 使用 Pointer、Touch、Wheel 和 Keyboard 输入控制相机；桌面端可以使用鼠标、滚轮、WASD 或方向键，移动端可以使用触摸；
- 使用第二个透明 Canvas 绘制 Sprite、GUI 和对话泡；GUI 捕获输入时可以阻止输入继续传给相机；
- 通过 WebGL2 point sprite 绘制基础粒子；高级效果失败时保留基础 PMX 场景；
- 从静态 `wwwroot/web-game` 目录加载游戏资源，并通过 `web.manifest.json` 检查资源路径、大小和 SHA-256；
- 在浏览器内存中打开未加密的 `.dwgame` 文件；页面上的“打开 .dwgame 内存包”文件选择器就是这个入口；
- 使用 IndexedDB 缓存已加载资源和保存 JSON 存档；
- 使用浏览器 Web Audio 播放、停止和统一释放音频；浏览器通常要求用户先点击页面才能解锁音频；
- 提供浏览器 TTS、麦克风录音、JSON HTTP 和 WebSocket 适配器；麦克风需要用户授权；
- 注册 Service Worker，缓存静态运行时文件并支持后续离线能力；
- 只加载导出时生成的 Web 预编译程序集，不在浏览器中运行 Roslyn，也不直接执行 `.csx` 源文件；
- 启动时探测 WebGPU 能力，但当前生产渲染后端仍然是 WebGL2；WebGPU 探测不会阻塞启动。

阶段 0 到阶段 9 的设计和实现记录位于仓库根目录的 `docs/web-runtime-design.md` 和 `docs/web-runtime-phase*.md`。其中阶段 9 是 WebGPU 能力评估与回退协议，**不是完整的 WebGPU 渲染器**。

## 2. 先理解几个名词

如果你是第一次接触这个项目，可以先按下面的方式理解：

| 名词 | 含义 |
| --- | --- |
| Web Player | 运行在浏览器中的游戏播放器，本目录就是它的代码 |
| WebAssembly（WASM） | 让 .NET 代码在浏览器中执行的运行格式 |
| Canvas | 浏览器中的绘图区域，主 Canvas 使用 WebGL2，覆盖层 Canvas 使用 2D 绘图 |
| `wwwroot` | 会被发布到网站根目录的静态文件目录 |
| `wwwroot/web-game` | 默认示例游戏的工程、场景和资源目录 |
| `web.manifest.json` | Web 资源清单，记录资源路径、大小、哈希和脚本信息 |
| `.dwgame` | GameEditor 导出的游戏包。Web 端当前可以在内存中打开未加密包 |
| Web 脚本 DLL | GameEditor 根据 Web 脚本 ABI 预编译出的程序集，位于 `compiled/web` |
| IndexedDB | 浏览器提供的本地数据库，用于资源缓存和 JSON 存档 |

## 3. 目录结构

从仓库根目录看，Web Player 的主要文件如下：

```text
src/Zhengyan.DigitalWife.GamePlayer.Web/
  README.md                         本文档
  Zhengyan.DigitalWife.GamePlayer.Web.csproj
                                    Blazor WebAssembly 项目文件
  Program.cs                         注册 Blazor 和 Web 平台服务
  App.razor                          路由入口
  Pages/
    Home.razor                       主页面、Canvas 和 .dwgame 文件选择器
    NotFound.razor                    404 页面
  Runtime/
    WebRuntimeHost.cs                 Web 帧循环、输入和场景生命周期
    WebProjectLoader.cs               加载工程、场景、VMD、PMX 和脚本
    WebHttpAssetStore.cs              从 HTTP 静态目录读取并校验资源
    WebMemoryAssetStore.cs             内存资源存储
    WebDwGamePackageLoader.cs          在内存中读取未加密 .dwgame
    WebIndexedDbAssetCache.cs          IndexedDB 资源缓存
    WebSaveStore.cs                    IndexedDB JSON 存档
    WebScriptRuntime.cs                Web 预编译脚本加载和生命周期
    WebScriptCompatibility.cs          脚本程序集兼容性检查
    WebRenderFrame.cs                  每帧传给 WebGL2 的渲染数据
    WebAudioService.cs                 Web Audio 适配器
    WebPlatformServices.cs             TTS、麦克风、HTTP 和 WebSocket
    WebGpuCapabilityService.cs         WebGPU 能力探测
  wwwroot/
    index.html                         浏览器入口页
    css/app.css                        页面和 Canvas 样式
    js/web-runtime.js                  输入、帧循环、Canvas 和 JS 与 .NET 桥接
    js/webgl2-renderer.js              WebGL2 shader、网格、材质和粒子
    js/web-storage.js                  IndexedDB 读写
    js/web-audio.js                    音频解锁和播放
    js/web-platform.js                 TTS、麦克风、网络和 Service Worker 桥接
    js/web-gpu.js                      WebGPU 能力探测
    sw.js                              Service Worker
    web-game/                          默认加载的示例游戏
      game.project.json
      web.manifest.json
      scenes/
      assets/
```

## 4. 准备开发环境

### 4.1 必需软件

建议使用 Windows PowerShell，并在仓库根目录执行命令。

1. 安装 .NET 10 SDK。
2. 安装 Node.js。运行项目本身不依赖 Node.js，但执行 JavaScript 语法检查和 smoke test 时需要它。
3. 准备一个支持 WebAssembly 和 WebGL2 的浏览器。优先使用最新版 Chrome 或 Edge。

可以先检查 .NET 和 Node.js 是否可用：

```powershell
dotnet --version
dotnet --info
node --version
npm --version
```

项目目标框架是 `net10.0`。仓库根目录的 `global.json` 允许 SDK 向较新的主版本滚动，但仍建议安装最新 .NET 10 SDK，以免出现目标框架或 WebAssembly 工具链不匹配。

### 4.2 浏览器要求

至少需要：

- WebAssembly；
- WebGL2；
- ES module；
- `fetch`、`createImageBitmap` 等常见浏览器 API。

语音、麦克风和 Service Worker 还有额外条件：

- 麦克风通常只在 `localhost` 或 HTTPS 页面中可用，并且需要用户点击允许；
- TTS 是否可用取决于浏览器和操作系统提供的语音；
- Service Worker 需要通过 HTTP/HTTPS 访问，不能直接双击 `index.html` 使用 `file:///` 打开；
- 音频通常必须在用户点击或触摸之后解锁，自动播放被浏览器拦截是正常行为。

## 5. 第一次编译并运行

### 5.1 进入仓库根目录

打开 PowerShell，执行：

```powershell
cd D:\Projects\CSharp\MMD\Zhengyan.DigitalWife
```

如果你的仓库放在其他位置，请把路径替换成自己的路径。

### 5.2 恢复 NuGet 依赖

第一次构建前执行：

```powershell
dotnet restore src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj
```

这一步会下载 Blazor WebAssembly、WebAssembly 开发服务器以及共享运行时所需的 NuGet 包。以后依赖没有变化时可以直接执行 `dotnet build` 或 `dotnet run`。

### 5.3 编译 Web Player

```powershell
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj -v minimal
```

如果只想检查本次代码是否能编译，并且本机已经有完整的 NuGet 缓存，可以使用：

```powershell
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj --no-restore -p:NuGetAudit=false -v minimal
```

看到 `Build succeeded` 并且错误数为 0，就表示 .NET 部分编译成功。

### 5.4 启动开发服务器

最简单的启动方式：

```powershell
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj
```

默认开发地址是：

```text
http://localhost:5052
```

如果浏览器没有自动打开，手动复制地址到 Chrome 或 Edge。第一次启动可能需要等待 WebAssembly 文件加载。

开发时也可以使用热重载：

```powershell
dotnet watch --project src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj
```

修改 `.razor`、C# 或部分静态资源后，开发服务器会尝试刷新页面。修改 JavaScript 模块后如果浏览器仍使用旧内容，请按 `Ctrl+F5` 强制刷新。

### 5.5 启动后应该看到什么

默认页面会从 `wwwroot/web-game` 加载内置示例。右侧状态面板应该逐步显示：

- 主循环：`requestAnimationFrame`；
- 渲染器：WebGL2 实际渲染器名称；
- 场景：入口场景名称；
- 实体和 PMX 网格数量；
- Canvas 宽高和设备像素比；
- 帧数和最近帧间隔。

在左侧 Canvas 中可以尝试：

- 鼠标按住拖动：旋转相机；
- 鼠标中键或对应拖动方式：平移相机；
- 鼠标滚轮：缩放；
- `W`、`A`、`S`、`D` 或方向键：移动相机；
- 手机或平板：使用触摸操作。

如果场景没有显示，先看右侧“错误”区域，再打开浏览器开发者工具的 Console。资源缺失、哈希不一致、WebGL2 不可用和脚本加载错误都会被转换为诊断信息。

## 6. 使用自己的 Web 游戏资源

### 6.1 最简单的方式：替换 `wwwroot/web-game`

Web Player 默认固定读取相对于网站根目录的 `web-game` 目录。开发时可以把自己的 Web 静态包放到：

```text
src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/web-game/
```

这个目录至少要有：

```text
web-game/
  game.project.json
  web.manifest.json
  scenes/main.scene.json
  assets/...
```

`game.project.json` 的 `defaultScene` 必须指向存在的场景；`web.manifest.json` 的 `entryScene` 必须登记在 `assets` 中。资源路径使用 `/`，不要写 Windows 盘符，例如：

```text
正确：assets/models/character.pmx
错误：D:\Games\MyGame\assets\models\character.pmx
```

每一个实际加载的资源都应登记在 `web.manifest.json`，并填写正确的文件大小和 SHA-256。运行时会先读清单，再按清单读取资源；没有登记的文件不会被当作合法游戏资产。

### 6.2 从 GameEditor 导出

推荐使用 GameEditor 的 `Project → Package / Publish` 导出工程。导出前注意：

1. 保存工程和当前场景；
2. 确认启动场景是你希望 Web Player 打开的场景；
3. 开启 Web 脚本预编译；
4. 检查导出日志中的 `Precompiled Web C# scripts` 和 Web 预编译警告；
5. 将导出的 Web 静态目录复制到 `wwwroot/web-game`，或者部署到网站中的 `web-game/` 目录；
6. 确认 `web.manifest.json`、工程 JSON、场景 JSON、模型、动作、贴图和 `compiled/web/*.dll` 都被复制。

GameEditor 还会在工程目录生成：

```text
compiled/web/
  <脚本名>.dll
  manifest.json
```

Web Player 只加载这些预编译程序集。浏览器不会读取并编译 `.csx`。如果脚本引用了 Android、桌面窗口、Veldrid、Silk.NET、OpenTK 或 `Microsoft.Win32` 等不适合浏览器的程序集，导出日志和 `compiled/web/manifest.json` 会记录兼容性错误。

### 6.3 用测试工具导出 Web 目录

仓库已有 Web 导出回归工具，适合验证一个工程能否生成静态 Web 包：

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests --no-restore -- --export-web `
  D:\Projects\CSharp\MMD\GameEditorProjects\DemoGame04 `
  D:\Temp\DemoGame04-web
```

导出完成后，检查输出目录：

```text
D:\Temp\DemoGame04-web\
  game.project.json
  web.manifest.json
  scenes/
  assets/
  compiled/web/       有 Web 脚本时才会出现
```

要在本地验证它，复制到项目的 `wwwroot/web-game`：

```powershell
Remove-Item -Recurse -Force src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/web-game
Copy-Item -Recurse D:\Temp\DemoGame04-web src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/web-game
dotnet run --project src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj
```

删除或替换示例目录前，请确认里面没有你还需要的本地测试资源。更稳妥的做法是先复制一份 Web Player 项目或使用临时分支。

## 7. 在页面中打开 `.dwgame`

主页面右侧有“打开 `.dwgame` 内存包”选择器。使用步骤：

1. 先启动 Web Player；
2. 点击文件选择器；
3. 选择一个 Web 可用的 `.dwgame` 文件；
4. 等待读取、解包、清单校验和场景加载；
5. 查看状态面板确认场景和 PMX 数量。

当前浏览器端的 `.dwgame` 边界如下：

- 支持在浏览器内存中读取未加密 `.dwgame`；
- 会检查 ZIP 条目路径，拒绝 `..` 等逃逸路径；
- 会根据清单校验资源大小和 SHA-256；
- 加密 `.dwgame` 仍交给 PC/Android 播放器处理；
- 浏览器不会要求用户提供本地 Windows 路径，也不会把包解压到本地文件系统。

如果选择加密包，页面会显示类似“Encrypted `.dwgame` packages cannot be opened directly in the browser”的错误。这是当前设计边界，不是浏览器文件选择器故障。

## 8. 发布和部署

### 8.1 生成 Release 发布文件

在仓库根目录执行：

```powershell
dotnet publish src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj `
  -c Release `
  -o artifacts/web-publish
```

发布后的静态网站通常位于：

```text
artifacts/web-publish/wwwroot/
```

这里面包含 `_framework/`、`index.html`、CSS、JavaScript、`sw.js` 和 `web-game/`。部署时要上传 `wwwroot` 目录中的**全部内容**，不要只上传 `index.html`。

如果只想发布到默认目录，也可以执行：

```powershell
dotnet publish src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj -c Release
```

默认输出通常在项目的 `bin/Release/net10.0/publish/wwwroot/` 下。具体路径以 `dotnet publish` 最后输出的日志为准。

### 8.2 用 Python 临时启动发布目录

如果机器安装了 Python，可以用一个简单的静态 HTTP 服务器检查发布结果：

```powershell
python -m http.server 8080 --directory artifacts/web-publish/wwwroot
```

然后打开：

```text
http://localhost:8080/
```

如果 `python` 命令不存在，可以尝试：

```powershell
py -m http.server 8080 --directory artifacts/web-publish/wwwroot
```

### 8.3 部署到 IIS、Nginx 或其他静态服务器

把 `artifacts/web-publish/wwwroot` 作为网站根目录，并满足以下条件：

1. 服务器能返回 `.wasm`、`.dll`、`.json`、`.js`、`.data`、图片和音频等静态文件；
2. 未找到路径时，前端路由回退到 `index.html`；
3. MIME 类型正确，尤其是 `.wasm`；
4. `web-game/` 目录可以被浏览器通过 HTTP GET 访问；
5. 如果前端和游戏资源来自不同域名，服务器需要正确配置 CORS；
6. 生产环境使用 HTTPS，便于 Service Worker、麦克风和后续网络能力工作。

Blazor WebAssembly 是静态网站。它不需要在服务器上运行 .NET 请求处理程序；服务器主要负责返回静态文件。`dotnet run` 使用的是开发服务器，适合开发和验证，不建议把它当作长期生产部署方案。

### 8.4 Service Worker 缓存更新

当前 Service Worker 的缓存名称写在 `wwwroot/sw.js` 的 `CACHE_NAME` 中：

```javascript
const CACHE_NAME = "zhengyan-web-runtime-v1";
```

发布不兼容的静态文件或希望强制清理旧资源时，修改版本号，例如改为 `v2`，再重新发布。浏览器激活新 Service Worker 后会删除旧版本缓存。

开发阶段如果发现页面一直使用旧 JavaScript：

1. 按 `Ctrl+F5`；
2. 打开开发者工具的 Application/应用 → Service Workers；
3. 点击 Unregister，或者清除该站点的存储；
4. 重新打开页面。

## 9. 编译检查和自动验证

### 9.1 .NET 构建

```powershell
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj `
  --no-restore -p:NuGetAudit=false -v minimal
```

`NuGetAudit=false` 只是不访问 NuGet 漏洞审计服务，不会关闭 C# 编译器检查。网络受限时可能看到 `NU1900` 警告；只要错误数为 0，仍可以继续进行本地构建。

### 9.2 JavaScript 语法检查

```powershell
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-runtime.js
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/webgl2-renderer.js
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-storage.js
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-audio.js
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-platform.js
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-gpu.js
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/sw.js
```

### 9.3 Web 模块 smoke test

```powershell
node tests/web-runtime-smoke.mjs
```

这个测试检查 Web JavaScript 模块能否被加载，并确认关键导出函数存在。它不等同于真实浏览器 GPU 测试。

### 9.4 共享运行时和 PMX/VMD 自测

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests --no-restore
```

它用于检查 PMX/VMD 姿态结果、Web 资源导出和部分共享运行时行为。真实浏览器中的画面、触摸、麦克风权限和显卡驱动仍需要在 Chrome/Edge 等浏览器中手动验证。

## 10. Web 脚本怎么工作

Web 脚本和 PC/Android 脚本不是把同一个桌面程序集直接搬进浏览器，而是使用专门的 Web ABI：

```text
GameEditor 工程中的 .csx
        │
        ├─ PC/Android 预编译
        └─ Web 预编译 → compiled/web/*.dll + manifest.json
                                      │
                                      ▼
                         WebScriptRuntime 加载 DLL
                                      │
                             Start / Update / Event
```

Web 程序集需要实现 `IWebGameScript`，使用 `WebScriptContext` 访问受支持的场景、实体、输入和计时能力。Web Player 会：

1. 读取 Web 脚本清单；
2. 校验 API 版本、程序集哈希和依赖顺序；
3. 拒绝引用不兼容桌面/Android 程序集的脚本；
4. 调用 `Start`、每帧调用 `Update`，并分发事件；
5. 把脚本异常记录到诊断列表，不直接终止渲染循环。

脚本程序集属于受信任游戏代码。兼容性检查可以避免明显的错误引用，但它不是安全沙箱；不要把来源不明的 DLL 直接放进生产游戏包。

## 11. 浏览器平台服务

Web 平台功能通过 C# 服务和 JavaScript 模块封装，游戏脚本不应该直接操作 `window`、`navigator` 或 DOM：

| C# 服务 | 浏览器实现 | 说明 |
| --- | --- | --- |
| `WebSaveStore` | `web-storage.js` / IndexedDB | 保存、读取、删除 JSON 存档 |
| `WebAudioService` | `web-audio.js` | 解锁、播放、停止和释放音频 |
| `WebSpeechService` | `speechSynthesis` | 浏览器 TTS |
| `WebMicrophoneService` | `getUserMedia` + `MediaRecorder` | 录音并返回字节数据 |
| `WebNetworkService` | `fetch` + `WebSocket` | JSON HTTP 和 WebSocket |
| `WebGpuCapabilityService` | `navigator.gpu` | 能力探测，不改变当前 WebGL2 默认后端 |

API 密钥不要写入 `wwwroot`、`web.manifest.json`、脚本 DLL 或公开的 JavaScript。需要 ASR、TTS、LLM 或实时语音时，建议由服务端代理，或者使用短期授权凭据。

## 12. 常见问题

### 页面空白或一直显示加载中

按下面顺序检查：

1. 确认访问的是 `http://localhost:5052`，不是直接打开 `index.html`；
2. 查看浏览器 Console 是否有 `_framework`、`.wasm` 或 JavaScript 模块 404；
3. 确认 `wwwroot/web-game/web.manifest.json` 存在；
4. 确认 manifest 中登记的文件实际存在；
5. 确认浏览器启用了 WebGL2；
6. 删除站点缓存和 Service Worker 后重新加载。

### 显示 `web.manifest.json` 或资源哈希错误

这表示清单中的 `size` 或 `sha256` 与实际文件不同，常见原因是手动修改了 JSON、重新压缩了图片、复制文件不完整，或者复制资源后忘记重新生成 manifest。重新导出 Web 包，不要手动猜哈希值。

### 选择 `.dwgame` 后无法打开

确认包是未加密 Web 包。当前浏览器端不能直接打开由 GameEditor 加密的 `.dwgame`。同时确认包内有 `web.manifest.json`、入口场景和 Web 资源。

### 模型能加载但没有贴图

确认 PMX 使用的贴图也被复制到工程，并且 manifest 中登记了贴图。路径应为工程相对路径，大小写也要与实际文件一致。

### 脚本没有运行

确认：

- GameEditor 导出时启用了 Web 预编译；
- `compiled/web/*.dll` 和脚本 manifest 已复制到 Web 包；
- 脚本实现了 `IWebGameScript`；
- 脚本没有引用桌面、Android 或图形后端程序集；
- 页面状态面板中的脚本诊断没有错误。

### 音频没有声音

先点击或触摸页面，让浏览器解锁音频。再检查音频 URI 是否可访问、浏览器音量是否静音，以及开发者工具中是否有资源 404。自动播放被阻止时，基础场景仍应继续运行。

### 麦克风不可用

麦克风需要 HTTPS 或 localhost，并且必须允许浏览器权限。检查浏览器地址栏的权限图标和系统隐私设置。拒绝麦克风权限不会影响 PMX 场景和普通输入。

### WebGPU 显示不可用

这是允许的结果。当前播放器默认使用 WebGL2，WebGPU 只做能力探测。只要 WebGL2 可用，页面仍可以继续运行。

### 修改代码后浏览器还是旧版本

开发服务器、浏览器 HTTP 缓存和 Service Worker 都可能缓存文件。停止服务后重新启动，按 `Ctrl+F5`，必要时在开发者工具中注销 Service Worker 并清除站点数据。

## 13. 当前边界和后续方向

当前 Web Player 的生产基线是 WebGL2。以下能力已经有接口或回退协议，但还不是完整实现：

- WebGPU 目前只有 `navigator.gpu`、adapter 和 limits 探测，尚无独立 WebGPU renderer；
- Skybox、Water 和 PostProcess 目前主要通过帧状态特性开关和安全回退表达，后续可以增加纹理和 RenderTexture pass；
- 远程 ASR、TTS、LLM 和实时语音需要外部服务端点，项目没有内置长期密钥；
- 真实浏览器的 GPU、触摸、音频权限和麦克风权限需要在带浏览器的机器上验证；
- 加密 `.dwgame` 继续由 PC/Android 播放器处理，Web 端优先使用静态目录或未加密内存包。

相关设计文档：

- [`docs/web-runtime-design.md`](../../docs/web-runtime-design.md)：总设计、边界和阶段恢复入口；
- [`docs/web-runtime-phase3.md`](../../docs/web-runtime-phase3.md)：PMX/VMD、WebGL2 和共享场景运行时；
- [`docs/web-runtime-phase4.md`](../../docs/web-runtime-phase4.md)：Web 包、manifest、内存包和 IndexedDB；
- [`docs/web-runtime-phase5.md`](../../docs/web-runtime-phase5.md)：Web 预编译脚本 ABI；
- [`docs/web-runtime-phase6.md`](../../docs/web-runtime-phase6.md)：输入、存档和基础音频；
- [`docs/web-runtime-phase7.md`](../../docs/web-runtime-phase7.md)：粒子、overlay 和高级渲染开关；
- [`docs/web-runtime-phase8.md`](../../docs/web-runtime-phase8.md)：语音、网络和 Service Worker；
- [`docs/web-runtime-phase9.md`](../../docs/web-runtime-phase9.md)：WebGPU 能力评估。

