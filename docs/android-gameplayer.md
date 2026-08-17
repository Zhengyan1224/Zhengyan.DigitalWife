# Android GamePlayer 架构与进度

首次配置 Android SDK、JDK、`.NET Android` workload（工作负载），或需要安装 APK、加载 `.dwgame` 时，
请先阅读 [Android GamePlayer 环境安装与部署说明](../src/Zhengyan.DigitalWife.GamePlayer.Android/README.md)。

Android GamePlayer 使用独立的 Android 原生主机实现，并继续兼容现有游戏工程格式。它不是对
桌面窗口主机的模拟，而是直接使用 Android 生命周期、输入、文件和图形接口。

## 支持范围

- 游戏工程目录和 `.dwgame` 发布包仍然是 Android 版本的内容格式。
- Android 端只支持 C# 脚本。正式的 Android 发布流程会预编译脚本，不会在设备上启动 Roslyn
  或 Python 解释器。
- Android 端不提供桌面精灵、透明窗口点击穿透、窗口拖拽和系统托盘功能。
- 规划的图形后端是 OpenGL ES 和 Vulkan。当前原生主机使用 OpenGL ES；后续完成 Vulkan 主机
  后，`Auto` 才会在设备和所需功能都可用时优先选择 Vulkan，否则回退到 OpenGL ES。
- Android 端不会使用 OpenCL。只有 Vulkan 后端完成后，才可能使用 Vulkan Compute。

GameEditor 的 `Package / Publish` 面板提供 **Android 兼容性检查**。检查器会扫描所有场景，拒绝
已经启用的非 C# 脚本，并报告 Android 会忽略的桌面专用设置。

## 主机架构

Android 应用使用 `Activity`、`SurfaceView`、`Choreographer` 帧调度和 EGL 窗口表面。主机会创建
真实的 OpenGL ES 上下文，响应 Android 的暂停、恢复和渲染表面重建事件，并把每一帧提交到
屏幕。桌面端的 `Silk.NET.Windowing` 主机保持不变。引擎共享能力通过平台服务分离，主要包括：

- 渲染表面的创建和画面提交；
- 应用生命周期和帧调度；
- 触摸、键盘和手柄输入；
- 音频播放和采集；
- 游戏工程和发布包存储。

主机可通过以下方式接收本地工程目录或 `.dwgame` 路径：

- `Intent` 中的 `zhengyan.project_path` 扩展参数；
- `file://` 或 `content://` 类型的 `Intent` URI；
- 应用沙盒内的 `files/GameProject` 目录。

创建渲染表面前，主机会先加载 `game.project.json`、编辑器场景列表和 Android 兼容性检查结果。
GLES 主机通过不依赖桌面运行库的 `Zhengyan.DigitalWife.Mmd.Core` 程序集解析 `pmx_model`
实体，并使用 GLES3 顶点缓冲和索引缓冲绘制实体变换、所选场景相机、环境光和平行光。当前支持
PMX 材质漫反射颜色、UV 和 Android 可以解码的基础纹理。纹理缺失或格式不支持时会回退到材质
颜色，并通过 logcat 输出诊断信息。

PMX GLES 路径会读取配置的全部 VMD 动作层，按照动作层权重混合骨骼和 Morph 轨道，并沿用
桌面端“目标关键帧控制区间”的贝塞尔曲线语义。它还会处理组 Morph、位置 Morph、UV Morph、
骨骼 Morph、附加变换和 CCD IK，并支持播放速度和循环设置。

没有动画 Morph 轨道的 BDEF1/2/4 模型会使用 GLES3 顶点着色器执行 GPU 蒙皮；SDEF/QDEF、
骨骼数量过大的模型和带动画 Morph 的网格会自动回退到 CPU 蒙皮。材质路径支持 PMX 基础纹理、
Sphere 纹理以及独立/共享 Toon 纹理。发布包没有包含桌面引擎资源时，共享 Toon 纹理会使用
Android 端生成的色阶纹理。

Android PMX 物理使用 BulletSharp 和 Android `libbulletc` ABI。运行时会创建 PMX 球形、盒形和
胶囊刚体，设置质量/惯量、阻尼、弹性和摩擦力，应用 PMX 碰撞组与遮罩，并创建六自由度弹簧
关节。`Dynamic` 和 `DynamicAndBoneMerge` 的模拟结果会按照与桌面运行时一致的偏移量和 Z 轴
转换写回骨骼层级。运行时遵守 `EnablePhysics`、工程重力和循环播放时重置物理的设置。

如果设备 ABI 不受支持，导致 Bullet 无法初始化，运行时会回退到确定性的轻量二级运动求解器。
原生依赖固定使用 `Evergine.LibBulletc.Natives` 2025.8.29.27，因为该版本满足 Android 16 对
16 KB 内存页大小的要求。

Android 引用的 `Zhengyan.DigitalWife.GameProjects.Core` 只包含工程数据模型、场景 JSON、发布包
读取器和兼容性检查。桌面专用的粒子映射以及原生渲染/音频依赖不会进入这个程序集。

## 实施阶段

1. Android 生命周期、渲染表面和触摸输入：**已完成。** 主机可提供每帧不可变的多点触摸快照，
   包括像素坐标、归一化坐标、位移、压力和触摸阶段。
2. OpenGL ES PMX 场景渲染：**进行中。** 已接入 PMX 动画和材质、IK、附加骨骼、Morph、
   Bullet 刚体/关节/碰撞和 GPU BDEF 蒙皮；阴影、粒子、后处理和音频仍需继续迁移。
3. Vulkan Surface/Swapchain 和 `Auto` 自动回退。
4. VMD、光照、阴影、粒子、水面、后处理和 GUI 的完整功能对齐。
5. Android 音频、软键盘和手柄支持。
6. GameEditor Android 发布器和发布阶段的 C# 脚本预编译。

Android 主机需要单独构建，这样只开发桌面版本时不必安装 Android workload（工作负载）：

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$jdk = "$env:LOCALAPPDATA\Android\jdk"
dotnet build Zhengyan.DigitalWife.Android.slnx `
  -p:AndroidSdkDirectory=$sdk `
  -p:JavaSdkDirectory=$jdk
```

构建这个解决方案必须安装 .NET Android workload（工作负载）。主桌面解决方案有意不包含 Android 应用
项目，避免桌面开发环境被强制要求安装 Android 工具链。
