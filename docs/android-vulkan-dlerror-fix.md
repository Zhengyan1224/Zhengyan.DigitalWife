# Android Vulkan：dlerror 返回值误释放

## 2026-10-08 日志定位

`temp/crash.log` 的关键顺序（同一进程 18049、同一主线程）：

```text
08:56:47.844  Vulkan startup: CreateInstance begin
08:56:47.846  Shared library 'libdl' not loaded, p/invoke 'dlerror' may fail
08:56:47.847  Pointer tag for 0x7293e4a5f8 was truncated
              SIGABRT
              libc.so abort -> libc.so free -> anonymous JIT code
```

日志中没有 `vkCreateInstance begin`，因此失败发生在 Vulkan 库的加载/函数初始化阶段，
早于 Vulkan 实例、设备、交换链、场景加载与每帧同步。

Veldrid 依赖的 Vk 1.0.25 中，`Vulkan.NativeLibrary.UnixNativeLibrary.LoadLibrary` 的第一步
会调用 `Libdl.dlerror()` 清除线程上残留的动态加载错误，并丢弃返回值。
其 [Libdl 接口声明](https://github.com/mellinoe/vk/blob/master/src/vk/Libdl.cs)
把 `dlerror` 声明为返回 `string` 的 P/Invoke。

但 [Android Bionic 的实现](https://android.googlesource.com/platform/bionic/+/refs/heads/main/linker/dlfcn.cpp)
返回线程内部 `dlerror_buffer` 的指针，同时清除 `current_dlerror`。
该内存属于系统，调用方不应释放。`string` 返回值会让 interop marshaller 复制后释放这个指针，
即使托管调用方只是丢弃结果。非空返回值因此触发 `free` 和指针标签检查，符合本次崩溃栈。
`Shared library ... not loaded` 是 P/Invoke 解析过程的日志，不能单独解释为设备缺少 Vulkan。

## 修复范围

- 构建时对独立副本 `vk.dll` 修正 `dlerror` 返回类型为 `IntPtr`，不接管或释放内存。
- 同时修正 Veldrid.SPIRV 使用的 `NativeLibraryLoader.dll`：其 `Libdl1`、`Libdl2` 有同样的声明错误。
  两个 native 导入改为返回 `IntPtr`，原有托管字符串接口改为显式 `Marshal.PtrToStringAnsi` 复制，保留接口且不释放指针。
- 核实该版本唯一调用点仅清除错误、随后 `pop`，没有需要字符串结果的消费者。
  若依赖升级改变调用约定，补丁工具会报错而不是盲目修改。
- Android 的 libdl 导入使用 `libdl.so` 和 C 调用约定。
- 为修补程序集生成不同的 MVID，避免与原始包的元数据/JNI 缓存混淆。
- 发布输入、裁剪器引用与最终裁剪产物都检查两个加载器；保留原有 Veldrid 补丁检查。
- 启动日志改为 `startupPatch=2; loader=borrowed-dlerror`，方便判断手机是否安装了此版本。

不需要修改 `.dwgame`。JIT 兼容设置、前一帧 fence 和资源内存屏障继续保留。
本次没有关闭 Android 指针标签检查，也没有删掉防闪烁所需的同步。

## 验证

补丁工具入口：

```powershell
dotnet build tools/VeldridAndroidPatch -c Release
dotnet tools/VeldridAndroidPatch/bin/Release/net10.0/VeldridAndroidPatch.dll --loader <原始vk.dll> <修补目录/vk.dll>
dotnet tools/VeldridAndroidPatch/bin/Release/net10.0/VeldridAndroidPatch.dll --loader <原始NativeLibraryLoader.dll> <修补目录/NativeLibraryLoader.dll>
dotnet tools/VeldridAndroidPatch/bin/Release/net10.0/VeldridAndroidPatch.dll --check-loader <修补目录/vk.dll>
dotnet tools/VeldridAndroidPatch/bin/Release/net10.0/VeldridAndroidPatch.dll --verify-loader <修补目录/vk.dll>
dotnet tools/VeldridAndroidPatch/bin/Release/net10.0/VeldridAndroidPatch.dll --verify-loader <修补目录/NativeLibraryLoader.dll>
dotnet tools/VeldridAndroidPatch/bin/Release/net10.0/VeldridAndroidPatch.dll --verify <修补目录/Veldrid.dll>
```

本机验证：

- Windows 测试在临时副本中将该 P/Invoke 的 native 目标替换为同为无参数、返回系统持有
  `char*` 的 `GetCommandLineA`，连续调用 1000 次验证非空借用指针不会被释放。
  两个加载器各运行 1000 次，共享加载器还轮流覆盖 Libdl1/Libdl2 两个分支。
  它验证返回值封送，不代表 Android 系统调用已在手机上执行。
- Unix 验证分支会制造 `dlopen` 失败，检查非空 `dlerror` 及下一次返回空指针；本机未执行该分支。
- 使用两个修补程序集，在本机原生 Vulkan 上执行 12 次设备创建/缓冲区分配，同时强制 compacting GC，通过。
- 既有 Android 运行时测试 18/18 通过，包括 TTS 口型、脚本引用与 Vulkan 连续帧渲染。

工作机目前没有连接 adb 设备，仍需用户在 Redmi 上复测 Vulkan 项目启动。

## 交付包

2026-10-08 Release ARM64 构建完成，v2/v3 签名检查通过。
已从最终 APK 的 assembly store 解压 `vk.dll`、`NativeLibraryLoader.dll` 和 `Veldrid.dll`，
三者分别通过接口/补丁检查，AOT 模块数仍为 0。
构建仅有原有 SDL 16 KB 对齐与重复打包警告（XA0141、XA4301）。

- 文件：`artifacts/android/GamePlayer-arm64-20261008-Signed.apk`
- 大小：57,828,049 字节
- SHA-256：`B17AF12FAC373508C47C0C8CAA1C72D18E865C725906230070BF27D612BC2BF6`
- 安装后启动日志应包含：`startupPatch=2; loader=borrowed-dlerror`

更新播放器后可以直接打开原来的 Vulkan `.dwgame` 复测，无需重导出。
