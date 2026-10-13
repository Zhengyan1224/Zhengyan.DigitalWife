# Android Vulkan 复杂场景优化（2026-10-13）

用户确认上一版已消除 Android Vulkan 闪烁、帧率也有提升，但 DemoGame01 首场景在手机上仍只有约 5–8 FPS，GLES 约为 10–15 FPS。本次接续工作区中未提交的优化，保留已有呈现和资源同步机制。

## 代码与场景检查

DemoGame01 的 `scenes/main.scene.json` 包含三个 PMX 模型、一块开启反射的水面、雨/樱花/云三组粒子（900 + 360 + 260）和一张背景图片。项目的 Android 画质为 auto，渲染目标预算为 96 MB。

- GLES 的反射贴图长边原来限制为 1024（low 为 512），Vulkan 仅根据显存预算缩小，可能仍按手机视口的完整分辨率绘制。两者的反射工作量并不相同。例如 2400×1080 的反射改为 1024×461 后，反射像素数减少约 82%；这不是整帧性能提升比例，也不是手机实测数据。
- 模型主通道已使用独立 uniform 快照，但水面、粒子、天空、背景图片和局部清屏仍调用 `CommandList.UpdateGraphicsBuffer`。这些拷贝会结束正在进行的 render pass，再通过屏障保护读写。在移动分块 GPU 上，这类中断可能增加颜色/深度附件的保存和加载开销。
- 不能直接把原来的上传改成对同一 buffer 的主机写入：同一帧可能包含阴影、反射、多个相机或多张图片，后写入的数据会覆盖此前录制的绘制参数或顶点。

## 修改

1. `AndroidRenderPolicy.ResolveReflectionSize` 统一 Android 两个后端的反射尺寸：正常档位长边最多 1024，low 最多 512，保持宽高比，不放大小视口。Vulkan 原有预算约束仍可继续缩小。
2. 水面、粒子、天空、图片和局部清屏的参数改用 `VeldridUniformArena` 的独立区间；水波纹使用相同机制上传整个参数数组。描述符按实际 buffer 缓存，通过动态偏移绑定。
3. 新增 `VeldridVertexArena`，水面、粒子和图片的顶点按次绘制分配独立区间。分页按三个 frame slot 复用，扩容时保留旧页，避免此前录制的绘制引用失效。
4. 日志增加 `vertexSnapshots`、`vertexUploadKB`，启动日志增加 `reflectionSizeLimit`，版本标记为 `startupPatch=5; ...; sceneUploads=draw-snapshots; reflections=android-size-limit`。

这些改动保留每图像呈现信号量、frame fence、更新前等待和 Compute 屏障。共享图形后端中的上传优化也适用于桌面 Vulkan；反射尺寸限制只应用于 Android。

## 验证与复测

新增原生 Vulkan 像素回读回归覆盖 60 连续帧、三个 frame slot、从非零 slot 创建资源、粒子阴影、主相机与反射相机、水面与天空参数隔离、不同位置和透明度的多张图片，以及顶点/参数分页扩容。原有 PMX 参数快照、Compute 和 CPU 回退上传回归继续保留。

完整 Android runtime 回归 **24/24 通过**，本地 GPU 为 Intel HD Graphics 520。
裁剪后的 Veldrid 通过 240 帧异步呈现验证（包含图像复用、重建、释放与错误分支），以及压缩 GC 下 12 次原生设备创建/资源分配验证；裁剪后的加载器和同步补丁检查也通过。

Release ARM64 APK 构建成功，0 错误，保留原有 SDL 页面大小/重复打包的 4 项警告。APK v2/v3 签名验证通过，AOT 模块为 0。

- 安装包：`artifacts/android/GamePlayer-arm64-20261013-vulkan-scene-Signed.apk`
- 大小：57,836,241 字节。
- SHA256：`EEECFFD3411338FC1DB4281EF986F7298705E6F8CB645ED2FA5962574D0F6676`。

构建命令：

```powershell
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android -c Release -r android-arm64 -t:SignAndroidPackage -p:AndroidPackageFormats=apk -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" -p:JavaSdkDirectory="$env:LOCALAPPDATA\Android\jdk" --no-restore
```

本地 ADB 没有连接 Android 设备，桌面 GPU 回归无法给出用户手机上的提速幅度。安装更新后的 Android 播放器后可继续使用现有 `.dwgame`。比较时保持相同画质、窗口方向、相机位置和场景状态，等待加载结束后记录至少 30 秒，观察水面、人物、粒子、暂停恢复和切换场景。

```powershell
adb logcat -v time | Select-String 'Vulkan frame profile|Android graphics backend|startupPatch'
```

`Vulkan frame profile` 约每五秒输出一次：`fence`、`update`、`record`、`present` 是 CPU 计时，包含调用中的等待；阴影和反射项是录制时间的子集，不是 GPU 时间戳。若 `fence/present` 占主导，下一步需要手机 GPU 分析；若 `update` 占主导，应检查蒙皮、物理、粒子更新；若 `record` 占主导，应检查绘制次数和命令录制成本。

## Vulkan 是否一定更快

Vulkan 可以减少驱动 CPU 开销，并提供更多优化控制，但不能保证每个场景都比 GLES 快。着色、透明叠加、反射分辨率、内存带宽和同步方式都会影响结果。Android 的 Vulkan 优势说明见 [Android 官方文档](https://developer.android.com/games/develop/vulkan/overview)；分块 GPU 的附件加载/保存开销见 [Khronos render pass 性能示例](https://docs.vulkan.org/samples/latest/samples/performance/render_passes/README.html)。
