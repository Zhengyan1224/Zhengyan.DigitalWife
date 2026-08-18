# Android GamePlayer 差异清单与完整开发计划

> 审计日期：2026-08-17
> 对照基准：当前仓库中的 PC `Zhengyan.DigitalWife.GamePlayer` 与 Android
> `Zhengyan.DigitalWife.GamePlayer.Android`

首次配置 Android SDK、JDK、`.NET Android` workload（工作负载），或需要安装 APK、加载
`.dwgame` 时，请先阅读
[Android GamePlayer 环境安装与部署说明](../src/Zhengyan.DigitalWife.GamePlayer.Android/README.md)。

## 目标和明确排除项

Android GamePlayer 的最终目标是：加载同一个 GameEditor 工程或 `.dwgame` 发布包时，在移动端
提供与 PC GamePlayer 等价的游戏运行能力。平台输入、音频、文件和图形 API 可以不同，但工程
语义、场景结果、脚本 API 和主要画面效果应保持一致。

以下两项明确不纳入 Android 对齐范围：

- **桌面精灵窗口模式**：透明窗口、点击穿透、窗口拖拽、系统托盘和托盘菜单不实现。
- **Python 脚本**：Android 只支持 C# 脚本；Python 绑定必须在发布兼容性检查中报错。

注意：场景中的普通 2D `SpriteSettings` 不是“桌面精灵窗口模式”。游戏内背景/前景 Sprite、
GUI、对话气泡和加载界面仍属于 Android 必须实现的功能。

Android 也不使用 OpenCL。OpenGL ES 后端使用 CPU 或 GLES GPU 蒙皮；Vulkan 后端完成后可使用
Vulkan Compute。

## 审计结论

当前 Android 工程是一个可以验证 APK、文件加载、EGL 和 PMX 基础绘制的原型，还不是 PC
GamePlayer 的 Android 主机。它直接由 `AndroidPmxSceneRenderer` 遍历 PMX 实体，没有接入 PC
端的 `RuntimeScene`、`RuntimeEntity`、组件生命周期和渲染 Pass。因此，继续在这个单体 Renderer
里逐项增加特例会形成第二套不兼容的引擎实现。

> 上述内容是本次阶段 0 审计时记录的基线。阶段 0～2 的落地结果见本文末尾的“阶段 0～2
> 实施状态”；阶段 3 以后仍然需要建立完整的共享场景运行时。

当前已经存在的 Android 能力：

- `Activity`、`SurfaceView`、EGL、暂停/恢复和渲染表面重建；
- `Choreographer` 帧调度和多点触摸快照；
- 工程目录/`.dwgame` 的 `Intent`、文件关联和分享加载入口；
- 初始场景 JSON 加载和基础 Android 兼容性扫描；
- GLES3 PMX 顶点/索引绘制、基础纹理及简化的 Sphere/Toon 采样；
- 基础主相机、环境光和平行光；
- 一套位于 `Mmd/PmxRuntime` 的共享 VMD 骨骼/Morph/IK/附加骨骼求解器；
- BDEF GPU 蒙皮的有限路径，以及 SDEF/QDEF/复杂模型的 CPU 回退；CPU 回退和 GPU 路径共用同一
  姿态结果；
- Bullet PMX 刚体、碰撞组、六自由度弹簧关节和骨骼回写的初步实现；
- arm64-v8a/x86_64 原生库以及 Android 16 的 16 KB 内存页兼容包。

这些“已经存在”的代码仍需通过 PC 基准测试验证，不能直接视为功能对齐。特别是 PMX 动画、
材质和物理目前是独立简化实现，语义并不完全等同于 PC。

## PC 与 Android 完整差异矩阵

状态含义：

- **可用**：已接入且不存在已知的结构性缺项。
- **部分**：有实现代码，但只覆盖子集，或尚未证明与 PC 等价。
- **缺失**：Android 运行路径没有接入。
- **排除**：按产品范围明确不在 Android 实现。

| 功能域 | PC GamePlayer | Android 当前状态 | 主要缺项 |
| --- | --- | --- | --- |
| 应用生命周期 | 窗口、暂停、恢复、退出 | 部分 | 已有 Activity/Surface 生命周期；没有运行时状态保存、后台资源策略、焦点/音频焦点和低内存处理 |
| 工程目录加载 | 支持 | 部分 | 可加载初始工程；错误只有 Toast，缺少选择器、最近项目、可恢复错误界面 |
| `.dwgame` 普通包 | 支持缓存和独立保存目录 | 部分 | Android 每次使用临时解包，持久缓存、缓存失效和清理策略未完成 |
| 加密/分包 `.dwgame` | 支持密码和多分包 | 缺失 | 没有密码输入 UI；`content://` 只复制单个文件，不能收集 `.001/.002/...` |
| 多场景和场景切换 | 支持加载界面和脚本事件 | 部分（阶段 3） | 已有同步队列、异步加载、卸载、进度和失败恢复；加载界面/脚本事件待后续阶段 |
| 加载界面 | 背景图、进度条、加载脚本 | 部分 | 共享层有分阶段进度状态；Android 可视化加载界面和加载脚本待阶段 5/6 |
| 运行时实体系统 | `RuntimeScene`/`RuntimeEntity` | 部分（阶段 3） | Android 已接入共享注册表、查询、添加、删除和更新；完整脚本对象待阶段 6 |
| 空实体 | 支持 | 部分（阶段 3） | 已进入共享运行时注册表，但 Android 暂无可视化组件 |
| PMX 静态网格 | 完整材质和 Pass | 部分（阶段 1 已完成 Android 主链） | 共享 PC/Android 黄金截图和非均匀缩放矩阵仍需阶段 3 验证 |
| PMX 材质 | Diffuse/Ambient/Specular、Toon、Sphere、材质标志 | 部分（阶段 1 已实现） | Android GLES 已接入主要材质状态；高级阴影/后处理仍待阶段 4 |
| PMX 纹理格式 | PNG/JPG/BMP/TGA/DDS 等路径 | 部分（阶段 1 已实现） | 已使用 Pfim/Stb 解码常用格式；发布期转换、编码边界和 GPU 压缩纹理仍待验证 |
| PMX 描边 | 材质 Edge Pass | 部分（阶段 1 已实现） | Android 已有 Edge Pass；与 PC 的黄金图及移动端质量档仍需验证 |
| PMX 阴影 | 平行光、点光、射灯投射和接收 | 部分（阶段 4） | Android 已接入平行光 Shadow Map、首个射灯局部 Shadow Map、2x2 PCF、Cast/Receive 和 Smooth/Toon；点光和多射灯阴影待后续 |
| VMD 骨骼动画 | 完整曲线和 IK 开关 | 部分（阶段 2 已实现共享求值器） | 真机黄金帧和 PC/Android 姿态容差仍需持续覆盖 |
| 多层 VMD | 独立播放、暂停、时间、权重、添加/删除 | 部分（阶段 2 已实现） | 与完整脚本 RuntimeEntity 的动态增删待阶段 6 |
| PMX Morph | 位置、UV、骨骼、材质、组、翻转、冲量 | 部分（阶段 2 已实现） | 附加 UV 与材质边界样例仍需增加黄金测试 |
| PMX IK/附加骨骼 | 完整 PMX 求值顺序 | 部分（阶段 2 已实现） | 复杂模型和 PC 物理顺序仍需黄金样例验证 |
| SDEF/QDEF | PC 实现 | 部分（阶段 2 已实现） | 已实现 QDEF 双四元数与 SDEF 路径；移动端性能/数值容差仍需验证 |
| GPU 蒙皮 | OpenGL/OpenCL 或 Vulkan Compute 链路 | 部分（阶段 1/2 已接入能力分级） | GLES 仍受 96 骨骼 uniform 限制；复杂模型走共享姿态结果的 CPU 上传，Vulkan Compute 待阶段 9 |
| PMX 物理 | Bullet 刚体、关节和骨骼回写 | 部分（阶段 2 已接入桥接） | 固定步长、子步、初始化/循环重置和 PC 结果黄金对比仍需阶段 3/8 |
| PMX 骨骼关联 | 同名骨骼同步，可绑定组件 Transform/Lighting | 部分（阶段 2 已实现） | 当前在 PMX Renderer 内建立关联；统一 RuntimeEntity 注册表待阶段 3 |
| PMX 脚本控制 | 动作、Morph、骨骼、材质、阴影、物理 | 缺失 | 没有 Android `RuntimeEntity` 和 C# 脚本主机 |
| 相机 | 多控制模式、透视/正交、动态 API | 部分（阶段 3） | 已接入共享控制、触摸旋转/平移/捏合和相机集合；完整脚本 API 待阶段 6 |
| 相机 VMD | 播放、循环、Seek、Roll、投影切换 | 部分（阶段 3） | 已推进独立 Camera VMD、Roll/Up 和投影；编辑器控制面板/脚本 Seek 待后续阶段 |
| 多相机 Viewport | 支持叠加和局部清理 | 部分（阶段 3） | 已实现 viewport 布局换算和局部 color/depth/stencil clear；Render Texture 仍缺失 |
| Render Texture | 多相机离屏纹理和刷新模式 | 部分（阶段 4） | 已支持 FBO、颜色/深度附件、Camera 绑定、每帧/间隔/手动刷新、原生刷新入口和 `rt:` Plane 采样；C# 脚本绑定和复杂后处理链仍缺失 |
| 环境光/平行光 | 静态、脚本和 VMD | 部分（阶段 3/4） | 已接入共享 Lighting、光照 VMD 和 PMX 平行光 Shadow Map；Android 脚本主机仍待阶段 6 |
| 点光源 | 多灯、动态控制、阴影 | 部分（阶段 3/4） | 已加载最多 8 个点光并支持运行时增删改；点光六面体局部阴影待后续 |
| 射灯 | 多灯、锥角、动态控制、阴影 | 部分（阶段 3/4） | 已加载最多 8 个射灯、方向/锥角和运行时增删改；首个 CastShadows 射灯使用独立锥体 Shadow Map，多个射灯阴影待后续 |
| 天空盒 | 纹理、曝光、Tint | 部分（阶段 4） | 已支持 equirectangular 背景、相机旋转、曝光和 Tint；反射/后处理仍缺失 |
| Textured Plane | Billboard、RT、镜面、阴影接收 | 部分（阶段 4） | 已支持主 Pass、纹理、尺寸、Billboard、Opacity/Tint 和平行光阴影接收；RT/镜面仍缺失 |
| 水面 | Gerstner、反射、交互和水下后处理 | 部分（阶段 4） | 已支持动态网格、Gerstner 波形、法线、颜色/透明度、基础平行光和天空盒环境反射；平面反射、波纹交互和完整水下后处理仍缺失 |
| 粒子 | 预设、纹理、混合、碰撞、阴影、触水 | 部分（阶段 4） | 已支持 CPU 生命周期模拟、Billboard、纹理、颜色渐变和 Alpha/Additive；阴影、碰撞/触水仍缺失 |
| 平面反射 | 水面和 Plane 镜面 | 缺失 | 没有反射 RenderTarget 和镜像相机 Pass |
| 后处理 | 水下等场景后处理 | 部分（阶段 4） | 已支持基于相机水下深度的雾化全屏覆盖；离屏场景颜色/深度、失真和焦散仍缺失 |
| 自定义 Shader | GLSL/SPIR-V 双路径及 Uniform | 缺失 | Android 没有移动端 shader 契约、离线校验和动态 Uniform |
| 抗锯齿 | 配置倍数和硬件回退 | 部分（阶段 4） | EGL 已按项目设置申请 1/2/4/8/16x，并自动回退和输出实际倍数；真机能力矩阵仍待验证 |
| OpenGL ES 后端 | PC Pass 功能 | 部分 | 目前是 Android 专用单 shader，不是现有 `IRenderer`/Pass 架构的移动实现 |
| Vulkan 后端 | PC Vulkan | 缺失 | 没有 Android Surface、Swapchain、RenderTarget、ImGui 或 Compute 链路 |
| GUI 控件 | Button/Label/Checkbox/Dropdown/Textbox/Progress | 缺失 | 没有 GUI Renderer、布局、样式、事件、文本输入和脚本对象 |
| 上下文菜单 | 窗口/实体/碰撞体/GUI/Sprite | 缺失 | 触摸端还需定义长按语义，外接鼠标保留右键语义 |
| 对话气泡 | 文本、目标实体和生命周期 | 缺失 | 没有投影到屏幕、布局和脚本管理器 |
| 游戏内 2D Sprite | 背景/前景、布局、旋转、透明度 | 缺失 | 这项必须实现，不属于被排除的桌面精灵窗口模式 |
| C# 脚本 | Start/Update/各类事件和完整运行时 API | 缺失 | 兼容性检查只验证语言，没有编译、打包、加载或执行脚本 |
| Python 脚本 | PC 支持 | 排除 | Android 明确不实现 |
| 键鼠输入 | PC 支持 | 缺失/平台化 | 需支持软键盘、外接键鼠和 Pointer；不照搬桌面窗口输入 |
| 触摸输入 | PC/移动抽象 | 部分 | 已生成快照，但没有接入 RuntimeInput、GUI、相机、射线和手势 |
| 手柄输入 | PC 支持 | 缺失 | Android `InputDevice`、轴/按键映射、热插拔和脚本快照未实现 |
| 剪贴板/文本选择 | PC GUI/API 支持 | 缺失 | 需要 Android ClipboardManager 和 IME 集成 |
| 场景音频 | 播放、暂停、循环、音量 | 缺失 | 没有 AudioTrack/AAudio/OpenSL ES/OpenAL 移动实现，也未加载 `AudioAsset` |
| TTS 和口型 | 合成、播放、PMX Morph 驱动 | 缺失 | 需要 Android ABI 的推理库、音频输出和共享口型控制 |
| 麦克风/ASR | PortAudio/Sherpa/Whisper | 缺失 | 需要权限、AudioRecord、音频焦点、Android ABI 和生命周期处理 |
| Realtime Voice | WebSocket、采音、播放和事件 | 缺失 | 网络/麦克风/音频基础未接入 |
| LLM | Chat、流式、工具调用、Skills/Memory | 缺失 | 没有 Android RuntimeScene、网络服务和安全存储策略 |
| 网络 API | HTTP/运行时封装 | 缺失 | Manifest 权限、客户端生命周期、证书和脚本 API 未接入 |
| Save | 工程外保存目录 | 缺失 | Loader 创建了保存路径，但没有 `RuntimeSaveStore` 或迁移/备份策略 |
| Collider/Raycast | Box/Capsule/Mesh、骨骼绑定 | 缺失 | 场景 Collider 与 PMX 内部 Bullet 物理是两套概念；Android 只实现了后者 |
| NavMesh | 烘焙结果、路径查询、贴地采样 | 缺失 | GameProjects.Core 也未包含相关运行时代码 |
| Debug Draw/性能 API | 线框、射线、计时和日志 | 缺失 | 没有移动端 Debug Pass、帧统计和脚本性能接口 |
| Android 发布器 | 兼容检查、C# 编译、APK/AAB | 部分 | 只有基础检查和通用 APK；没有资产转换、脚本预编译、签名/AAB 和工程专用发布 |
| 桌面精灵窗口模式 | PC 专用 | 排除 | Android 不实现 |

## PMX 显示和动作问题的根因

目前 PMX “能显示但不正确”不是单个参数问题，主要来自 Android 重新实现了一套简化路径：

1. **矩阵和坐标契约未建立基准测试。** `System.Numerics` 行向量/矩阵布局、OpenGL uniform
   上传、Z 轴翻转、FrontFace、投影深度范围和 UV 原点需要统一验证。
2. **Shader 只覆盖简化漫反射。** PC 的 PMX 材质、Toon、Sphere、描边、透明、阴影和局部光源
   分布在多个 Pass 中；Android 当前只有一个 Vertex/Fragment Shader。
3. **动画求解器是独立副本。** 它没有复用 PC 已验证的 PMX/VMD 求值顺序，IK 限制、局部附加、
   完整 Morph、SDEF/QDEF 和物理前后变形顺序存在语义缺口。
4. **GPU 蒙皮能力过窄。** 96 个 uniform 骨骼上限、Morph 限制和 CPU 顶点回传会导致不同模型
   走完全不同的路径，结果和性能都难以稳定。
5. **没有运行时模型层。** Android 无法在模型之间共享骨骼姿态，所以 PMX Relation、骨骼绑定
   Collider、口型和脚本骨骼 API 都无法实现。

因此 PMX 修复必须以“共享求值器 + 明确渲染契约 + 黄金样例”推进，不能只调整当前 shader。

## 目标架构

建议把 Android 改为与 PC 共用游戏运行时、仅替换平台服务和后端实现：

```text
GameEditor 工程 / .dwgame
            |
            v
GamePlayer.Runtime.Core
  - SceneRuntime / RuntimeEntity
  - PMX/VMD 状态和 Relation
  - GUI/脚本事件/场景切换
  - 音频、网络、保存等平台无关契约
            |
            +-------------------------------+
            |                               |
            v                               v
PC Platform Services                 Android Platform Services
Silk Window/Input/OpenAL             Activity/Input/Audio/Storage/IME
            |                               |
            +---------------+---------------+
                            v
                  抽象渲染接口和共享 Pass
                    |                 |
                    v                 v
            OpenGL/OpenGL ES       Vulkan
```

需要建立或抽取以下边界：

- `GamePlayer.Runtime.Core`：场景、实体、相机、灯光、GUI 数据、脚本事件、保存和场景切换；不得
  引用 Silk Windowing、ImGui 原生后端、PortAudio 或桌面 API。
- 共享 PMX Runtime：只保留一套 VMD/Morph/IK/Append/Physics 求值语义；PC 和 Android 使用同一
  组姿态结果和测试。
- 共享 Render Pass 契约：PMX Main/Edge/Shadow、粒子、水面、Plane、天空盒、GUI 和后处理使用
  `IRenderer`/Capability 创建资源，Android 不再用一个专用类承载全部场景。
- Android 平台服务：生命周期、Surface、触摸/键鼠/手柄、IME、剪贴板、音频、麦克风、文件
  选择器、权限、网络状态和安全存储。
- 发布服务：GameEditor 在 Android 发布前完成脚本编译、纹理/音频转换、兼容性检查和 APK/AAB
  参数生成。

## 完整实施计划

以下阶段按依赖顺序排列。前一阶段没有达到验收标准时，不应大规模进入后一阶段。

### 阶段 0：建立对齐基线和自动化测试

任务：

- 建立 Android 专用测试工程，至少包含静态 PMX、Toon/Sphere、透明材质、SDEF/QDEF、IK、
  Append、全类型 Morph、物理、Relation、多相机、三类灯光、GUI、粒子、水面和音频场景。
- PC 和 Android 使用相同工程、相机参数、帧号和分辨率生成截图。
- 增加姿态诊断输出：逐骨骼 Local/Global/Skin Matrix、Morph 权重、材质参数和物理刚体矩阵。
- 增加图像差异工具和允许误差；颜色空间差异与真实几何错误分开报告。
- 建立真机矩阵：至少覆盖 Redmi Note 12 Pro（当前设备）、一台 Adreno arm64 真机和 x86_64
  模拟器；覆盖 API 24、较新稳定版和 Android 16/16 KB 页设备。
- 为 Android Compatibility 增加“支持、降级、拒绝”三种结果，不再只检查脚本语言。

验收标准：

- CI 能构建 Android Debug/Release；所有测试资产都有明确许可证和固定版本。
- 能在同一 VMD 帧比较 PC/Android 骨骼矩阵和截图。
- 兼容性报告能列出当前所有未支持实体和功能，不再允许静默忽略。

### 阶段 1：修正 PMX 静态显示和材质

任务：

- 定义并测试世界坐标、观察矩阵、投影矩阵、Z 翻转、矩阵上传、FrontFace 和 UV 原点契约。
- 修正法线矩阵，处理非均匀缩放；明确 sRGB 纹理、线性光照和 Framebuffer 色彩空间。
- 对齐 PC 的 PMX 材质：Diffuse、Ambient、Specular、Specular Power、Alpha、双面/剔除、
  GroundShadow/DrawEdge 标志和材质绘制顺序。
- 对齐基础纹理、Sphere Multiply/Add/SubTexture 和独立/共享 Toon 采样。
- 增加透明材质排序/Alpha Test 策略，避免头发、睫毛和衣服显示错误。
- 把 PNG/JPG/BMP/TGA/DDS 解码抽象到共享纹理加载器；发布阶段可选择转为移动端标准格式。
- 实现 PMX Edge Pass，并保持 `EnableEdge` 和材质边缘参数有效。
- 移除清屏颜色的测试脉冲，确保项目配置颜色按原值输出。

验收标准：

- 静态 PMX 黄金场景中，轮廓、纹理方向、透明层、Toon/Sphere 和材质颜色与 PC 基准一致。
- 非均匀缩放时法线和光照正确；不同 GPU 上不存在正反面相反或纹理倒置。
- 不支持的纹理会在发布检查时转换或明确报错，不在手机上静默丢失。

### 阶段 2：共享 PMX/VMD 求值器和骨骼关联

任务：

- 从 PC 路径抽出平台无关 PMX Pose Evaluator，替换 `AndroidPmxAnimator` 的独立近似实现。
- 对齐 VMD 目标关键帧贝塞尔语义、30 FPS 时间、循环边界和 Seek。
- 完整实现 IK 开关轨道、IK Link 限制、固定轴、局部轴、局部/全局 Append 和变形层级顺序。
- 完整实现位置、UV/附加 UV、骨骼、材质、组、翻转和冲量 Morph。
- 对齐 BDEF1/2/4、SDEF 和 QDEF；QDEF 不再退化为 BDEF4。
- 每个动作层拥有独立时间、播放状态、权重和循环重置物理设置。
- 建立 Android PMX Runtime Object，暴露骨骼、Morph、材质和动作状态。
- 实现 `PmxRelationSettings`：按同名骨骼同步，支持组件 Transform 和 Lighting 绑定。
- 实现运行时动作、Morph、骨骼节点和材质纹理控制所需的共享接口。
- 验证 Bullet 初始化、固定步长、子步进、运动学/动态/合并刚体、关节限制和循环重置；删除
  不能保证语义的轻量 fallback，或把它标记为显式低质量降级而不是自动等价实现。

验收标准：

- 测试 VMD 的关键帧和中间帧骨骼矩阵在容差内与 PC 一致。
- IK、Append、Morph、SDEF/QDEF 和物理测试模型不再出现明显姿态分叉。
- 两个 PMX 模型开启 Relation 后，同名骨骼、可选 Transform 和 Lighting 与 PC 行为一致。
- Play/Pause/Stop/Seek、动作层增删和权重修改可以实时执行。

### 阶段 3：共享场景运行时和相机/灯光

任务：

- 新建/抽取 `GamePlayer.Runtime.Core`，让 PC 和 Android 共用 `RuntimeScene`、`RuntimeEntity` 和
  注册表，不再由 Renderer 直接遍历 JSON。
- 支持 PMX、空实体、点光源、射灯、粒子、水面和 Plane 的统一创建、更新和销毁生命周期。
- 实现多场景加载、异步/分阶段加载、场景卸载、加载进度和错误恢复。
- 接入主相机选择、透视/正交、第一/第三人称、跟随、自由相机和平台化输入控制。
- 复用 `VmdSceneAnimationPlayer` 的相机/光照 VMD 语义，包括 Camera Roll/Up、投影和循环。
- 实现多相机 Viewport、局部 Clear 和布局换算。
- 实现环境光、平行光、点光和射灯集合及 C# 动态添加/删除/修改接口。

#### 阶段 3 实施状态（2026-08-17）

本轮已经完成阶段 3 的运行时基础和 Android 接入：

- 新增 `Zhengyan.DigitalWife.GamePlayer.Runtime.Core`，提供平台无关的
  `RuntimeScene`、`RuntimeEntity`、`RuntimeCamera`、`RuntimeLighting` 和
  `RuntimeSceneManager`；Android Renderer 不再直接遍历场景 JSON。
- 场景注册表支持 PMX、空实体、点光源和射灯的统一查询、添加、删除、参数修改和实体版本号；
  Android GPU 模型在版本变化后自动重建，新增/删除 PMX 不需要重启 GamePlayer。
- 已接入主相机选择、透视/正交、editor/custom/free 相机输入、第一/第三人称、跟随、自动环绕、
  相机 VMD（循环、帧推进、Roll/Up、投影）以及环境光/平行光 VMD。
- 已接入多 Camera Viewport 的参考分辨率布局换算、OpenGL ES 局部 color/depth/stencil 清理，
  并对每个 viewport 重新提交相机矩阵和灯光集合。
- `RuntimeSceneManager` 支持启动加载、队列切换、卸载、同步/异步加载、进度状态和错误恢复；
  无效场景不会破坏当前可运行场景。Android Surface 重建只释放 EGL/GPU 资源，不会错误销毁运行时场景。
- Android 触摸快照已映射到共享相机输入：单指旋转，双指平移/捏合缩放；VMD、跟随和自动环绕模式
  会按相机控制模式优先处理。
- 阶段 3 自测覆盖 RuntimeEntity 灯光增删改、Viewport、场景 A/B 切换、异步加载、失败恢复和卸载。

阶段 3 仍未宣称完成的部分，顺延到后续阶段：

- PC GamePlayer 仍保留其功能更完整的桌面 `RuntimeScene`/`RuntimeEntity` 类型；下一步需要把 PC
  运行时逐步适配到本 Core 的共享契约，不能把两个同名类型直接强行替换。
- Android 当前实际绘制的是 PMX、环境/平行光、点光和射灯；粒子、水面、Textured Plane、天空盒、
  GUI、游戏内 Sprite、Render Texture、音频和场景脚本仍由阶段 4～8 接入。
- Android 点光目前只有光照贡献，点光六面体阴影仍待后续；射灯已接入首个 CastShadows 射灯的锥体 Shadow Map，
  主材质按 PMX/Plane 的 Cast/Receive 和 Toon/Smooth 语义采样，多个射灯阴影仍待后续。
- 异步加载 API 已完成共享层契约，但 Android GPU 资源提交仍在 GL 渲染线程同步执行；后续加载界面
  会把 CPU 解析、纹理解码和 GPU 上传拆成可观测的分阶段任务。

验收标准：

- Android 可以加载、切换和重新加载多个场景，旧资源完全释放。
- Camera/Light VMD 在指定帧与 PC 构图一致。
- 多 Viewport 不互相污染颜色、深度或 Stencil。
- Relation、Collider 和脚本查询都通过同一个 RuntimeEntity 实例工作。

### 阶段 4：补齐 OpenGL ES 场景渲染 Pass

任务：

- 将 PMX Main/Edge/Depth/Shadow Pass 接入共享抽象，而不是继续扩展单一 GLES shader。
- 实现平行光 Shadow Map，以及 PMX/Plane 的 Cast/Receive 开关和 Smooth/Toon 接收模式。
- 实现点光源立方体阴影和多射灯阴影，加入移动端分辨率、数量和更新频率预算。
- 实现天空盒、Textured Plane、Billboard、Render Texture 和运行时材质纹理替换。
- 实现粒子全部预设、纹理/混合/方向模式、阴影投射和水面交互。
- 实现 Gerstner 水面、波纹、粒子/Collider 触水、平面反射和水下后处理。
- 实现多 Render Texture、刷新模式和 `rt:` 引用。
- 建立 Android 自定义 Shader 契约：GLES GLSL ES 版本、固定资源布局、Uniform 校验和发布期
  离线编译检查；Vulkan SPIR-V 留到阶段 9。
- 实现 MSAA 能力查询及 1x/2x/4x/8x 自动回退，控制台/logcat 输出实际倍数。

#### 阶段 4 实施状态（2026-08-18）

本轮已完成：

- Android EGL 根据 `GameWindowSettings.AntiAliasingSamples` 请求 MSAA，按目标倍数向下回退到设备可用
  配置，并输出 `requested/actual` 采样数；1x 保持无多重采样。
- Android PMX 新增平行光 Shadow Map：1024 深度图、GPU/CPU 蒙皮共用 Depth Pass、`EnableShadow` 投射
  开关、`ReceiveShadow` 接收开关、`ReceiveShadowMode=Smooth/Toon` 以及主 Pass 2x2 PCF。
- Android 已接入第一个 `CastShadows` 点光源的六面体 Shadow Map（六面 90 度视锥、线性距离深度和四点 PCF），
  以及第一个 `CastShadows` 射灯的锥体 Shadow Map；局部阴影仍遵循 PMX/Plane 的 `ReceiveShadow` 与 Toon/Smooth。
- Android 已接入基础 `textured_plane`/`plane` 主 Pass：支持项目变换、尺寸、Tint/Opacity、Billboard、纹理
  和平行光阴影接收；镜面反射暂时给出兼容性降级警告，不会静默丢失配置。
- Android 已接入基础 Skybox Pass：使用项目天空盒纹理、Tint、Exposure 和相机旋转绘制 equirectangular
  背景；天空盒不参与深度/阴影，纹理缺失时记录日志并保留清屏回退。
- Android 已接入基础 Particle Pass：共享 `ParticleEntitySettings`，进行确定性 CPU 生命周期/速度/加速度模拟，
  使用动态 Billboard VBO，支持自定义纹理、SoftCircle/Streak/Flame fallback、颜色渐变、Alpha/Additive 混合；
  粒子阴影和水面交互暂时通过兼容性警告降级。
- Android 已接入基础 Water Pass：按 `WaterSurfaceSettings` 生成移动端受限分辨率网格，支持 Gerstner 位移、动态法线、
  Deep/Reflection Tint、透明度、环境/平行光着色和基于天空盒的 equirectangular 环境反射；平面反射、涟漪交互与水下后处理仍通过兼容性警告降级。
- Android 已接入基础 RenderTexture：为启用的目标创建 GLES 颜色/深度 FBO，绑定指定 Camera，并允许 Plane 通过 `rt:` 路径采样；
  支持每帧、按间隔和手动刷新判定，并提供 `RequestRenderTextureRefresh` 原生入口；C# 脚本绑定与多级后处理链仍待阶段 6。
- Android 已接入水下基础后处理：当相机低于启用水面时，根据水下雾密度、可见距离和雾颜色绘制全屏覆盖；
  该 Pass 不读取离屏场景颜色，因此失真、焦散和真正 RenderTexture 后处理仍待后续实现。
- 阴影 FBO 创建失败时明确记录降级日志并关闭阴影，不影响主场景绘制；兼容性报告不再把平行光 PMX
  阴影误报为完全缺失。

仍在阶段 4 后续迭代中的部分：多点光/多射灯 Shadow Map、水面平面反射/交互、完整后处理、
Render Texture 脚本绑定、自定义 Android Shader 契约和真机性能预算。

验收标准：

- 测试工程中所有非桌面实体类型都可见且参数有效。
- 三类灯光和阴影的 Cast/Receive、Toon/Smooth 模式与 PC 语义一致。
- 水面、粒子、Plane、天空盒和 Render Texture 能组合使用且没有 RenderTarget 状态泄漏。
- 中档移动 GPU 在预设质量档下达到稳定帧时间，阴影预算超限时有确定性降级。

### 阶段 5：输入、GUI、游戏内 Sprite 和加载界面

任务：

- 把现有多点触摸快照接入共享 `RuntimeInput`，实现按下/移动/抬起/取消和主触点语义。
- 实现触摸点转射线、相机手势和 GUI Pointer Capture，避免相机与 GUI 同时响应。
- 接入 Android 软键盘/IME、文本组合、光标/选区、退格、回车和多行文本。
- 接入 ClipboardManager；支持复制、粘贴和脚本剪贴板 API。
- 支持外接键盘、鼠标、滚轮和 Android Gamepad，保持脚本按钮/轴名称与 PC 一致。
- 构建 Android ImGui 或等价 GUI Renderer，支持 Button、Label、Checkbox、Dropdown、
  Textbox、Progress、样式、布局和事件。
- 实现上下文菜单：触摸长按作为移动端入口，外接鼠标保留右键。
- 实现对话气泡、实体屏幕投影以及背景/前景 2D Sprite。
- 实现加载背景、图片、进度条和安全区/刘海/圆角屏布局。

验收标准：

- 所有 GUI 控件可触摸操作，Textbox 能正确唤起中文输入法并处理组合输入。
- GUI、Sprite、3D Scene 的绘制顺序与 PC 一致。
- 屏幕旋转、分辨率变化和应用恢复后 GUI 状态不丢失、不重叠。

### 阶段 6：C# 脚本和运行时 API

Android 不应在设备上启动 Roslyn 编译 `.csx`。推荐提供两种发布模式：

- **工程专用 APK/AAB**：GameEditor 在 PC 上把 C# 脚本编译成程序集，并在 Android 打包时
  一起进入应用。这是商店发布的推荐模式。
- **通用侧载 GamePlayer + `.dwgame`**：发布包内包含预编译 IL 和 API 清单，由 Android Mono
  运行时加载。需要明确 Android/商店对动态代码加载的政策限制；此模式主要用于开发和可信侧载。

任务：

- 抽取稳定的 `GamePlayer.ScriptApi` 程序集，避免脚本引用桌面 GamePlayer 可执行项目。
- GameEditor Android Publisher 在发布时编译 C#，输出程序集、依赖清单、API 版本和哈希。
- 配置 Trimmer/AOT 保留脚本可访问类型；禁止任意本地原生库和未声明反射入口。
- 实现 Start/Update、GUI、加载、语音、ASR、Realtime、LLM Tool 和场景事件派发。
- 对齐 `RuntimeEntity` 的 Transform、动作层、Morph、骨骼节点、材质、阴影、物理、Relation、
  粒子、水面和灯光 API。
- 对齐 `RuntimeScene` 的实体、相机、GUI、Sprite、场景切换、Physics、Navigation、Debug、Save、
  LLM、ASR、Realtime Voice、Network 和灯光集合 API。
- Android 兼容性检查拒绝 Python、缺失预编译程序集、API 版本不匹配和不支持的依赖。

验收标准：

- 同一个 C# 行为测试工程在 PC 和 Android 上得到相同实体状态和事件顺序。
- Android 启动过程中不调用 Roslyn，也不需要写入可执行代码目录。
- 脚本异常包含脚本名、事件名和可读堆栈，单个脚本失败不会破坏渲染线程。

### 阶段 7：音频、TTS、ASR、Realtime Voice 和 LLM

任务：

- 选择并实现 Android 音频平台服务（优先 AAudio/Oboe 或稳定的 AudioTrack/AudioRecord
  封装），支持播放、暂停、停止、循环、音量和多音源。
- 处理 Audio Focus、来电/耳机切换、蓝牙路由、后台暂停和采样率转换。
- 加载场景 `AudioAsset`，支持发布期音频格式转换。
- 为 SherpaOnnx/Whisper/TTS 准备 arm64-v8a Android 原生库，并验证 16 KB 页兼容。
- 接入 TTS 播放和共享 PMX 口型 Morph 驱动。
- 请求并管理 `RECORD_AUDIO`/`INTERNET` 等运行时权限，拒绝后提供可恢复提示。
- 接入 ASR、Realtime Voice、唤醒词、流式音频和对应脚本事件。
- 接入 OpenAI-compatible LLM、流式输出、Function Call、Skills 和 Memory；API Key 使用 Android
  Keystore/安全配置，不写入日志。
- 接入 `RuntimeNetwork` 和 `RuntimeSaveStore`，定义离线、超时、应用升级和数据迁移行为。

验收标准：

- 场景音频与应用生命周期同步，无后台持续播放或资源泄漏。
- TTS 能驱动口型；ASR/Realtime Voice 在授权、拒绝和网络断开情况下都能恢复。
- 保存数据位于应用私有目录，升级覆盖安装后仍存在，卸载行为符合 Android 规则。

### 阶段 8：场景碰撞、Raycast、NavMesh 和调试能力

任务：

- 复用 Box/Capsule/Mesh Collider 生成逻辑，支持 PMX 骨骼绑定 Collider。
- 实现场景 Raycast、Overlap、Distance、地面采样和 Collider 上下文菜单命中。
- 接入 NavMesh 烘焙产物读取、路径查询、最近点和贴地移动；烘焙仍由 GameEditor 在 PC 完成。
- 区分“PMX 内部 Bullet 物理”和“游戏场景 Collider 查询”，避免错误共用碰撞组或世界单位。
- 实现移动端 Debug Draw、帧率、CPU/GPU 时间、模型/三角形/Draw Call 和内存统计。

验收标准：

- PC/Android 对同一 Raycast/NavMesh 测试返回相同实体、Collider 和近似命中点。
- 骨骼绑定 Collider 随动画更新，Relation 和物理模型下仍正确。
- Release 默认关闭昂贵 Debug Draw，但脚本性能查询仍可用。

### 阶段 9：Android Vulkan 和 Vulkan Compute

应在 OpenGL ES 场景语义稳定后再进入本阶段，避免同时调试“功能差异”和“后端差异”。

任务：

- 建立 Android Vulkan Surface、设备/队列、Swapchain、深度/MSAA、同步和生命周期恢复。
- 把阶段 1-5 的所有 Pass 接到现有 `IRenderer`/Capability 工厂，禁止出现 Android Vulkan 专用
  场景逻辑分叉。
- 实现 Render Texture、三类阴影、ImGui/Sprite、后处理和读回。
- 接入 Vulkan Compute GPU-only 蒙皮，渲染直接消费 Compute 输出，不回读 CPU。
- 完成 `Auto` 能力探测：只有所有必需 Feature/Format/Pass 可用时选择 Vulkan，否则记录原因并
  回退 GLES3。
- Android 自定义 Shader 使用 SPIR-V 契约，并在 GameEditor 发布期完成校验。

验收标准：

- 同一设备 GLES/Vulkan 截图和脚本结果在容差内一致。
- Surface 销毁/重建、切后台和旋转不会设备丢失或闪烁。
- Vulkan Compute 蒙皮没有每帧 Fence Wait + CPU Readback + Vertex Re-upload 链路。

### 阶段 10：Android 发布器、兼容性和产品化

任务：

- GameEditor 增加 Android 发布目标：包名、版本号、图标、横竖屏、最低 API、权限、质量档、
  ABI、签名、APK/AAB 和是否工程专用脚本程序集。
- 发布前转换不兼容的纹理/音频，编译 C#，收集全部依赖并生成资产清单和哈希。
- 扩展兼容性检查：实体类型、Shader、纹理格式、脚本 API、原生 ABI、加密/分包、权限、内存
  预算、灯光/阴影预算和后端能力。
- 支持加密包密码 UI；支持分包的多文件选择/导入和完整性校验。
- 加入持久解包缓存、LRU 清理、版本/哈希失效和独立 Save 目录。
- 生成 Android 图标/启动画面、崩溃日志、隐私说明和第三方许可证。
- 建立签名密钥安全流程，支持 `adb` 测试 APK 和 Google Play AAB。

验收标准：

- 非技术用户可从 GameEditor 一次发布可安装的 APK/AAB，并在真机启动指定工程。
- 兼容性检查没有 Error 时，发布产物不会因已知不支持功能在设备上静默缺失。
- 覆盖安装保留 Save；缓存可清理；签名和版本升级流程可重复执行。

## 性能和稳定性要求

功能对齐之外，所有阶段都应遵守以下约束：

- 使用 30 FPS 和 60 FPS 两个质量目标，帧调度基于真实 Delta Time，不使用渲染帧数推进动画。
- PMX 姿态求值和物理使用固定/有界步长；长时间切后台后不能一次补算大量物理步。
- 对模型、纹理、RenderTarget、Shadow Map、粒子和音频设置可查询的内存预算。
- Surface 重建必须重建 GPU 资源但保留场景逻辑状态；Activity 重建需要保存当前场景和必要状态。
- 支持 Android 低内存回调、热降频和电量模式；质量降级必须可预测并写入 logcat。
- Renderer、Bullet、Bitmap、Buffer、Audio 和网络对象都必须有确定的 Dispose/停止路径。
- 所有后台回调通过线程安全队列回到游戏线程，不允许直接从音频/网络线程修改场景集合。

建议最低性能验收场景：

- 1 个高质量 PMX + VMD + 物理 + 平行光阴影 + GUI：目标 60 FPS。
- 3 个 PMX + 多灯光 + 粒子 + 水面：中档质量目标稳定 30 FPS。
- 连续运行 30 分钟、前后台切换 50 次、Surface 重建 20 次：无崩溃、无持续内存增长。

## 测试与发布矩阵

| 维度 | 最低覆盖 |
| --- | --- |
| CPU/ABI | arm64-v8a 真机、x86_64 模拟器 |
| GPU | Mali（当前 Redmi）、Adreno 至少各一台 |
| Android | API 24 最低版本、一个主流版本、最新目标版本 |
| 内存页 | 4 KB 与 Android 16 的 16 KB 页环境 |
| 后端 | GLES3；Vulkan 阶段完成后增加 GLES/Vulkan 双跑 |
| 屏幕 | 16:9、20:9、刘海/挖孔、安全区、不同 DPI |
| 输入 | 单点、多点、中文 IME、外接鼠标键盘、手柄 |
| 生命周期 | Home、锁屏、旋转、分屏、来电/音频焦点、进程重建 |
| 包 | 普通、加密、分包、损坏包、旧版本包、覆盖升级 |

每个功能必须至少包含：单元测试、PC/Android 状态对比、截图或音频/输入集成测试，以及一台
真实设备验证。只在模拟器通过不能标记为完成。

## 推荐执行顺序

实际开发时按以下优先级推进：

1. **阶段 0-2：PMX 正确性、共享求值器和 Relation。** 先解决当前模型显示、动作和骨骼关联。
2. **阶段 3：共享 RuntimeScene/RuntimeEntity。** 这是 GUI、脚本和其他实体的共同前置条件。
3. **阶段 4-6：GLES 场景 Pass、GUI/Input、C# 脚本。** 达到普通游戏工程可运行。
4. **阶段 7-8：音频/AI/保存、碰撞/NavMesh。** 补齐 PC 游戏逻辑能力。
5. **阶段 9：Vulkan。** 在 GLES 语义稳定后复制同一组 Pass，不另写场景系统。
6. **阶段 10：发布器和产品化。** 最终形成非开发者可使用的 Android 发布流程。

## 阶段 0～2 实施状态（2026-08-17）

阶段 0～2 已完成第一轮可运行实现，当前验收边界如下：

- **阶段 0：已完成基线工具和拒绝式兼容性检查。** 新增 `tests/Zhengyan.DigitalWife.PmxParity.Tests`
  测试工程，支持 VMD 曲线自检、确定性姿态快照比较和 PMX 结构诊断；`AndroidProjectCompatibility`
  现在返回“支持/降级/拒绝”，对未支持实体、GUI、加载脚本、运行时 C# 和多摄像机等能力明确报告，
  不再静默忽略。
- **阶段 1：已完成 Android PMX 静态渲染主链。** 纹理解码已覆盖常用 PNG/JPG/BMP/TGA 及 DDS
  路径，使用共享的 PMX 材质数据驱动 Diffuse/Ambient/Specular、Sphere/Toon、透明排序、双面
  标志和 Edge Pass；清屏不再使用测试脉冲色。软透明纹理会自动进入透明深度写入策略。
- **阶段 2：已完成第一轮共享求值器和关联。** `PmxPoseEvaluator` 已移入 `Mmd/PmxRuntime`，
  PC/Android 共用 `VmdInterpolationCurve`、PMX 诊断和 `IPmxPoseEvaluator` 契约；求值器覆盖多层
  VMD 独立时间/权重/循环、位置/UV/附加 UV/骨骼/材质/组/翻转/冲量 Morph、IK 开关与 Link 限制、
  Append、BDEF1/2/4、SDEF、QDEF、Bullet 物理桥接以及按同名骨骼的 Relation。Android 只提供
  GLES 顶点上传和 `IPmxPhysicsBridge`，不再拥有另一份动画求值器。
- **当前实际降级：** GLES 统一变量上限仍限制 GPU 蒙皮骨骼数；超过上限的模型会走 CPU 蒙皮，
  兼容性报告会给出原因。完整 PC/Android 黄金截图、真机矩阵和大规模共享 `RuntimeScene` 仍属于
  阶段 3 及以后，不应把本阶段标记为整个 Android GamePlayer 完成。

### 阶段 0～2 验收命令

在仓库根目录执行：

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests/Zhengyan.DigitalWife.PmxParity.Tests.csproj --no-restore
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests/Zhengyan.DigitalWife.PmxParity.Tests.csproj --no-restore -- `
  --analyze "D:\Projects\CSharp\MMD\GameEditorProjects\DemoGame01\assets\models\Body\Body.pmx"
dotnet build Zhengyan.DigitalWife.Android.slnx --no-restore `
  -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" `
  -p:JavaSdkDirectory="$env:LOCALAPPDATA\Android\jdk"
```

预期结果：自检输出 `PMX/VMD parity self-tests passed.`；PMX 诊断列出顶点、材质、骨骼、Morph、
刚体和关节计数，并对超过 GLES 骨骼上限的模型给出警告；Android 解决方案构建为 0 警告、0 错误。

## Android 完成定义

只有同时满足以下条件，才能声明 Android GamePlayer 与 PC 版完成主要功能对齐：

- 同一个非桌面精灵工程无需手工修改即可在 PC 和 Android 加载。
- 除 Python 和桌面精灵窗口模式外，所有启用功能要么等价运行，要么在发布前明确报错；不允许
  静默忽略。
- PMX 静态画面、指定 VMD 帧姿态、Morph、Relation、物理和阴影通过黄金对比。
- 多场景、相机、灯光、GUI、游戏内 Sprite、C# 脚本、音频、保存、碰撞和 NavMesh 均通过
  自动化及真机测试。
- OpenGL ES 稳定运行；选择 Vulkan 时不依赖 OpenGL ES 或 OpenCL，并能在不支持时自动回退。
- APK/AAB 可由 GameEditor 发布，签名、权限、升级、缓存和 Save 行为均有文档和测试。

## 构建入口

Android 主机独立于桌面解决方案构建：

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$jdk = "$env:LOCALAPPDATA\Android\jdk"
dotnet build Zhengyan.DigitalWife.Android.slnx `
  -p:AndroidSdkDirectory=$sdk `
  -p:JavaSdkDirectory=$jdk
```

完整环境安装、Release APK 和真机部署命令见
[Android GamePlayer 环境安装与部署说明](../src/Zhengyan.DigitalWife.GamePlayer.Android/README.md)。
