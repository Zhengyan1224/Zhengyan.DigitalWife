# Web 与 PC/Android 能力差距审计

更新时间：2026-11-03

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
