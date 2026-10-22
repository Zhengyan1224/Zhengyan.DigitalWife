# Android GamePlayer 主机

本项目是 Zhengyan DigitalWife 共享运行时的 Android 原生主机，目前提供以下能力：

2026-10-22：C# 脚本 `Input` 已暴露原生多点 Touch 快照、稳定 ID、像素位移和按下/释放/取消
记录，与 PC 共用 `TouchPoint / TouchPhase`。`TouchTransitions` 保留两帧之间快速点击的事件顺序，
`Touches` 用于持续移动。平台可通过全局 `PlatformName / IsAndroid / IsWindows / IsLinux / IsMacOS`
判断。新增 API 的脚本需使用新版编辑器重新导出，并更新播放器。
示例见 [Input 文档](../Zhengyan.DigitalWife.GameEditor/api_docs/09-input.md)。

2026-09-28：GLES / Vulkan 已接入 PC 共用加载画面、C# 加载脚本，以及 LLM 流式工具调用、
Skills / Memory。编辑器和 Android 现在引用同一套强类型脚本 API，导出时不再使用全 dynamic
的替代接口。升级后请同时更新 Android 播放器并重新导出 `.dwgame`。
详见 [当前能力与验证范围](../../docs/android-render-parity.md)。

2026-10-20：`Scene.RealtimeVoice` 已接入 PC 共用运行逻辑和服务协议，包括服务端转写、
唤醒词、流式语音回复、HTTP 语音合成、会话重置、取消和口型同步。Android 用原生麦克风和
扬声器适配，读取游戏包内的 RealtimeVoice 配置；完成事件等待播放结束，错误写入
`ZhengyanGamePlayer` 日志。HTTP / HTTPS 地址均支持，实时连接对应 WS / WSS。

2026-10-21：`Scene.Asr.StartStreamingRecognition` 在原生识别器返回 `SpeechTimeout`、
`NoMatch` 或空结果时继续收音，直到识别到问题或脚本调用 `StopStreamingRecognition`。
对话脚本可在回复结束后保持 30 秒等待下一轮，期间无需重复唤醒；原生单次识别的静默超时
不会提前触发脚本的错误回调。显式停止会返回最终文本（无语音时为空），问题转写、录音和权限
等真实错误仍正常报告。此修复在 Android 播放器内，已有 `.dwgame` 包只需更新播放器即可使用。

回复期间的唤醒词监听切换到下一轮收音时，会先释放旧识别器并等待 300 毫秒；
`RecognizerBusy` 和尚未准备好收音时的 `Client` 错误最多重试三次，不重置脚本的
30 秒期限。取消前已排入脚本队列的 ASR 回调也会被丢弃，避免旧监听错误终止新的续聊。
ASR 启动、静音重启、完成、重试和真实错误直接写入 `ZhengyanGamePlayer` logcat 标签，
错误日志包含原生错误名/码、请求 ID 和回调名，不依赖脚本的 `Console.Error` 输出。

Android `Scene.Asr` 当前使用系统 `SpeechRecognizer`，系统服务可能连接远端语音识别服务器，
与 PC 使用工程内 Whisper / Sherpa 模型的实现不同。唤醒词监听（包括回复期间的打断监听）
收到 `NetworkTimeout (1)` 或 `Network (2)` 时，以 500 毫秒至 5 秒的退避间隔继续监听，
不触发终止回复的脚本错误回调。续聊收音在尚未检测到说话且没有部分文本时遇到网络超时，
也保持脚本原定的 30 秒期限；检测到说话后的转写失败仍报告错误。恢复不能修复系统服务的
网络连接，断网期间唤醒检测可能不可用，相关失败和重试仍记录为 warning。

```powershell
adb logcat -v threadtime ZhengyanGamePlayer:I '*:S'
```

直接安装或分享 Debug APK 时，构建命令需要添加 `-p:EmbedAssembliesIntoApk=true`。
否则默认快速部署产物依赖开发工具另行推送程序集，单独安装 APK 不完整。构建仍需 JDK 21。

- 使用 Android `Activity` 管理创建、暂停、恢复和销毁生命周期；
- 使用 `SurfaceView` 管理渲染表面重建，并通过 `Choreographer` 调度每一帧；
- 建立 EGL/OpenGL ES 清屏、绘制和画面提交循环；
- 在平台边界生成每帧不可变的多点触摸状态快照；
- 通过 Android `Intent` 或应用私有的 `files/GameProject` 目录加载本地工程目录和 `.dwgame` 包；
- 直接启动应用时打开系统文件选择器，选择 `.dwgame` 后加载；通过“打开方式”或“分享/发送”传入
  `.dwgame` 的启动方式保持不变；
- 在开始渲染前执行 Android 兼容性检查；
- 使用 GLES3 绘制 PMX 几何体、基础纹理、实体变换、主相机、环境光和平行光；
- 支持多层 VMD 播放、CPU/GPU 蒙皮、循环和播放速度设置。

场景和运行时能力被限制在 Android 主机边界之后，不会把桌面精灵、Python、OpenCL 或桌面窗口
API 带入 Android 目标。当前已经接入 PMX IK、附加骨骼、Morph 动画、多层 VMD 混合、
Sphere/Toon 材质、GPU BDEF 蒙皮，以及 Bullet 刚体、关节和碰撞物理。剩余差异主要集中在其他
场景渲染 Pass、阴影、音频和发布流程。

## 项目横竖屏

Android 播放器加载项目后，根据 GameEditor 中项目的窗口宽高选择显示方向：

- 宽度小于高度（例如 720×1280）：竖屏。
- 宽度大于高度（例如 1280×720）：横屏。
- 宽高相等：沿用横屏。

同一方向内允许随设备旋转正反朝向。尚未加载项目时使用默认横屏；打开另一个项目时重新
读取其窗口尺寸。GLES 和 Vulkan 共用此规则，渲染视口、GUI 和触摸坐标使用手机实际表面尺寸。
在编辑器修改窗口尺寸并保存后，需要重新导出 `.dwgame`，让包内配置包含新的宽高。

## 打开加密游戏包

选择 GameEditor 导出的加密 `.dwgame` 后，播放器会显示“打开加密游戏包”密码输入框并唤起
软键盘。输入导出时设置的密码，点击“打开”或键盘上的完成键即可继续加载。密码区分大小写，
也保留前后空格；密码为空时不能提交。

密码校验失败会提示重新输入，可继续重试或取消。取消后，如果已有游戏正在运行则返回该游戏，
否则重新打开文件选择器。分卷包请一次选择全部分卷，密码输入规则相同；重试复用已导入的文件。
普通未加密包不显示密码输入框，已有加密包无需重新导出。

密码只用于本次解密，不保存到配置或日志。需要密码与密码校验失败由包加载器返回明确状态，
不依赖异常消息的语言。每次解包使用独立临时目录，失败或取消新包不会清理当前游戏的解包目录。

## APK 图标自定义

可以自定义。Android APK 使用构建时固定的应用图标，与运行时加载的游戏工程图标无关。
默认图标来自仓库中的 `assets/mmd/samples/GameData/Logo/favicon.png`，并以
`@drawable/app_icon` 写入 APK。建议使用带透明背景、至少 192x192 像素的 PNG。

有两种替换方式：

1. 直接替换 `assets/mmd/samples/GameData/Logo/favicon.png`，保持文件名不变（这也会影响使用
   同一默认资源的桌面程序）；
2. 构建时传入自定义 PNG 路径，不修改仓库文件：

   ```powershell
   dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android/Zhengyan.DigitalWife.GamePlayer.Android.csproj `
     -c Release -p:AndroidApplicationIconPath="D:\Icons\my-game-icon.png"
   ```

自定义文件会被作为 `Resources/drawable/app_icon.png` 打包，并同时用于 Android 的普通图标
和圆形图标。指定的图标路径必须存在且是有效 PNG；否则 Android 构建会报告资源缺失。

## APK 应用名称

APK 的应用名称由 Android 项目文件中的 `ApplicationTitle` 控制，安装后桌面启动器显示的名称
也来自该属性。直接修改：

```xml
<ApplicationTitle>我的游戏</ApplicationTitle>
```

也可以在构建时覆盖，不修改项目文件：

```powershell
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android/Zhengyan.DigitalWife.GamePlayer.Android.csproj `
  -c Release -p:ApplicationTitle="我的游戏"
```

当电脑没有配置 `ANDROID_HOME` 或 `JAVA_HOME` 时，可安装 .NET Android workload，并在构建
命令中显式传入 SDK 路径：

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$jdk = "$env:LOCALAPPDATA\Android\jdk"
dotnet build Zhengyan.DigitalWife.Android.slnx `
  -p:AndroidSdkDirectory=$sdk `
  -p:JavaSdkDirectory=$jdk
```

## Android 环境安装与部署（小白指南）

下面的步骤以 Windows 10/11 为例。Android GamePlayer 是独立的 Android 工程，桌面版的
`Zhengyan.DigitalWife.sln` 不需要 Android 工具链；只在编译 Android 版本时执行本节步骤。

### 1. 先准备源代码和基础工具

1. 安装 Git（如果使用 ZIP 下载源码则可以跳过），并把项目放在一个没有权限限制的目录，例
   如 `D:\Projects\CSharp\MMD\Zhengyan.DigitalWife`。
2. 安装 **64 位 .NET 10 SDK**，不要只安装 .NET Runtime。安装完成后打开新的 PowerShell，确认：

   ```powershell
   dotnet --info
   dotnet --list-sdks
   ```

   输出中应当能看到 `10.x.x` SDK。若命令不存在，应重新安装 SDK 或把 dotnet 安装目录加入
   `PATH`，然后重新打开 PowerShell。

### 2. 安装 .NET Android workload（工作负载）

以普通用户打开 PowerShell，执行：

```powershell
dotnet workload install android
dotnet workload list
```

在 `dotnet workload list` 中应能看到 `android`。如果提示权限不足，请以“管理员身份运行
PowerShell”再次执行；如果安装中断，可使用：

```powershell
dotnet workload repair
dotnet workload install android
```

Workload 会安装 Android 项目所需的 MSBuild 目标、Android SDK 工具和打包任务。网络较慢时
请等待命令完成，不要在中途关闭窗口。

### 3. 安装 Android SDK 和 JDK

#### 不安装 Android Studio：使用 Android SDK Command-line Tools

Android Studio 不是构建必需品。可以直接从 Android 官方页面下载 **Command line tools
only**：<https://developer.android.com/studio#command-tools>。Windows 机器按以下步骤安装：

1. 下载 Windows 版 command-line tools 压缩包，并解压到临时目录。
2. 创建 SDK 目录和 `cmdline-tools\latest` 目录，将压缩包内的 `bin`、`lib`、`NOTICE.txt`
   等内容放到 `cmdline-tools\latest` 下，最终应存在：

   ```text
   %LOCALAPPDATA%\Android\Sdk\cmdline-tools\latest\bin\sdkmanager.bat
   ```

3. 准备 JDK 21。当前 .NET 10 Android workload（36.x）要求 JDK 21。可以单独安装
   Microsoft OpenJDK、Eclipse Temurin 等 JDK 21。某些 .NET Android 安装目录会附带 JDK，
   但必须先用 `java -version` 确认版本为 21；不需要安装 Android Studio 自带的 JBR。
4. 在 PowerShell 中设置路径并安装构建所需的 SDK 包：

   ```powershell
   $sdk = "$env:LOCALAPPDATA\Android\Sdk"
   $jdk = "C:\Program Files\Microsoft\jdk-21"
   $sdkManager = "$sdk\cmdline-tools\latest\bin\sdkmanager.bat"
   $androidApi = "35"
   $buildTools = "35.0.0"

   $env:ANDROID_HOME = $sdk
   $env:ANDROID_SDK_ROOT = $sdk
   $env:JAVA_HOME = $jdk
   $env:Path = "$sdk\platform-tools;$sdk\cmdline-tools\latest\bin;$env:Path"

   & $sdkManager "--sdk_root=$sdk" "platform-tools" `
     "platforms;android-$androidApi" "build-tools;$buildTools"
   & $sdkManager "--sdk_root=$sdk" --licenses
   ```

   如果 `$jdk` 路径不同，请改成实际的 JDK 21 目录。若某个 Build-Tools 版本不可用，
   可先执行 `& $sdkManager "--sdk_root=$sdk" --list`，再选择列表中的版本。
5. 验证命令行工具：

   ```powershell
   Test-Path "$sdk\platform-tools\adb.exe"
   Test-Path "$sdk\platforms\android-$androidApi\android.jar"
   Test-Path "$sdk\build-tools\$buildTools\aapt2.exe"
   Test-Path "$jdk\bin\java.exe"
   ```

   以上命令都应输出 `True`。之后按照本文后面的构建命令执行，并显式传入
   `AndroidSdkDirectory` 和 `JavaSdkDirectory`。

最容易的方式是安装 Android Studio（只使用它的 SDK Manager，不需要用 Android Studio 打开
本项目）：

1. 打开 Android Studio，进入 **更多操作（More Actions）-> SDK 管理器（SDK Manager）**。
2. 在 **SDK 平台（SDK Platforms）** 中安装一个可用的 Android API 平台（建议安装 API 35
   或更新版本）。
3. 在 **SDK 工具（SDK Tools）** 中勾选并安装：
   - Android SDK Build-Tools；
   - Android SDK Platform-Tools（包含 `adb`）；
   - Android SDK Command-line Tools (latest)；
   - Android Emulator（只有需要模拟器时才必须安装）。
4. 记下 SDK 位置（SDK Location）。Windows 默认位置通常是
   `%LOCALAPPDATA%\Android\Sdk`。
5. JDK 建议使用 **JDK 21**。当前 .NET Android 环境通常会在
   `%LOCALAPPDATA%\Android\jdk` 提供可用 JDK；也可以使用 Android Studio 自带的 JBR，
   但构建时必须把路径写给 `JavaSdkDirectory`。

在本项目中，推荐先用下面的变量验证路径（如果你的安装位置不同，请修改变量）：

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$jdk = "$env:LOCALAPPDATA\Android\jdk"
Test-Path "$sdk\platform-tools\adb.exe"
Test-Path "$jdk\bin\java.exe"
```

两个命令都应输出 `True`。若 JDK 路径不存在，请在 Android Studio 的 SDK Manager 或 .NET
Android 安装目录中找到 JDK 21，并将 `$jdk` 改为该目录。

### 4. 配置环境变量（推荐）

下面的设置只对当前 PowerShell 窗口有效，适合第一次验证：

```powershell
$env:ANDROID_HOME = $sdk
$env:ANDROID_SDK_ROOT = $sdk
$env:JAVA_HOME = $jdk
$env:Path = "$sdk\platform-tools;$sdk\emulator;$sdk\cmdline-tools\latest\bin;$env:Path"
```

如果希望永久配置，请在 Windows 搜索中打开“编辑系统环境变量 -> 环境变量”，新增用户变量
`ANDROID_HOME`、`ANDROID_SDK_ROOT`、`JAVA_HOME`，并把以下目录加入用户 `Path`：

```text
%LOCALAPPDATA%\Android\Sdk\platform-tools
%LOCALAPPDATA%\Android\Sdk\emulator
%LOCALAPPDATA%\Android\Sdk\cmdline-tools\latest\bin
```

修改后必须关闭并重新打开 PowerShell、Visual Studio 或 Rider。

### 5. 检查环境是否完整

```powershell
dotnet --info
dotnet workload list
& "$sdk\platform-tools\adb.exe" version
```

如果 `adb` 报错，说明 Platform-Tools 没有安装或 `$sdk` 指向错误。`NETSDK1147` 通常表示
Android workload 没安装；`XA5300` 通常表示 Android SDK 路径没有找到，此时使用下面构建命令
中的显式路径参数。

### 6. 还原并编译 Android GamePlayer

在仓库根目录执行。路径带空格时必须保留双引号：

```powershell
dotnet restore Zhengyan.DigitalWife.Android.slnx `
  -p:AndroidSdkDirectory="$sdk" `
  -p:JavaSdkDirectory="$jdk"

dotnet build Zhengyan.DigitalWife.Android.slnx --no-restore `
  -p:AndroidSdkDirectory="$sdk" `
  -p:JavaSdkDirectory="$jdk"
```

开发调试也可以只编译 Android 主机项目：

```powershell
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android/Zhengyan.DigitalWife.GamePlayer.Android.csproj `
  -p:AndroidSdkDirectory="$sdk" `
  -p:JavaSdkDirectory="$jdk"
```

生成可安装的 Release APK：

```powershell
dotnet restore src/Zhengyan.DigitalWife.GamePlayer.Android/Zhengyan.DigitalWife.GamePlayer.Android.csproj `
  -p:Configuration=Release `
  -p:AndroidSdkDirectory="$sdk" `
  -p:JavaSdkDirectory="$jdk"

dotnet build src/Zhengyan.DigitalWife.GamePlayer.Android/Zhengyan.DigitalWife.GamePlayer.Android.csproj `
  -c Release --no-restore `
  -p:AndroidSdkDirectory="$sdk" `
  -p:JavaSdkDirectory="$jdk"
```

`--no-restore` 只有在同一配置（这里是 `Release`）的 `dotnet restore` 成功完成后才能使用。
如果 NuGet 源暂时不可用，请先修复网络/代理或启用可用的镜像源，再重复 restore；不要直接跳过 restore。

APK 位于：

```text
src/Zhengyan.DigitalWife.GamePlayer.Android/bin/Release/net10.0-android/
```

文件名通常以 `com.zhengyan.digitalwife.gameplayer-Signed.apk` 结尾。这个构建产物适合开发和
测试；发布到 Google Play 或正式分发时，还需要使用自己的 Android keystore 签名，不能把调试
签名当作正式签名。

### 7. 连接真机或启动模拟器

真机部署需要 Android 7.0（API 24）或更高版本，并建议设备支持 OpenGL ES 3.0：

1. 在手机“设置 -> 关于手机”中连续点击“版本号”开启开发者选项。
2. 在“开发者选项”中打开“USB 调试”。
3. 用 USB 连接电脑，在手机上确认“允许此电脑进行 USB 调试”。
4. 执行：

   ```powershell
   & "$sdk\platform-tools\adb.exe" devices
   ```

   设备状态应为 `device`。显示 `unauthorized` 时，解锁手机并重新确认授权。

需要模拟器时，在 Android Studio 的 **设备管理器（Device Manager）** 创建一个 API 24+ 的设备，启动后再执
行 `adb devices`。优先选择带硬件加速的 x86_64 模拟器；真机通常使用 arm64-v8a。

### 8. 安装、启动并加载游戏项目

安装 APK：

```powershell
$apk = Get-ChildItem "src/Zhengyan.DigitalWife.GamePlayer.Android/bin/Release/net10.0-android" `
  -Filter "*-Signed.apk" | Select-Object -First 1
& "$sdk\platform-tools\adb.exe" install -r $apk.FullName
```

启动应用（包名固定为 `com.zhengyan.digitalwife.gameplayer`）：

```powershell
& "$sdk\platform-tools\adb.exe" shell monkey `
  -p com.zhengyan.digitalwife.gameplayer 1
```

GamePlayer 可以加载 GameEditor 的工程目录或 `.dwgame` 发布包。推荐把发布包复制到手机的
`Download` 目录，然后用文件管理器点击 `.dwgame` 并选择 **Zhengyan DigitalWife GamePlayer**
打开；应用会通过 `content://` URI 自动复制并解压包。也可以通过 Android 的文件分享/打开方
式传入 `file://` URI。应用同时注册了 Android 的“打开方式”和“发送/分享”入口；部分厂商的
文件管理器会把未知扩展名标记成通用 MIME 类型，因此分享候选中可能也会对其他文件显示本应用，
但只有有效的 `.dwgame` 游戏包能够通过运行时校验并加载。

开发调试时，如果宿主能访问该路径，也可以显式传入 `zhengyan.project_path`：

```powershell
& "$sdk\platform-tools\adb.exe" shell am start `
  -n com.zhengyan.digitalwife.gameplayer/.MainActivity `
  --es zhengyan.project_path "/sdcard/Download/DemoGame.dwgame"
```

若 Android 版本限制了直接读取共享存储，请改用文件管理器的“打开方式”流程，让系统授予
`content://` 临时读取权限。工程目录必须包含 `game.project.json` 及其 `assets`、场景和脚本
文件；`.dwgame` 包则由 GamePlayer 自动解压到应用缓存目录。

为了避免 Android 设备在启动时进行耗时的 Roslyn 编译，发布 `.dwgame` 时请在 GameEditor
中保持 **Android C# 预编译** 选项开启。正确导出的包会包含
`compiled/android/manifest.json` 和对应的 `*.dll`。如果加载的是较旧的包或预编译未开启，
GamePlayer 仍会对脚本执行一次运行时编译作为兼容回退；编译失败的脚本会被记录并跳过后续每帧
执行，不会反复阻塞渲染线程。重新导出带有 Android 预编译程序集的包，可以同时消除启动卡顿并
确保脚本 API 在发布前完成兼容性检查。

查看启动和兼容性日志：

```powershell
& "$sdk\platform-tools\adb.exe" logcat -s ZhengyanGamePlayer
```

如果应用闪退，需同时查看引擎日志、Android 主线程异常和 .NET/Mono 异常。仅使用
`-s ZhengyanGamePlayer` 会过滤掉 `AndroidRuntime` 的崩溃堆栈，此时使用：

```powershell
& "$sdk\platform-tools\adb.exe" logcat -v threadtime `
  ZhengyanGamePlayer:V AndroidRuntime:E MonoDroid:V mono-rt:E '*:S'
```

如果 `adb` 已经加入 `PATH`，也可以直接执行：

```powershell
adb logcat -v threadtime ZhengyanGamePlayer:V AndroidRuntime:E MonoDroid:V mono-rt:E '*:S'
```

PowerShell 中需要给 `*:S` 加引号，避免 `*` 被当作通配符处理。建议在复现问题前先执行
`adb logcat -c` 清空旧日志，然后启动应用并再次触发问题。

### 9. Android 版本的功能边界

- Android 脚本只支持 C#；Python 脚本会在发布兼容性检查中被拒绝。C# 脚本可通过 `Scene.GetSprite(...)` 获取 `AndroidScriptSprite`，并在运行时调用 `SetSourceRect(x, y, width, height)` 播放精灵表动画。
- 不支持桌面精灵、透明点击穿透、窗口拖拽和系统托盘。
- Android 支持 OpenGL ES 和 Vulkan；Renderer=Auto 时优先尝试 Vulkan，设备初始化失败后自动
  回退到 OpenGL ES。Vulkan 已覆盖 PMX、天空盒、水面、粒子、Textured Plane、阴影、平面反射、
  水下后处理和 RenderTexture。GUI、触摸和 IME 统一使用 Android 原生 Canvas/View 路径，
  Android Vulkan 不加载桌面 `cimgui` 原生库。
- OpenCL 不会在 Android 上启用。使用 Vulkan 后端时才可能使用 Vulkan Compute。
- 未完成的粒子、阴影、后处理、音频、软键盘和手柄能力会在兼容性检查中提示，发布前应逐项
  查看 GameEditor 的 Android 检查结果。

### 10. 常见问题

| 现象 | 处理方法 |
| --- | --- |
| `NETSDK1147` | 执行 `dotnet workload install android`，然后重新打开终端。 |
| `XA5300` 或找不到 SDK | 检查 `$sdk`，并在 `restore/build` 命令中传入 `AndroidSdkDirectory`。 |
| 找不到 `java.exe` | 使用 JDK 21，检查 `$jdk\bin\java.exe`，并传入 `JavaSdkDirectory`。 |
| `adb` 显示 `unauthorized` | 解锁手机、允许 USB 调试，必要时在开发者选项中撤销 USB 调试授权后重连。 |
| `INSTALL_FAILED_NO_MATCHING_ABIS` | 当前 APK 包含 arm64-v8a 和 x86_64；确认设备 ABI 或使用匹配的构建产物。 |
| 直接启动应用 | 应用会自动打开系统文件选择器；选择 `.dwgame`（或分包的全部文件）即可启动。也可以通过文件管理器“打开方式”或“分享/发送”传入 `.dwgame`。 |
| 黑屏或无法创建 GLES 上下文 | 使用支持 OpenGL ES 3.0 的设备，查看 `adb logcat -s ZhengyanGamePlayer`。 |
