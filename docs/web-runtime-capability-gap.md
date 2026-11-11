# Web 与 PC/Android 能力差距审计

更新时间：2026-11-11

## 浏览器离线续作

已接入本地 Wllama/GGUF、Sherpa ONNX TTS/ASR、Pyodide Python 与版本化离线缓存，默认模式为 offline。原有浏览器语音与远程 Realtime 适配保留在 connected 模式。当前支持的模型、缓存方式及浏览器沙箱边界见 [离线运行说明](web-offline-runtime.md)；下方语音适配说明记录此前的 connected 模式，不再代表当前默认链路。

## 2026-11-11 场景与渲染补齐

本次继续完成中断会话中的工作，使用 `artifacts/web-player/wwwroot/web-game` 的 DemoGame01 对照 PC。

| 能力 | 当前状态 | 验证与边界 |
| --- | --- | --- |
| 背景与前景 Sprite | 已实现 | 负 DrawOrder 在天空之后、几何之前绘制；前景与 RenderTexture 按统一顺序合成；源矩形、旋转、透明度和图像方向。 |
| PMX 身体/服装关联 | 已实现 | 按骨骼名称关联，解析依赖顺序，绑定实体变换；149 个实际服装骨骼通过测试；自身姿态变化也会重新绑定。 |
| Toon/Sphere 与材质 Morph | 已实现 | 公共 Toon、模型 Toon/Sphere、纹理乘加、漫反射/环境/高光/描边状态；Sphere 使用相机空间法线；沿用 PC 的 Sphere 模式 1/2 行为。 |
| 天空盒 | 已实现 | 全景天空、场景颜色与曝光、自定义路径和引擎默认天空，主相机与反射相机均支持。 |
| 水面 | 已实现 | 与 PC 相同的 Gerstner 公式、细分网格、动态法线、Fresnel、天空与平面反射、交互波纹。 |
| 水下 | 已实现 | 按水面边界与相机深度启用；独立深度纹理、雾、吸收、色调、扭曲、焦散、气泡、晕影；离水恢复与多相机隔离。 |
| 粒子 | 已实现主要路径 | 实际数量与寿命、加速度、纹理四边形、速度朝向、颜色/大小渐变、混合、碰撞、水面交互、反射与投影；粒子碰撞法线目前由移动方向近似。 |
| 多相机与 RenderTexture | 已实现 | 单独相机目标与反射资源、Viewport 合成、rt: 名称/ID 引用、平面/Sprite/粒子采样、every_frame/fixed_rate/on_demand 首次刷新、尺寸变化与删除释放。 |
| 动态实体 | 已实现主要路径 | 平面/水面新增、修改、移除同步 GPU；PMX 新增、模型路径修改与移除也有资源加载/释放链路；已登记 VMD 可由脚本按路径使用。 |
| 碰撞/导航/调试 | 已接入 | PMX/平面世界空间 Mesh collider、骨骼节点、共享导航与调试线绘制；导航仍使用现有共享实现。 |
| 触摸与 GUI | 已实现主要路径 | 触点、帧间有序转移、DPR、取消、双指平移/缩放、实际 Viewport 射线、文本编辑、圆角/对齐、按下/释放/点击；Sprite entered/exited，离开按下位置的释放不会误点击；右键菜单按窗口/实体/GUI/Sprite 目标匹配与分发。 |
| ASR/唤醒词 | 已实现浏览器适配 | SpeechRecognition 的部分/最终文本、停止、取消、唤醒词、权限错误与脚本事件；使用浏览器提供的识别能力，依赖浏览器支持及其语音服务。 |
| LLM/工具/记忆 | 已实现 | HTTP/SSE、分片工具调用、工具轮次、回调、错误/取消；内存同步存档与 IndexedDB 持久化；memory_* 工具及已打包的 skills 文档读取。 |
| Realtime Voice | 已实现浏览器适配 | WebSocket 会话、文本响应、PCM16 输入重采样、转录/响应回调、PCM 输出、HTTP TTS、取消和会话重置；受服务端与浏览器权限约束。需要 HTTP 头认证的 WebSocket 服务应使用浏览器兼容中继或配置子协议。 |
| TTS 口型 | 已实现 | 复用引擎汉字/kana/元音字典与最长匹配；SpeechSynthesis 边界驱动元音 Morph；结束/取消/失败恢复原手动 Morph 状态。时间精度受浏览器边界事件限制。 |
| 异步 Web 脚本 | 已实现 | Start/Update/Event 异步契约兼容旧 DLL；等待中的 Update 不重入，事件按顺序执行；预编译支持 await 和不同参数数量的局部函数重载。 |
| 加密游戏包 | 已实现播放器路径 | Web Crypto 对应原生 PBKDF2-SHA256 / AES-256-GCM 分块格式；密码输入、nonce 顺序、认证与明文 SHA-256 校验；真实 PC 多块包、错误密码和损坏回归通过。 |
| PC/Android 编译契约 | 已处理 | TouchPoint/PointerClickGesture 放在 Abstractions，原 Mmd.Game 类型转发，避免重复类型；Android 动作层明确类型别名。 |

不能据此将“所有 PC 功能”标为完成。本地 ONNX 语音模型、原生进程/文件命令技能、TCP/UDP、Python、原生自定义 shader 与部分粒子行为仍存在差距或需要平台服务。浏览器 TTS/ASR 的声音、离线能力与时间精度也不等同于原生端。桌面精灵按本次要求排除。

实际测试项目的 11 个 Web 脚本已重新编译，清单登记 16 个场景绑定，包括三个语音/对话场景。C# 服务回归覆盖存档恢复、路径隔离、中文 SSE、分片工具、HTTP 错误、取消、GUI 拾取和异步脚本事件顺序；JavaScript 回归还覆盖 ASR、Realtime、TTS 边界和取消。

Release AOT 浏览器回归已通过：主场景的 874×491 主视口和第二相机、跳舞时人物/服装同时更新、两个场景切换、录音按钮按下/释放、ASR 文本进入输入框、流式 LLM 回复进入气泡，以及加密包实际文件上传/密码解密/解包。语音与模型端点使用受控替代，未访问实际麦克风或外部模型服务；浏览器异常为 0。GPU transform feedback 检查 113 个实际顶点、149 根骨骼、3 帧，最大 CPU/GPU 误差 0.00000262，WebGL 错误为 0。

续作还修复了两个跨层问题：GUI 未绑定目标时与 PC 一样优先选择被服装关联的人物；每帧多模型姿态数组显式作为单个 JS 参数传递，避免只更新身体、服装停留在旧姿态。`WebScriptTrimming.xml` 保留运行时动态 DLL 使用的时间、字符串、集合等 BCL 契约，防止 AOT 发布后出现缺失方法。引擎贴图/口型字典也参与模块版本摘要。

验证：C# WebRuntime（含 `--parity`）、JavaScript 渲染/输入/Bullet/音频/缓存测试、真实 Edge WebGL2 像素测试。像素验证覆盖背景遮挡、贴图方向、天空、第二相机、水下退出、RenderTexture 定时刷新及删除、镜面 MSAA。历史性能数据不能替代本次场景的实测帧率。

以下为 2026-11-03 的基线审计，状态以本节为准。

这份清单把 Web Player 与 PC GamePlayer、Android GamePlayer 的运行时能力按资源、脚本和渲染管线拆开。状态分为：

- **已补齐**：Web 已经有对应闭环，并且脚本或场景可以实际使用；
- **部分支持**：有共享数据或基础实现，但行为、效果或控件还没有达到 PC/Android；
- **待实现**：Web 当前没有对应运行时闭环。

## 当前已经补齐的部分

| 能力 | Web 状态 | 说明 |
| --- | --- | --- |
| 工程/场景/资源清单 | 已补齐 | `web.manifest.json`、SHA-256、路径归一化、静态目录和未加密 `.dwgame`。 |
| PMX/VMD 基础加载 | 已补齐 | 共享 PMX 解析、Morph、骨骼、IK、姿态评估和 VMD 相机/实体动作。 |
| Web C# 预编译 | 已补齐 | Web ABI DLL、脚本依赖顺序、哈希和诊断清单。 |
| GUI 按钮/复选框/下拉框事件 | 已补齐 | Canvas overlay、指针命中、脚本 GUI 事件和控件状态。 |
| Sprite overlay | 已补齐 | 通过透明 Canvas 加载图片、布局、旋转、透明度和点击事件。 |
| 脚本音频 | 已补齐 | `Audio.Play/Pause/PlayOneShot/Stop/SetVolume/SetLoop/GetLoop`，通过浏览器 Web Audio 执行。 |
| 脚本动作控制 | 已补齐 | `Entity.ApplyMotion/PlayMotion/PauseMotion/StopMotion`，同步切换共享姿态评估器。 |
| 浏览器 TTS 回调 | 已补齐 | `Entity.Speak`、`SpeakWithCallback`、`IsSpeechEvent` 和 `SpeechCallbackName`。 |
| 场景点光源/射灯 | 已补齐 | Web 帧协议和 GLSL 已支持最多 8 个点光源、4 个射灯。 |
| GUI/Sprite 脚本对象 | 已补齐 | `Scene.GetGuiControl`、`Scene.GetSprite`、`RuntimeGuiControl`、`RuntimeSpriteControl`。 |
| 基础输入兼容属性 | 已补齐 | 鼠标坐标、鼠标增量、滚轮、按键查询和鼠标按键查询。触摸仍是简化状态。 |

## 渲染层差距

| PC/Android 能力 | Web 当前状态 | 补齐计划 |
| --- | --- | --- |
| PMX 漫反射、纹理、双面、透明、边缘 | 部分支持 | 基础材质已经存在；需要继续补 Sphere/Toon 纹理、材质混合模式、Alpha Test 和更准确的 PMX 光照。 |
| 方向光 | 已补齐 | 方向光和环境光进入 WebGL2 PMX shader。 |
| 点光源、射灯 | 已补齐 | 已接入光照计算；局部光阴影仍待实现。 |
| 方向光阴影 | 部分支持 | 单张 1024 深度图；需要级联/偏移、透明投影和更稳定的 PCF。 |
| 点光源/射灯阴影 | 待实现 | 需要 WebGL2 阴影 atlas、局部光矩阵和每光源预算。 |
| PMX 自阴影/接收阴影 | 部分支持 | 基础接收存在；材质级 self-shadow 与 toon 阴影仍需补齐。 |
| Sprite 图片 | 已补齐 | Canvas overlay 路径；还需要与后处理、RenderTexture 和精确旋转命中统一。 |
| Textured Plane | 部分支持 | 已转成 Web mesh，支持纹理、透明度和深度；billboard、阴影语义和反射仍需补齐。 |
| Skybox | 待实现 | 当前只使用场景 clear color；需要天空盒纹理加载和 cubemap/全屏天空 shader。 |
| 水面/Gerstner 波 | 部分支持 | 已生成带透明度的基础水面网格；Gerstner 波、反射、折射、波纹和水下后处理仍需补齐。 |
| Planar reflection | 待实现 | 需要额外相机、反射 RenderTarget 和递归/透明处理策略。 |
| 粒子 | 部分支持 | 已有 point sprite 基础粒子；缺纹理粒子、速度朝向、混合模式、水面交互、碰撞和阴影。 |
| 后处理/水下效果 | 待实现 | 需要 WebGL2 framebuffer 链、色调/雾、畸变、Caustics 和水下遮罩。 |
| 多相机/Viewport | 部分支持 | 共享 RuntimeCamera 已有，但 Web 只发送主相机一套帧；需要多 viewport 合成。 |
| RenderTexture | 待实现 | 资源配置存在，Web 没有 RenderTarget 生命周期和 `rt:` 纹理实现。 |
| 自定义 GLSL/SPIR-V | 待实现 | Web 不加载 PC GLSL/Vulkan SPIR-V；需要单独的 Web shader 契约和安全白名单。 |

## 脚本层差距

| PC/Android 脚本能力 | Web 当前状态 | 补齐计划 |
| --- | --- | --- |
| `Entity` 位置/旋转/缩放/朝向 | 已补齐 | 共享 `RuntimeEntity` 已有基础属性和 `SetPosition/LookAt`。 |
| `Scene.GetEntity/GetGuiControl/GetSprite` | 已补齐 | 返回共享 Web runtime wrapper。 |
| `Scene.Lighting` | 部分支持 | 方向光、环境光和 VMD 控制已存在；需要完整别名和状态同步测试。 |
| `Scene.PointLights/SpotLights` | 已补齐 | 提供 `Add/Get/Remove/Clear/Count/All`。 |
| `Input.MouseX/MouseY/IsMouseButtonDown/IsKeyDown` | 已补齐 | 浏览器 pointer/keyboard 状态进入共享 `RuntimeCameraInput`。 |
| `Input.Touches/TouchTransitions` | 待实现 | 需要平台无关触点结构、Web pointer 多指跟踪和 `PointerClickGesture` 兼容层。 |
| `Scene.Camera.ScreenPointToRay` | 部分支持 | 共享 RuntimeCamera 已提供基础反投影射线；仍需使用真实 Web canvas viewport/DPR 校准。 |
| `Scene.Physics.Raycast/SampleGround` | 部分支持 | Core 有基础碰撞 API；Web PMX collider 注册和 mesh collider 仍未接入。 |
| `Scene.Navigation` | 部分支持 | Core 有导航结构；Web 需要从场景碰撞/地面建立可用 NavMesh，并实现调试信息。 |
| GUI 控件读写 | 已补齐 | 文字、可见性、Checked、Progress、SelectedIndex 等基础属性。文本框浏览器编辑仍待补。 |
| Sprite 事件 | 已补齐 | clicked 事件已接入；entered/pressed/released/hover 状态仍待补。 |
| Context Menu | 待实现 | Web overlay 没有右键菜单状态机和菜单项脚本事件。 |
| `RuntimeAudio` 全量 API | 部分支持 | 常用播放控制已支持；音频资源流式加载、播放位置、one-shot 管理和完成事件待补。 |
| `Entity.Speak/SpeakWithCallback` | 已补齐 | 使用浏览器 SpeechSynthesis；speakerId、口型同步、语音队列和中断语义仍不等同于原生端。 |
| ASR/麦克风事件 | 待实现 | Web 有录音服务，但尚未接入 `IsAsrEvent`、实时转写和脚本事件生命周期。 |
| LLM/工具调用事件 | 待实现 | Web 网络适配器存在，但 `LlmEvent`、工具调用和脚本上下文尚未桥接。 |
| Realtime Voice | 待实现 | 缺少 WebSocket/WebRTC 音频会话、转写事件和回调分发。 |
| 存档/网络/性能/调试对象 | 部分支持 | C# Web 服务存在，但 `Scene.Save/Network/Performance/Debug` 尚未统一进入 WebScriptContext。 |
| 脚本动态添加实体/灯光/粒子 | 部分支持 | 灯光已支持；Web renderer 尚未为动态 plane/water/particle 建立统一资源生命周期。 |

## 资源和平台差距

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| PNG/JPG/WebP/DDS | 部分支持 | PMX 纹理使用 `createImageBitmap`；DDS、Toon 和压缩纹理兼容性需要单独处理。 |
| OGG/WAV/MP3 | 部分支持 | Web Audio 浏览器解码；需要统一预加载、错误和释放策略。 |
| VMD 按需加载 | 部分支持 | 场景动作预加载；脚本动态引用未登记 VMD 时无法同步加载。 |
| 加密 `.dwgame` | 待实现 | 浏览器端需要 WebCrypto 解密和流式解包策略。 |
| IndexedDB 缓存 | 已补齐 | 资源和 JSON 存档已经有适配器。 |
| Service Worker 离线发布 | 部分支持 | 运行时缓存存在，游戏资源版本和增量更新策略仍需完善。 |
| 麦克风权限 | 部分支持 | 原始录音已实现；ASR 事件闭环未实现。 |

## 后续实现顺序

1. Web 脚本输入/碰撞：`ScreenPointToRay`、触摸结构、Web collider 注册和 GUI/Sprite 完整事件状态；
2. Web 场景几何：Textured Plane、Skybox、统一 Sprite/RenderTexture 资源；
3. 水面和后处理：Gerstner 网格、反射/折射、水下效果和粒子交互；
4. 阴影质量：局部光阴影 atlas、PCF、透明投影和材质 self-shadow；
5. 高级脚本服务：ASR、LLM、Realtime Voice、存档/网络/性能对象；
6. 性能和发布：按需加载、GPU instancing、资源释放、移动端 WebGL2 压力测试和浏览器兼容矩阵。

每完成一项，应该同步更新本文件的状态、Web ABI 文档和对应回归测试，避免“接口已经存在但实际渲染/事件链没有闭环”。
