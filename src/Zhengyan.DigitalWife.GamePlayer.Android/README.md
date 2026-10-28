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
| 本地 ASR / TTS | 工程内 Sherpa CPU 推理；原生 AudioRecord / AudioTrack 采音与播放，不调用系统语音服务 |
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
| JDK 17 或 21 | Android 打包工具需要的 Java 开发环境；当前已使用 JDK 17.0.14 验证构建 |
| adb | 安装 APK、查看设备和 logcat |

Android Studio 可用于安装 SDK 和模拟器，但不是本项目的代码构建入口。C# 项目仍由 dotnet、Visual Studio 或 Rider 构建。

以下命令均在[仓库根目录](../../README.md)执行：Windows 使用 PowerShell，Linux 使用 Bash，macOS 使用终端中的 zsh 或 Bash。按自己的系统完成环境准备，并在同一个终端继续构建，保留后面使用的 `sdk`、`jdk` 和 `adb` 变量。

### 1. 安装 .NET Android workload

#### Windows

先安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，再执行：

```powershell
dotnet --list-sdks
dotnet workload install android
dotnet workload list
```

#### Linux

下面以 **Ubuntu 24.04 / Linux Mint 22 的 x86_64 开发机**为例。其它发行版先按 [.NET Linux 安装文档](https://learn.microsoft.com/dotnet/core/install/linux)准备对应的系统依赖；Android SDK 的主机工具也需要匹配开发机架构。

安装下载工具和 .NET 系统依赖：

```bash
sudo apt-get update
sudo apt-get install -y git curl unzip ca-certificates libc6 libgcc-s1 \
  libgssapi-krb5-2 libicu74 libssl3t64 libstdc++6 tzdata zlib1g
```

使用[官方安装脚本](https://learn.microsoft.com/dotnet/core/install/linux-scripted-manual)将 .NET 10 SDK 安装到当前用户目录，再安装 Android workload。如果已经安装 .NET 10 SDK，可跳过 SDK 安装及环境变量配置，直接执行末尾的 `dotnet` 命令。

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/digitalwife-dotnet-install.sh
bash /tmp/digitalwife-dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

dotnet --list-sdks
dotnet workload install android
dotnet workload list
```

用户目录安装不需要 `sudo`。将上述 `DOTNET_ROOT` 和 `PATH` 两行配置加入 `~/.bashrc`，新终端才会继续使用该 SDK。如果使用系统目录中的 SDK，workload 安装可能需要管理员权限；用该 SDK 的完整 `dotnet` 路径执行 `sudo /实际路径/dotnet workload install android`，确保安装到构建时使用的同一套 SDK。

#### macOS

从 [.NET 10 下载页](https://dotnet.microsoft.com/download/dotnet/10.0)安装 **SDK**：Apple Silicon（M 系列）选择 **Arm64**，Intel Mac 选择 **x64**。JDK 也选择与开发机匹配的架构，选择依据见 [.NET macOS 安装文档](https://learn.microsoft.com/dotnet/core/install/macos)。使用官方 `.pkg` 安装到系统目录后执行：

```bash
dotnet --list-sdks
sudo dotnet workload install android
dotnet workload list
```

如果 SDK 安装在当前用户可写的目录中，workload 安装不加 `sudo`；还原和构建也使用当前用户执行。

三个系统都应确认 SDK 列表包含 `10.x`，在仓库根目录运行 `dotnet --version` 时实际选中 `10.x`，workload 列表中有 `android`。当前 `global.json` 允许从旧版本向更高主版本滚动，但 .NET 9 不能编译本项目。安装失败先查看网络和权限错误，不要直接跳过。

### 2. 准备 Android SDK 与 JDK

三个系统都可通过 Android Studio 的 SDK Manager 安装 SDK，或从 [Android Command-line Tools](https://developer.android.com/studio#command-tools)下载对应系统的命令行工具。

当前 .NET Android workload（36.1.69）的最低要求是 JDK 17，JDK 21 也在支持范围内。Windows 开发机已使用 JDK 17.0.14、Android API 36 和 Build-Tools 36.0.0 完整构建，本机 Android SDK Command-line Tools 19.0 也已在 JDK 17 下验证运行。

下面的安装示例以 JDK 17 为准；已安装 JDK 21 时也可以使用其实际路径。具体所需版本以已安装 .NET Android workload 和 Android SDK 命令行工具的版本要求为准。

在 SDK Manager 中安装 `Android SDK Platform 36`、`Android SDK Build-Tools 36.0.0`、`Android SDK Platform-Tools` 和 `Android SDK Command-line Tools (latest)`，并记下界面显示的 **Android SDK Location**。手动解压命令行工具时，按 [sdkmanager 官方说明](https://developer.android.com/tools/sdkmanager)整理为以下结构，避免多套一层 `cmdline-tools`：

```text
<Android SDK>/
  cmdline-tools/
    latest/
      bin/        # sdkmanager / sdkmanager.bat
      lib/
      NOTICE.txt
      source.properties
```

#### Windows：SDK / JDK 路径

在 Windows 上，SDK 常在 `%LOCALAPPDATA%/Android/Sdk`。JDK 路径应指向实际安装的 JDK 17 或 21，下面路径只是示例，不能假设它已经存在：

```powershell
$sdk = "$env:LOCALAPPDATA/Android/Sdk"
$jdk = "$env:LOCALAPPDATA/Android/jdk"
Test-Path "$sdk/platform-tools/adb.exe"
Test-Path "$jdk/bin/java.exe"
& "$jdk/bin/java.exe" -version
```

前两项应为 True，Java 版本应为 17 或 21。若 JDK 安装在其它位置，修改 `$jdk`。

使用命令行 SDK Manager 时，确认目录为 `cmdline-tools/latest/bin/` 后执行：

```powershell
$env:JAVA_HOME = $jdk
$sdkManager = "$sdk/cmdline-tools/latest/bin/sdkmanager.bat"
& $sdkManager "--sdk_root=$sdk" "platform-tools" "platforms;android-36" "build-tools;36.0.0"
& $sdkManager "--sdk_root=$sdk" --licenses
```

#### Linux：安装 JDK 17 并配置路径

Ubuntu 24.04 / Linux Mint 22 可执行：

```bash
sudo apt-get install -y openjdk-17-jdk
sdk="$HOME/Android/Sdk"
jdk="/usr/lib/jvm/java-17-openjdk-amd64"
```

`$HOME/Android/Sdk` 是 Android Studio 常用路径；手动安装 SDK 时使用自己选择的目录。上面的 JDK 路径适用于 x86_64 的 Debian / Ubuntu 包，使用其它架构、发行版或 JDK 发行包时，将 `jdk` 改为实际 JDK 17 或 21 根目录。

#### macOS：安装 JDK 17 并配置路径

可安装 [Eclipse Temurin 17](https://adoptium.net/temurin/releases/?version=17) 的 macOS JDK；已经安装 Homebrew 的开发机也可使用 [temurin@17](https://formulae.brew.sh/cask/temurin@17)：

```bash
brew install --cask temurin@17
```

安装后配置路径，`java_home` 会定位已注册的 JDK 17；使用 JDK 21 时将 `-v 17` 改为 `-v 21`：

```bash
sdk="$HOME/Library/Android/sdk"
jdk="$(/usr/libexec/java_home -v 17)"
```

SDK 路径以 Android Studio 的 **Android SDK Location** 或手动安装目录为准。`jdk` 应指向包含 `bin/java` 和 `bin/javac` 的 JDK 根目录。

#### Linux / macOS：检查工具、安装 SDK 包并接受许可

完成对应系统的 `sdk` 和 `jdk` 配置后，在同一个终端执行：

```bash
export JAVA_HOME="$jdk"
export ANDROID_HOME="$sdk"
export PATH="$JAVA_HOME/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/cmdline-tools/latest/bin:$PATH"

test -x "$jdk/bin/java"
test -x "$jdk/bin/javac"
"$jdk/bin/java" -version
test -x "$sdk/cmdline-tools/latest/bin/sdkmanager"

sdkManager="$sdk/cmdline-tools/latest/bin/sdkmanager"
"$sdkManager" "--sdk_root=$sdk" "platform-tools" "platforms;android-36" "build-tools;36.0.0"
"$sdkManager" "--sdk_root=$sdk" --licenses

test -d "$sdk/platforms/android-36"
test -d "$sdk/build-tools/36.0.0"
"$sdk/platform-tools/adb" version
```

`test` 成功时无输出，退出码为 0；失败时先修正路径或补装对应工具再继续。Java 版本应为 17 或 21，最后应显示 adb 版本。根据提示逐项阅读并接受许可。模拟器与系统镜像仅在需要模拟器时安装。

上面的环境变量只对当前终端有效。需要长期使用时，将实际 SDK/JDK 路径以及 `JAVA_HOME`、`ANDROID_HOME`、`PATH` 配置加入 Linux 的 `~/.bashrc` 或 macOS zsh 的 `~/.zshrc`；重新打开终端后仍需设置构建命令中的 `sdk="$ANDROID_HOME"`、`jdk="$JAVA_HOME"`。

### 3. 构建可直接安装的 ARM64 APK

先还原，再编译和签名；两次使用相同配置与架构。这里的 `android-arm64` 指手机架构，与开发机是 Intel、AMD 还是 Apple Silicon 无关。

Windows PowerShell：

```powershell
dotnet restore src/Zhengyan.DigitalWife.GamePlayer.Android -r android-arm64 -p:Configuration=Release -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$jdk"

dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android -c Release -r android-arm64 -t:SignAndroidPackage -p:AndroidPackageFormats=apk -p:EmbedAssembliesIntoApk=true -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$jdk" --no-restore

dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android/Zhengyan.DigitalWife.GamePlayer.Android.csproj -c Release -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$jdk"
```

Linux / macOS：

```bash
dotnet restore src/Zhengyan.DigitalWife.GamePlayer.Android -r android-arm64 \
  -p:Configuration=Release -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$jdk"

dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android -c Release -r android-arm64 \
  -t:SignAndroidPackage -p:AndroidPackageFormats=apk -p:EmbedAssembliesIntoApk=true \
  -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$jdk" --no-restore

dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android/Zhengyan.DigitalWife.GamePlayer.Android.csproj -c Release -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$jdk"
```

产物：

```text
src/Zhengyan.DigitalWife.GamePlayer.Android/bin/Release/net10.0-android/android-arm64/com.zhengyan.digitalwife.gameplayer-Signed.apk
```

直接分享或安装 Debug APK 时也要带 `-p:EmbedAssembliesIntoApk=true`，否则快速部署产物可能依赖 IDE 另行推送程序集，单个 APK 不完整。

上面生成的是开发测试签名。正式分发需配置自己的 keystore；签名变化会影响已有应用能否覆盖升级。当前项目默认关闭 AOT 以兼容动态游戏脚本及现有运行时；不要仅为体积优化随意打开它。

### 4. 连接手机并安装

打开手机“开发者选项”和“USB 调试”，连接后在手机允许调试授权。

Windows PowerShell：

```powershell
$adb = "$sdk/platform-tools/adb.exe"
& $adb devices -l
& $adb install -r "src/Zhengyan.DigitalWife.GamePlayer.Android/bin/Release/net10.0-android/android-arm64/com.zhengyan.digitalwife.gameplayer-Signed.apk"
```

Linux / macOS：

```bash
adb="$sdk/platform-tools/adb"
"$adb" devices -l
"$adb" install -r "src/Zhengyan.DigitalWife.GamePlayer.Android/bin/Release/net10.0-android/android-arm64/com.zhengyan.digitalwife.gameplayer-Signed.apk"
```

Linux 若提示 `no permissions` 或无法通过 USB 发现设备，Ubuntu / Linux Mint 需要配置 `plugdev` 用户组和设备的 udev 规则，见 [Android 官方真机调试说明](https://developer.android.com/studio/run/device)：

```bash
sudo apt-get install -y android-sdk-platform-tools-common
sudo usermod -aG plugdev "$(id -un)"
```

注销并重新登录使用户组生效，然后重新插拔手机，再运行 `"$adb" devices -l`。其它发行版按其设备权限规则配置。

设备状态应为 `device`。安装后在手机打开 GamePlayer，选择游戏包。x86_64 模拟器需要用 `android-x64` 架构构建对应 APK，不能安装 ARM64 包。

## 手机连接电脑上的语音 / LLM 服务

手机的 `127.0.0.1` 指手机自己。连接电脑服务时：

- 同一局域网：在包内配置电脑的局域网 IP，服务监听可访问网卡并开放端口。
- USB 调试：测试时可使用 adb reverse 把手机端口转发到电脑。

以 RealtimeVoice 端口 5058 为例，Windows PowerShell：

```powershell
& $adb reverse tcp:5058 tcp:5058
```

Linux / macOS：

```bash
"$adb" reverse tcp:5058 tcp:5058
```

此时手机访问 `http://127.0.0.1:5058` 会转发到电脑对应端口。只对正在连接的调试设备有效。远端服务要保持运行；HTTPS 证书也需要被手机信任。

[RealtimeVoice 服务说明](../../samples/Zhengyan.DigitalWife.Samples.RealtimeVoice/README.md)有完整启动步骤。

## 语音报错与日志

Android 本地语音使用包内工程配置的 Sherpa 模型，`Scene.Asr` 录音、问题转写、唤醒词匹配和 `Entity.Speak` 合成都在本机运行。LLM 仍调用工程配置的远程接口。显式使用 `Scene.RealtimeVoice` 的脚本仍是另一条服务端语音链路，不应与本地对话流程混用。

启用 `Asr.Enabled`，选择 `Provider=sherpa`、`Sherpa.Provider=cpu`；TTS 启用 `Voice.Enabled`，选择 `TtsProvider=sherpa-onnx`、`ModelKind=vits/matcha`、`InferenceProvider=cpu`。不支持 Whisper，也不会在模型缺失时自动回退系统识别。模型、tokens、词典和 Matcha vocoder 必须已打进工程；优先使用 `assets/...` 或 `project:assets/...`。已有包若已包含这些 Sherpa 文件可直接更新播放器，缺文件的包需重新导出。

唤醒词用本地 ASR 文本匹配，不需要额外关键词模型。流式模型匹配 partial 时可唤醒/打断；非流式模型在一段话结束后匹配。静音不会提前关闭续聊，30 秒等期限仍由脚本控制。`Asr.EndpointSilenceSeconds`（默认 0.9）控制说话后的停顿分句，`SpeechRmsThreshold`（默认 0.008）控制人声能量阈值。常驻静音只保留短预录缓冲，不会无限积累识别音频。

TTS 保留角色说话和完成回调 API，回调等待原生播放排空，取消不派发旧完成事件；口型按实际合成音频时长驱动。加载模型与推理在后台运行。CPU 模型与 3D 渲染共享手机资源，首次预热、长句合成、内存与发热需真机验证。

日志会显示 `ASR local start / event`、`TTS local synthesized / playback complete` 及模型路径错误。旧版系统识别器的 `NetworkTimeout` 恢复路径已移除；本地模型不可用会明确报告，不发起网络识别或下载。

查看引擎和崩溃日志，Windows PowerShell：

```powershell
& $adb logcat -v threadtime ZhengyanGamePlayer:V AndroidRuntime:E MonoDroid:V mono-rt:E '*:S'
```

Linux / macOS：

```bash
"$adb" logcat -v threadtime ZhengyanGamePlayer:V AndroidRuntime:E MonoDroid:V mono-rt:E '*:S'
```

只看 `ZhengyanGamePlayer` 可能漏掉 AndroidRuntime 崩溃堆栈。复现前可用 `& $adb logcat -c`（PowerShell）或 `"$adb" logcat -c`（Linux / macOS）清掉旧日志。日志反馈请隐去密钥和私人对话。

## 自定义 APK 名称与图标

APK 名称来自项目 `ApplicationTitle`，图标来自构建属性 `AndroidApplicationIconPath`。可在 build 命令追加：

```text
-p:ApplicationTitle="我的游戏播放器" -p:AndroidApplicationIconPath="D:/Icons/my-game-icon.png"
```

图标文件应存在；Linux / macOS 将示例的 `D:/Icons/my-game-icon.png` 换成实际路径，例如 `/home/user/Icons/my-game-icon.png` 或 `/Users/user/Icons/my-game-icon.png`。APK 图标是构建时资源，与加载的游戏窗口图标不同。

## 常见问题

| 现象 | 处理方式 |
| --- | --- |
| NETSDK1147 | 安装 Android workload |
| XA5300 / 找不到 SDK | 检查路径并显式传入 AndroidSdkDirectory |
| 找不到 Java / JDK 版本不符 | 确认 JDK 17 或 21，检查所用工具链的版本要求，并传入实际 JavaSdkDirectory |
| adb unauthorized | 解锁手机并允许 USB 调试 |
| Linux adb no permissions | 配置 plugdev 用户组与 udev 规则，重新登录并插拔手机 |
| INSTALL_FAILED_NO_MATCHING_ABIS | 选择与手机/模拟器匹配的 RuntimeIdentifier |
| 安装覆盖失败 | 核对包名、版本、签名和系统错误，不要先卸载丢失存档 |
| 找不到 dwgame | 使用系统文件选择器，分包需全部选择 |
| 黑屏 / 图形初始化失败 | 查看 logcat、设备图形能力和包内后端设置 |
| APK 单独安装缺程序集 | 使用 EmbedAssembliesIntoApk=true 重新构建 |
| 16 KB 页 / 原生库警告 | 检查打包进 APK 的原生库兼容性，在目标 Android 设备验证 |
| 语音输入不可用 | 麦克风权限、包内模型文件、CPU provider 与识别阈值 |
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
