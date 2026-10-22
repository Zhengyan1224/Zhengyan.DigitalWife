# GamePlayer Android：在手机上运行游戏包

这是原生 Android 播放器。它读取 GameEditor 导出的 `.dwgame`，运行场景、MMD 角色、C# 脚本、触摸界面、音频和 AI 对话。

**只想玩游戏：安装提供者给出的 APK，打开 `.dwgame` 即可，不需要安装 .NET 或 Android Studio。** 下方开发环境步骤适用于自己从源码编译 APK。

## 安装后怎么打开游戏

1. 安装适合手机架构的播放器 APK。多数现代 Android 手机使用 ARM64。
2. 启动 GamePlayer，系统文件选择器会打开。
3. 选择 `.dwgame`。也可以在文件管理器中使用“打开方式”或“分享”传给 GamePlayer。
4. 加密包会弹出密码框；输入导出时的密码。
5. 出现加载画面后进入游戏。语音交互需要允许麦克风权限。

分包文件形如 `Game.dwgame.001`、`.002`。一次选取或分享全部分片，避免只拿到第一片。游戏包是内容，APK 是播放器，二者不能互相替代。

横竖屏由包内窗口宽高决定：宽大于高为横屏，高大于宽为竖屏。游戏界面相对布局以工程窗口尺寸为基准缩放。

## Android 与 PC 的差异

| 功能 | Android 当前方式 |
| --- | --- |
| 渲染 | GLES / Vulkan；Auto 尝试 Vulkan，初始化失败可回退 GLES |
| 场景、MMD、特效 | 共用部分 PC 渲染与运行时代码；设备画面和性能仍需验证 |
| 脚本 | C#；PC Python 脚本不支持 |
| 触摸 | 原生多指输入、触控笔、快速按下/释放/取消，脚本使用共用 Touch API |
| 游戏 GUI / 软键盘 | 原生 Android View / Canvas / IME |
| 本地 ASR / TTS | 系统 SpeechRecognizer / TextToSpeech，识别器可能依赖网络 |
| RealtimeVoice | Android 原生采音/播放，服务协议与 PC 一致 |
| 普通场景音频 | Android MediaPlayer；PC OpenAL 空间音频并非全部等价 |
| Skills 命令 | Android shell 和应用权限；不自带桌面命令或 Python |
| 桌面精灵 / 托盘 | 不提供 |
| OpenCL | 不启用；Vulkan 可选 Compute 并回退 CPU |

目标最低系统是 Android 7.0 / API 24，运行还需要兼容的图形和原生库。最低系统版本不是对所有设备的兼容承诺。

[当前能力对照](../../docs/android-render-parity.md)是实现范围说明；[历史架构计划](../../docs/android-gameplayer.md)含早期审计，部分旧限制已经变化。

## 游戏制作者：先做兼容检查

在 PC GameEditor 的 `Project -> Package / Publish` 中：

1. 点击 `Check Android Compatibility`。
2. 把 Python 游戏逻辑改成 C#，处理桌面窗口功能及 shader 差异。
3. 启用 Android C# 预编译并导出。
4. 更新 Android 播放器后在真机打开包。

编译后的脚本位于 `compiled/android/`；源码保留为兼容回退。使用新脚本 API 的包需用新版编辑器重新导出，新包也需要匹配的播放器。

可直接使用 `PlatformName == "Android"` 或 `IsAndroid`，以及 `Input.Touches / TouchTransitions / TryGetTouch`。查看[Touch 示例](../Zhengyan.DigitalWife.GameEditor/api_docs/09-input.md)和[平台标识](../Zhengyan.DigitalWife.GameEditor/api_docs/03-system-api.md)。

## 从源码编译：先认识工具

| 工具 | 用途 |
| --- | --- |
| .NET 10 SDK | 编译 C# |
| .NET Android workload | 让 dotnet 能编译 Android |
| Android SDK | 平台 API、Build-Tools 和 adb |
| JDK 21 | Android 打包工具需要的 Java 开发环境 |
| adb | 安装 APK、查看设备和 logcat |

Android Studio 可用于安装 SDK 和模拟器，但不是本项目的代码构建入口。C# 项目仍由 dotnet、Visual Studio 或 Rider 构建。

以下命令在[仓库根目录](../../README.md)的 PowerShell 执行。Linux / macOS 开发机需调整 SDK/JDK 路径及 adb 文件名。

### 1. 安装 .NET Android workload

```powershell
dotnet --list-sdks
dotnet workload install android
dotnet workload list
```

SDK 应包含 10.x，workload 列表应有 android。安装失败先查看网络和权限错误，不要直接跳过。

### 2. 准备 Android SDK 与 JDK

可通过 Android Studio SDK Manager 安装 SDK，或从 [Android Command-line Tools](https://developer.android.com/studio#command-tools)安装命令行工具。

仓库当前使用过的构建组合为 JDK 21、Android API 36 和 Build-Tools 36.0.0；具体所需版本以已安装 .NET Android workload 的构建提示为准。

在 Windows 上，SDK 常在 `%LOCALAPPDATA%/Android/Sdk`。JDK 必须指向实际安装的 JDK 21，下面路径只是示例，不能假设它已经存在：

```powershell
$sdk = "$env:LOCALAPPDATA/Android/Sdk"
$jdk = "$env:LOCALAPPDATA/Android/jdk"
Test-Path "$sdk/platform-tools/adb.exe"
Test-Path "$jdk/bin/java.exe"
& "$jdk/bin/java.exe" -version
```

前两项应为 True，Java 版本应为 21。若 JDK 安装在其它位置，修改 `$jdk`。

使用命令行 SDK Manager 时，确认目录为 `cmdline-tools/latest/bin/` 后执行：

```powershell
$env:JAVA_HOME = $jdk
$sdkManager = "$sdk/cmdline-tools/latest/bin/sdkmanager.bat"
& $sdkManager "--sdk_root=$sdk" "platform-tools" "platforms;android-36" "build-tools;36.0.0"
& $sdkManager "--sdk_root=$sdk" --licenses
```

接受许可后检查 SDK 包是否安装完成。模拟器与系统镜像仅在需要模拟器时安装。

### 3. 构建可直接安装的 ARM64 APK

先还原，再编译和签名；两次使用相同配置与架构：

```powershell
dotnet restore src/Zhengyan.DigitalWife.GamePlayer.Android -r android-arm64 -p:Configuration=Release -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$jdk"

dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android -c Release -r android-arm64 -t:SignAndroidPackage -p:AndroidPackageFormats=apk -p:EmbedAssembliesIntoApk=true -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$jdk" --no-restore
```

产物：

```text
src/Zhengyan.DigitalWife.GamePlayer.Android/bin/Release/net10.0-android/android-arm64/com.zhengyan.digitalwife.gameplayer-Signed.apk
```

直接分享或安装 Debug APK 时也要带 `-p:EmbedAssembliesIntoApk=true`，否则快速部署产物可能依赖 IDE 另行推送程序集，单个 APK 不完整。

上面生成的是开发测试签名。正式分发需配置自己的 keystore；签名变化会影响已有应用能否覆盖升级。当前项目默认关闭 AOT 以兼容动态游戏脚本及现有运行时；不要仅为体积优化随意打开它。

### 4. 连接手机并安装

打开手机“开发者选项”和“USB 调试”，连接后在手机允许调试授权。

```powershell
$adb = "$sdk/platform-tools/adb.exe"
& $adb devices -l
& $adb install -r "src/Zhengyan.DigitalWife.GamePlayer.Android/bin/Release/net10.0-android/android-arm64/com.zhengyan.digitalwife.gameplayer-Signed.apk"
```

设备状态应为 `device`。安装后在手机打开 GamePlayer，选择游戏包。x86_64 模拟器需要用 `android-x64` 架构构建对应 APK，不能安装 ARM64 包。

## 手机连接电脑上的语音 / LLM 服务

手机的 `127.0.0.1` 指手机自己。连接电脑服务时：

- 同一局域网：在包内配置电脑的局域网 IP，服务监听可访问网卡并开放端口。
- USB 调试：测试时可使用 adb reverse 把手机端口转发到电脑。

以 RealtimeVoice 端口 5058 为例：

```powershell
& $adb reverse tcp:5058 tcp:5058
```

此时手机访问 `http://127.0.0.1:5058` 会转发到电脑对应端口。只对正在连接的调试设备有效。远端服务要保持运行；HTTPS 证书也需要被手机信任。

[RealtimeVoice 服务说明](../../samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)有完整启动步骤。

## 语音报错与日志

Android 的本地 ASR 使用系统服务，不会自动加载包内 PC Whisper / Sherpa 模型。系统识别器网络超时并不等于 LLM 服务失败。

后台唤醒监听遇到 NetworkTimeout / Network 会退避重启；尚未检测到说话的续聊超时保留脚本原期限。已检测到问题后的转写失败仍报告给脚本。恢复逻辑不能修复系统服务的网络，断网期间唤醒可能不可用。

查看引擎和崩溃日志：

```powershell
& $adb logcat -v threadtime ZhengyanGamePlayer:V AndroidRuntime:E MonoDroid:V mono-rt:E '*:S'
```

只看 `ZhengyanGamePlayer` 可能漏掉 AndroidRuntime 崩溃堆栈。复现前可用 `& $adb logcat -c` 清掉旧日志。日志反馈请隐去密钥和私人对话。

## 自定义 APK 名称与图标

APK 名称来自项目 `ApplicationTitle`，图标来自构建属性 `AndroidApplicationIconPath`。可在 build 命令追加：

```text
-p:ApplicationTitle="我的游戏播放器" -p:AndroidApplicationIconPath="D:/Icons/my-game-icon.png"
```

图标文件应存在。APK 图标是构建时资源，与加载的游戏窗口图标不同。

## 常见问题

| 现象 | 处理方式 |
| --- | --- |
| NETSDK1147 | 安装 Android workload |
| XA5300 / 找不到 SDK | 检查路径并显式传入 AndroidSdkDirectory |
| 找不到 Java / JDK 版本不符 | 确认 JDK 21 并传入 JavaSdkDirectory |
| adb unauthorized | 解锁手机并允许 USB 调试 |
| INSTALL_FAILED_NO_MATCHING_ABIS | 选择与手机/模拟器匹配的 RuntimeIdentifier |
| 安装覆盖失败 | 核对包名、版本、签名和系统错误，不要先卸载丢失存档 |
| 找不到 dwgame | 使用系统文件选择器，分包需全部选择 |
| 黑屏 / 图形初始化失败 | 查看 logcat、设备图形能力和包内后端设置 |
| APK 单独安装缺程序集 | 使用 EmbedAssembliesIntoApk=true 重新构建 |
| 16 KB 页 / 原生库警告 | 检查打包进 APK 的原生库兼容性，在目标 Android 设备验证 |
| 语音输入不可用 | 系统权限、识别/TTS服务和它们的网络或语言资源 |
| 新脚本 API 不存在 | 更新播放器并用新版编辑器重新导出 |

## 开发验证入口

共享回归可在 PC 上运行，不需要连接手机：

```text
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests
```

Release 裁剪后的脚本 API 检查：

```text
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --verify-linked-script-apis src/Zhengyan.DigitalWife.GamePlayer.Android/obj/Release/net10.0-android/android-arm64/linked
```

这些检查不能替代真机麦克风、扬声器、软键盘、触摸和显卡测试。

- [项目总览](../../README.md)
- [GameEditor](../Zhengyan.DigitalWife.GameEditor/README.md)
- [当前能力与验证范围](../../docs/android-render-parity.md)
