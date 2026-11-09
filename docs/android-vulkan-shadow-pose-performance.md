# Android Vulkan 阴影与姿态上传优化（2026-11-09）

本次按 DemoGame05 的配置检查 Vulkan 性能，优先保留现有画质和同步保护。工程目录只读，未修改场景、资产或工程设置。

DemoGame05 主场景包含教室和人物两个 PMX 模型、方向光阴影、一个开启阴影的点光源、一个开启阴影的射灯，以及一块开启镜面反射的图片平面。工程配置为 Vulkan Compute、4× MSAA、Android `auto` 画质，方向光/局部阴影上限为 1024/512。

近期阴影画质修复将 Vulkan 的每个 PCF 采样点改为四次 `texelFetch`，先比较深度再插值；GLES 使用硬件线性比较采样。因此，相同阴影画质在 Vulkan 上增加了纹理指令开销。同期恢复默认 MSAA 和反射分辨率也增加了场景负载。这些代码变化可以解释差距扩大的可能来源，尚无同一手机的前后帧率数据来确认各项贡献。

## 实现

`PmxMainPassRenderer.SampleBilinearShadow` 改为一次 `textureGather` 读取相同四个深度，再按原来的比较和双线性权重计算覆盖率。

| 每个片元的阴影路径 | 原来的纹理指令调用次数 | 优化后的调用次数 |
| --- | ---: | ---: |
| 方向光 3×3 PCF | 36 | 9 |
| 单个点光源 2×2 PCF | 16 | 4 |
| 单个射灯 2×2 PCF | 16 | 4 |

这里减少的是着色器纹理指令调用数；四邻域深度数据仍全部参与计算，不能把 75% 的指令减少当作整帧 FPS 增幅或带宽减少。覆盖率、Toon 阈值、偏移、图集区域钳制和原始深度采样器保持原有语义，自定义 SPIR-V 的资源布局也保持兼容。

Gather 按规范返回 `(i0,j1), (i1,j1), (i1,j0), (i0,j0)`，使用 `wzxy` 恢复原来的两行顺序。采样坐标选在目标四邻域的中心，避免纹理坐标精度在边缘或整数附近选错邻域；插值仍使用原始坐标的权重。[Khronos textureGather 参考](https://registry.khronos.org/OpenGL-Refpages/gl4/html/textureGather.xhtml)

新增 `VeldridPmxPoseUploader`，将 CPU 姿态的位置、法线和可选 UV 复制合并到一个命令列表、一次队列提交。原来每个模型需要两到三次提交及相应屏障。此优化适用于 CPU 蒙皮和 Compute 回退；DemoGame05 正常使用 Compute 时，主要收益来自阴影采样。

上传器保留三个独立 staging slot，复用前等待该 slot 的 fence，复制前后保留内存屏障。GPU 目标缓冲区仍为原来的 device-local 对象，Compute 绑定无需变更；未上传 UV 时保留旧 UV。释放上传器前等待尚未完成的复制。没有引入每帧 `WaitForIdle`，也没有移除呈现信号量或帧 slot 的保护。

Android 启动日志增加 `startupPatch=6; shadowFilter=bilinear-gather; cpuPoseUpload=batched`，并修正 `frameSync=frame-slot-fence` 标记。优化代码属于共享 Vulkan 后端，PC Vulkan 也会使用。

## 验证

本机 Intel HD Graphics 520 原生 Vulkan 上，相关 8 项回归通过，覆盖多帧阴影/反射绘制、参数快照、Compute 和大数据 CPU 回退、场景上传。新增验证包括：

- 7×5、1×1、16×8 的 R32 与深度附件纹理，共 36,864 个像素，逐像素对比独立 CPU 双线性比较参考，含二维非对称图案、边界外坐标和深度相等情况；误差不超过 2/255。
- 方向光、点光源和射灯的平滑及 Toon 模式实际 PMX 绘制回读。
- 100,012 个顶点、18 次连续姿态提交，逐顶点检查位置、法线和隔帧不更新的 UV，并验证上传器在复制尚未全部结束时释放。
- DemoGame05 教室（19,659 顶点）和人物（52,521 顶点）各三轮加载、真实 Compute 更新及释放；人物 GPU/CPU 位置最大误差约 `3.82e-6`。

```powershell
dotnet run --no-restore --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test Vulkan
dotnet run --no-restore --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --vulkan-project-models 'D:\Projects\CSharp\MMD\GameEditorProjects\DemoGame05'
```

本次未在 Android 手机上安装运行修改后的播放器，不报告手机 FPS 提升数值。更新播放器后可继续使用原有 `.dwgame`。复测应固定场景、相机、方向、画质和手机温度，在加载完成后采集至少 30 秒的 `Vulkan frame profile`；其中 `fence/update/record/present` 为 CPU 耗时，包含对应调用中的等待，不是 GPU 时间戳。

## 安装包

Android Release ARM64 构建成功，0 错误；裁剪后的 Vulkan 启动、同步补丁及加载器检查通过。APK v2/v3 签名验证通过。构建保留 NuGet 漏洞元数据源不可访问及既有 SDL 原生库页面大小/重复打包警告。

- [签名 APK](../artifacts/android/GamePlayer-arm64-20261109-vulkan-shadow-pose-Signed.apk)
- 大小：109,126,862 字节。
- SHA256：`D176DC2F64A6B017FD9BB788A8349F9D1768931B4B609B5C84AA146CFAA54D16`。

```powershell
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android -c Release -r android-arm64 -t:SignAndroidPackage -p:AndroidPackageFormats=apk -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" -p:JavaSdkDirectory="$env:LOCALAPPDATA\Android\jdk" --no-restore
```
