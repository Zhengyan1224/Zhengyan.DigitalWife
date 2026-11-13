# PC、Android、Web 脚本 API 对照

本次以 PC 播放器的公开脚本接口为基准，补齐 Android 包装层和 Web 共享运行时。PC 的窗口、托盘和脚本服务实现保持原有路径；共享 PMX 求解器新增的脚本覆盖状态只在调用骨骼、表情控制接口后生效。

## 接口范围

检查包含 50 个公开脚本类型、全局事件变量及数据对象，逐项比较属性、方法重载、参数名称、可选参数和返回类型。记录类型自动生成的成员也参与检查，因此检查项数量不等于游戏 API 数量。

| 入口或类型 | Android | Web | 本次补齐的主要内容 |
| --- | --- | --- | --- |
| 脚本全局变量 | 已对齐 | 已对齐 | 平台、时间、加载、托盘、ASR、LLM 工具与实时语音事件 |
| `Scene` / `RuntimeScene` | 已对齐 | 已对齐 | 实体集合、灯光集合、窗口、运行时设置、性能、RenderTexture |
| `Scene.Camera` | 已对齐 | 已对齐 | 相机属性、视口、屏幕射线、拾取、触摸与实体忽略条件 |
| `Entity` / `Scene.GetEntity(...)` | 已对齐 | 已对齐 | 可见性、播放状态、物理重力、关系绑定、变换和特效参数 |
| 模型动作接口 | 使用原生 PMX 模型 | 使用共享求解器 | 层增删、暂停、时间/帧定位、循环物理重置设置 |
| 模型骨骼与表情接口 | 使用原生 PMX 模型 | 使用共享求解器 | 节点读写、表情覆盖、保存/恢复基础动画、清除覆盖 |
| 模型材质接口 | 使用原生材质 | 更新 Web 材质 | 材质名称、贴图覆盖、`rt:` 绑定与恢复 |
| 自定义 shader 与 uniform | GLES / Vulkan | WebGL2 | PMX 和贴图平面 shader，标量、向量、颜色和清除操作 |
| `Scene.GetGuiControl(...)` | 原生界面 | 浏览器文本控件 | 文本、样式、光标、选区、组合输入与修改事件 |
| `Scene.Bubble` | 原生气泡 | Canvas 气泡 | 管理接口、文本、锚点、边距、换行、字体、颜色和绘制顺序 |
| `Scene.GetSprite(...)` | 原生渲染 | WebGL2 | 属性、布局、命中检测，按实际画布尺寸计算 |
| `Input` | 触摸、键鼠、手柄 | 浏览器输入 | 按键别名、手柄轴/按钮、剪贴板、光标显示与锁定 |
| `Scene.Window` | Activity / 游戏表面 | 网页画布 / Fullscreen API | 尺寸、实际尺寸、标题、可见性、全屏、计时模式、退出 |
| `Scene.Performance` | 已接入帧统计 | 已接入帧统计 | FPS、原始 FPS、帧间隔、总时间、帧数 |
| `Scene.Physics` / `RuntimePhysics` | 共享几何计算 | 共享几何计算 | 射线、胶囊、碰撞体检测和距离计算 |
| `Scene.Navigation` | 已有共享实现 | 已有共享实现 | 烘焙、路径、移动与导航调试信息 |
| `Scene.Debug` | 已接入 | 已接入 | 线、射线与调试信息 |
| `Audio` | 原生音频 | 浏览器音频 | 与 PC 一致的参数名称、一次播放和循环状态 |
| `Entity.Speak(...)` | 离线 TTS | 离线 TTS | 默认声音配置、`RuntimeVoiceOptions`、speakerId 和完成回调 |
| `Scene.Asr` | 离线 ASR | 离线 ASR | 预加载、录音状态、全部停止、输入设备配置和事件时间偏移 |
| `Scene.Llm` | 在线 API | 在线 API | 模型/温度覆盖、流式响应、委托回调、脚本工具和工具返回值 |
| `Scene.RealtimeVoice` | 已有原生服务 | 已有 Web 服务 | 输入配置、命名参数和请求取消接口 |
| `Scene.Network` HTTP | 原生 HTTP | 浏览器 HTTP | PC 请求接口、响应数据对象、超时、取消和请求头 |
| `Scene.Network` TCP / UDP | 原生 socket | 受浏览器限制 | Android 已接入；Web 调用明确抛出平台不支持异常 |
| `Scene.Save` | 应用存储 | IndexedDB 与内存存储 | 文件路径、JSON、文本读写；Web 的完整路径是虚拟存储路径 |
| `Scene.Runtime` | 原生计算与应用权限 | Web 计算后端 | 计算后端状态、计算设置、系统命令及结果对象 |
| 事件、射线、碰撞、导航、网络、声音和 LLM 数据对象 | 已对齐 | 已对齐 | 与上述接口配套的公开属性和成员 |

## 平台行为

### 窗口、嵌入和托盘

`Scene.Window.SetSize(width, height)` 在 Web 调整游戏画布，在 Android 调整游戏显示表面；不会改变手机的物理分辨率。`ActualWidth`、`ActualHeight` 用于读取实际显示尺寸。相对布局使用项目窗口尺寸作为参考。

`SetFullscreen(true)` 在 Android 进入隐藏系统栏的沉浸显示，在 Web 使用浏览器原生全屏。浏览器要求用户操作授权；如果脚本在启动或异步回调中请求全屏，播放器显示进入全屏按钮，用户点击后完成请求。退出全屏不需要再次点击。iframe 需要 `allowfullscreen` 和 `allow="fullscreen"`。

Web 的 `Title` 设置页面标题，`Visible` 控制画布容器，`Resizable` 控制容器的拖动调整尺寸。Android 的 `Resizable` 保存项目窗口偏好，实际应用窗口能否由系统调整取决于设备的多窗口模式；它不会创建桌面式窗口边框。

`Exit()` 和 `Quit()` 在 Android 关闭播放器 Activity，在 Web 停止游戏、语音、录音和渲染。浏览器页面仍由用户或宿主网页管理。

Android 和 Web 没有 PC 系统托盘。`IsTrayMenuEvent` 固定为 `false`，`TrayMenuItemId`、`TrayMenuItemText`、`TrayMenuEventName` 为空字符串，因此同一份脚本中的托盘分支可以正常编译。DemoGame02 原来的导出错误由这些全局变量和 `Quit()` 缺失导致。

画布嵌入和网页桌面精灵的 HTML 示例见 [Web README](../src/Zhengyan.DigitalWife.GamePlayer.Web/README.md)。桌面精灵模式下 Web 背景透明，仅作用于当前网页。

### 输入

Android 光标锁定使用 Android 8.0 / API 26 及以上的 Pointer Capture，需连接可用的鼠标或其他指针设备。低版本调用 `TrySetCursorLocked` 返回实际支持情况。

Web 光标锁定使用 Pointer Lock，首次请求可能显示点击授权提示。`IsCursorLocked` 根据浏览器的实际锁定事件更新。浏览器拒绝请求时不会报告已经锁定。

Web 同步剪贴板读取返回播放器已经收到的复制/粘贴文本；`await Input.ReadClipboardTextAsync()` 请求读取系统剪贴板，需 HTTPS 或 localhost 及浏览器授权。同步写入通过浏览器复制能力尝试完成，`TrySetClipboardText` 返回结果。Android 使用系统剪贴板。

### 网络、命令和计算后端

Android 的 TCP / UDP 与命令执行使用原生 .NET 接口，权限和可访问路径受应用环境约束。

浏览器不能直接打开原生 TCP / UDP socket、执行系统进程或使用 OpenCL / Vulkan。TCP / UDP 和启用不支持的计算后端会抛出 `PlatformNotSupportedException`；系统命令返回 `Success=false`、`ExitCode=-1` 和明确的错误说明。HTTP 仍受浏览器 CORS 规则约束。需要相应能力的 Web 游戏应通过服务器 HTTP / WebSocket 接口实现。

`ComputeBackend` 返回实际后端：Web 为 `WASM` 或 `WebGL2 vertex skinning`，Android 返回模型实际使用的 CPU / Vulkan 计算后端。Android 的 OpenCL 不在当前播放器支持范围内。

### Shader

Web shader 必须符合 GLSL ES 3.00；简单的 `#version 330 core` 会转换版本并补充精度声明。顶点输入为 `a_position`、`a_normal`、`a_uv`，常用变换为 `u_model`、`u_viewProjection`。使用 `SetCustomShaderFloat/Int/Vector*/Color` 设置自定义 uniform。

桌面专属扩展、UBO 布局和 Vulkan SPIR-V 不能自动转换成 Web shader。四路径重载在 Web 使用前两个 GLSL 路径。PMX 自定义 shader 使用 CPU 蒙皮，确保输入位置已经变形；清除 shader 后恢复标准渲染。

### LLM 工具与 Python

C# 工具回调通过 `LlmEventName == "tool_execute"`、`LlmCallbackName`、`LlmToolArgumentsJson` 识别调用，用脚本的 `return` 返回字符串或可序列化对象。工具执行使用独立事件上下文，允许同一个脚本等待 LLM 时处理工具，避免互相等待。

Web Python 使用共享 API 的反射桥，包含同一套平台、时间和事件全局变量。Python 工具回调设置 `ToolResult` 返回结果。Android 保持现有的 C# 脚本运行方式。

Android 和 Web 的唤醒词接口会应用脚本传入的分段时长、语音扩展时长和尾部静音参数。一个监听请求内使用持续麦克风采集，识别分段之间保持采集，切换到问句录音或取消请求时释放原来的采集。

## 验证与复查

完整接口清单可从当前 PC、Android 和 Web 程序集重新生成：

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.GameEditor.Tests -- --script-api-audit temp/script-api-audit.json
dotnet run --project tests/Zhengyan.DigitalWife.GameEditor.Tests -- --test "script API parity"
dotnet run --project tests/Zhengyan.DigitalWife.WebRuntime.Tests
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests
```

编译回归根据 PC API 生成位置参数和命名参数调用、属性读写及重载，分别由 Android、Web 的真实导出编译器检查。运行回归覆盖模型动作与物理、节点/表情/材质、流式 LLM、工具回调重入、HTTP 取消、TCP / UDP 回环、窗口与加载流程。

Android 全套回归中的 Nene 注视测试依赖额外的 `Nene_Yashiro_Matsu_e511.pmx` 模型；本工作区没有该模型，应单独提供该测试资源后复测。Android 原生窗口、鼠标捕获和系统剪贴板仍需在对应设备上验证。
