# Android Vulkan 启动、脚本引用与 TTS 口型修复

对应 2026-10-04 的 DemoGame01、DemoGame05、DemoGame06 反馈。

## Vulkan 启动

提供的日志停在 `Android graphics backend request`，尚未到场景资源加载，且没有 native crash buffer。
因此不能仅凭该日志把手机闪退归因到某一条原生调用，也不能把本机 Vulkan 测试当成 Redmi 实机验证。

代码审查发现 [Veldrid 4.9 的 CreateLogicalDevice](https://github.com/veldrid/veldrid/blob/v4.9.0/src/Veldrid/Vk/VkGraphicsDevice.cs)
在 `fixed (VkExtensionProperties* properties = props)` 中保存扩展名称指针，但在
`vkCreateDevice` 消费这些指针之前结束了 `fixed`。其间仍有托管分配，Mono 移动式 GC 可能使指针失效。

`tools/VeldridAndroidPatch` 在构建时修补独立的 Veldrid 副本：

- 在取得扩展数组后用 `GCHandleType.Pinned` 固定，并在覆盖后续设备创建的 `finally` 中释放。
- Veldrid 使用 Vulkan 1.0 实例和显式启用的 KHR 扩展，相关查询使用 KHR 入口。
- 交换链从 `supportedCompositeAlpha` 选择模式，避免 [上游硬编码 Opaque](https://github.com/veldrid/veldrid/blob/v4.9.0/src/Veldrid/Vk/VkSwapchain.cs)。
- 为实例、Surface、物理设备、逻辑设备、交换链以及关键 native 调用添加 `ZhengyanGamePlayer` 标签的阶段日志。

补丁进入 Android 裁剪之前的 `ResolvedFileToPublish`（显式设置 `PostprocessAssembly=true`），
并替换裁剪器的 Veldrid 引用路径。构建检查裁剪输入和产物中的补丁，遗漏时直接报错。
不修改 NuGet 缓存或桌面播放器的 Veldrid。
之前的 JIT/禁用 AOT 兼容措施、上一帧 fence 等待、compute/copy/graphics 内存屏障均保留。
本次没有删掉防闪烁所需的 GPU 同步。

验证工具会实际加载修补的程序集，在持续强制 compacting GC 下连续创建设备、分配缓冲区 12 次，
并检查交换链透明度模式选择。该测试已在本机原生 Vulkan 上通过。

```powershell
dotnet build tools/VeldridAndroidPatch -c Release
dotnet tools/VeldridAndroidPatch/bin/Release/net10.0/VeldridAndroidPatch.dll <原始Veldrid.dll> <修补Veldrid.dll>
dotnet tools/VeldridAndroidPatch/bin/Release/net10.0/VeldridAndroidPatch.dll --verify <修补Veldrid.dll>
```

## DemoGame06 脚本不执行

`clicked` 已正常派发；脚本在执行前因自动导入的 `System.Net.Http` 没有元数据引用而编译失败。
`using System.Net.Http` 本身不会迫使 Android 提前加载该程序集。桌面扫描整个框架目录会掩盖问题。

新增 `AndroidScriptMetadata` 明确加载默认导入涉及的程序集及依赖，直接使用内存中的元数据，
引用一次生成并缓存。Android host 和 Android 引用回归测试使用同一入口。
裁剪配置也保留脚本动态使用的 HTTP、Socket、JSON、Regex API。
无需改动游戏脚本，旧 `.dwgame` 的运行时编译回退仍可使用。

已使用该内存引用入口编译项目原始脚本：DemoGame01 11 个、DemoGame05 3 个、DemoGame06 3 个，全部通过。

## TTS 口型

Android 之前只接了播放完成通知，没有接人物的 `SpeechTransformUpdater`。
现在 `Entity.Speak` / 回调语音通过 Android TTS 的 `OnStart` 启动人物口型，
支持 `OnRangeStart` 的系统语音引擎还会按当前文本范围切换嘴形；不提供范围回调时，
按文本字典和语速持续驱动口型，直到播放结束。

使用项目 `Voice.LipSync` 的开关、语言、嘴形映射和回退元音配置。
字典可来自项目，也可使用 APK 解压后的内置中/日/英字典。
所有模型更新都投递到渲染线程。
完成、失败、停止、替换语音及切换场景会停止口型；旧请求回调不会停止新请求的口型。
移除 transform updater 时标记姿态需要更新，确保暂停人物的闭嘴状态也能上传到顶点缓冲区。
播放完成回调仍只在成功播放结束后触发。

18 项运行时回归测试全部通过，包括默认导入 API 的编译和执行、口型张开/范围切换/停止复位、
已有的 TTS 初始化重试、原生 Vulkan 连续帧渲染和脚本发布测试。

## 构建与实机复测

```powershell
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android -c Release -r android-arm64 -t:SignAndroidPackage -p:AndroidPackageFormats=apk
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests
```

ARM64 APK 输出在 `src/Zhengyan.DigitalWife.GamePlayer.Android/bin/Release/net10.0-android/android-arm64/`。
更新播放器即可使用这次修复，不需要为脚本引用或口型问题重新导出 `.dwgame`。

2026-10-05 Release ARM64 构建成功。最终签名 APK 为 57,828,049 字节，v2/v3 签名校验通过；
已从 APK 中取出 assembly store、解压其中的 Veldrid 并通过补丁检查，也确认包内有
`System.Net.Http` 和更新后的脚本/播放器程序集，AOT 模块数为 0。
SHA-256：`5CDA0152EB1EADD569E471CA60E1CF7C83C84A77651EB0B280CF8C9984B16C27`。
构建仍有原有 SDL 库的 16 KB 对齐和重复打包警告（XA0141 / XA4301），没有新增编译警告。

当前工作机未连接 adb 设备。需在手机上复测三个 Vulkan 项目、DemoGame06 的跳舞按钮和 DemoGame01 的 TTS 口型。
若 Vulkan 仍闪退，保存同一次运行的完整日志，包含新增启动阶段和 native crash buffer：

```powershell
adb logcat -b all -v threadtime > android-vulkan-full.txt
# 另一个终端在复现闪退后执行：
adb logcat -b crash -d > android-vulkan-crash.txt
```

不要仅按 `ZhengyanGamePlayer` 过滤崩溃日志，否则会漏掉 libc、DEBUG、AndroidRuntime、Vulkan driver 的调用栈。
