# DemoGame01 Android Vulkan 性能检查（2026-11-09）

Vulkan 的优势主要是降低驱动的 CPU 开销，并允许引擎显式控制资源和同步；GPU 着色、附件加载/保存、驱动实现及帧循环仍决定实际帧率，不能仅凭 API 名称判断快慢。[Android Vulkan 官方概览](https://developer.android.com/games/develop/vulkan/overview)，[Khronos Render Pass 性能示例](https://docs.vulkan.org/samples/latest/samples/performance/render_passes/README.html)

本次继续保留上一轮阴影 gather 和 CPU 姿态批量上传，工程目录只读。检查的首场景包含 Body、MaidOutfit、foreignhouse、一个水面、三组粒子、屏幕图片、两个启用的相机和一张 512×512 RenderTexture。模型顶点数分别为 52,521、100,012 和 19,152。

## 真机基线

连接的测试设备使用 Adreno 619，屏幕渲染尺寸 2310×1080。用户报告 OpenGL ES 约 9.2 FPS、Vulkan 约 5.3 FPS。本次采集的版本 6 Vulkan 稳定日志为约 5.5–5.6 FPS：

| CPU 计时阶段 | 每帧约耗时 |
| --- | ---: |
| 帧 slot fence | 0.03 ms |
| 场景更新 | 14 ms |
| 命令录制 | 49 ms |
| 提交 / 呈现 | 107 ms |

这些是 CPU 计时，包含调用中的等待，不能直接视为 GPU 时间戳。呈现路径占主要时间，继续消除 CPU 更新前等待不是这次的重点。两个后端请求 8× MSAA，日志显示 GLES 实际为 4×、Vulkan 实际为 2×；本次不改变该回退或工程画质设置。

## 修改

1. `VeldridEmptyPassPatch` 为 Veldrid 4.9 的 `SetFramebufferCore` 增加待清屏检查。没有绘制、也没有颜色或深度清屏的目标切换不再启动空 render pass；原有最终布局转换和资源引用保护继续执行。实际绘制、部分清屏、深度清屏和完整清屏保留。修补同时用于 Android 和 PC Vulkan 的构建副本，不修改 NuGet 原始 DLL。
2. Vulkan 水面上传按原始顺序压紧非零涟漪，将时间衰减、波形时间相位、半径平方倒数在 CPU 上预计算。GPU 保留空间衰减和完整的有效涟漪计算，没有截断涟漪数量、生命周期或强度。48 个有效涟漪时，每个片元的指数函数由 96 次减为 48 次；没有有效涟漪时跳过空槽计算。参数仍通过帧 slot 的不可变快照上传。
3. 内置 PMX 阴影增加硬件线性深度比较采样。仅当实际原生深度格式支持采样、深度附件和线性过滤，且所有启用的阴影绑定都提供比较采样器时使用。保留方向光 3×3、局部光源 2×2 PCF、深度偏移、图集边界和 Toon 阈值。未启用的阴影绑定复用已有深度 view 作为合法占位，不改变 enable 标记；不增加阴影作用。

原始深度点采样器继续提供给图片平面和自定义 SPIR-V。自定义着色器使用原有 shader 和 sampler；不支持硬件路径或驱动拒绝比较 pipeline 时，内置 PMX 回到 gather 路径。[Vulkan GLSL 的独立 samplerShadow 类型](https://github.com/KhronosGroup/GLSL/blob/main/extensions/khr/GL_KHR_vulkan_glsl.txt)

启动标记为 `startupPatch=8; shadowFilter=hardware-comparison-or-gather; emptyRenderPass=skipped; waterRipples=precomputed`。帧日志新增 `waterDraws`、`waterRipplesPerDraw`、`pmxDraws`、`hardwareShadowDraws`，用于确认实际启用的路径。

## 验证依据

- 原生 Vulkan 调用计数及 4096 个回读像素证明：32 次空目标切换没有产生 render pass，次级颜色附件、深度、完整和最终清屏仍执行。裁剪后的 Android Veldrid 也执行此验证。
- 水面 GPU 对照原始公式，覆盖 0/1/5/17/48 个涟漪、稀疏槽位、小强度、负强度及两种生命周期，40,960 个像素最大误差约 `1.91e-6`。
- 硬件阴影路径实际绘制方向光、点光源、射灯的平滑和 Toon 模式，并检查实际使用了比较 pipeline；自定义 SPIR-V 绘制保持。
- 多帧 PMX、反射、深度、GPU 蒙皮及大数据 CPU 回退测试继续保留。首场景三个真实模型各三轮加载、Compute 更新和释放通过。

```powershell
dotnet run --no-restore --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test Vulkan
dotnet run --no-restore --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --vulkan-project-models 'D:\Projects\CSharp\MMD\GameEditorProjects\DemoGame01' scenes/main.scene.json
dotnet tools/VeldridAndroidPatch/bin/Release/net10.0/VeldridAndroidPatch.dll --verify-empty-passes src/Zhengyan.DigitalWife.GamePlayer.Android/obj/Release/net10.0-android/android-arm64/linked/Veldrid.dll
```

## 阶段性真机结果

版本 7（空 pass 和水面预计算）已覆盖安装到同一手机，用户测得约 5.8–5.9 FPS；日志为约 5.7–5.8 FPS，录制降到约 41 ms，呈现仍约 109 ms，每帧绘制水面 3 次、每次 48 个有效涟漪。收益有限，尚未接近 GLES，因此继续增加版本 8 的硬件阴影比较路径，而没有降低画质。

本地保留覆盖安装前的 APK，覆盖安装使用 `adb install -r` 保留应用数据。帧率比较需排除加载和首次 pipeline 创建，固定场景、相机、画质和温度；不同计时窗口的 GUI FPS 与五秒日志统计可能有少量差异。

## 最终真机结果与安装包

版本 8 已在同一手机安装，并使用相同的 DemoGame01 Vulkan 游戏包复测。用户保持首场景运行后的连续五秒日志稳定在约 **7.8–7.9 FPS**，屏幕 FPS 为 **7.9**。每帧 `hardwareShadowDraws=486`，确认实际使用了硬件比较；呈现路径约 54–56 ms，命令录制约 49–50 ms，更新约 14 ms。相比本次版本 6 的日志基线 5.5–5.6 FPS，提升约四成；仍低于用户测得的 GLES 约 9.2 FPS，尚未追平。

没有降低工程请求的 MSAA、反射分辨率、阴影采样范围、涟漪数量或粒子数量。最终截图检查人物、水面反射、粒子和右下角 RenderTexture 正常显示；该检查覆盖首场景，并不能替代所有项目和 Android 生命周期的完整真机回归。

包含修补版 Veldrid 的原生 Vulkan 回归 **10/10 通过**，Android Release ARM64 构建 0 错误，APK v2/v3 签名验证通过。既有 SDL 页面大小/重复打包和 NuGet 漏洞元数据源警告保留。

- [版本 8 签名 APK](../artifacts/android/GamePlayer-arm64-20261109-vulkan-hardware-shadows-Signed.apk)
- SHA256：`F8B6D061949BE4D7792825EE3A582AA1237A32301CC54AB572D674685F14BD1E`。
- [最终首场景截图](../temp/demogame01-vulkan-hardware-final.png)
- [最终手机日志](../temp/demogame01-vulkan-hardware-final-device.log)

```powershell
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android -c Release -r android-arm64 -t:SignAndroidPackage -p:AndroidPackageFormats=apk -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" -p:JavaSdkDirectory="$env:LOCALAPPDATA\Android\jdk" --no-restore
```
