# VeldridAndroidPatch：Android Vulkan 构建修补工具

这是引擎构建流程内部使用的维护工具。它为 Android 使用的 Veldrid / Vulkan 加载程序集加入当前仓库所需的运行时修补和验证，普通游戏用户无需手动运行。

Android 工程会在打包、裁剪前对构建副本应用修补，检查裁剪后的结果；不应手动修改 NuGet 缓存中的原始 DLL。

## 处理的范围

- Vulkan 启动与原生指针生命周期。
- Vk / NativeLibraryLoader 加载错误处理。
- Android Render Pass 与 Present 同步。
- 透明合成选择与内存安全。
- 修补标识检查及验证入口。

这些修补针对当前仓库依赖版本。升级 Veldrid 或 Vulkan 绑定后，需要重新确认 IL 匹配与回归结果，不能承诺任意版本都适用。

## 常规使用

构建 [Android GamePlayer](../../src/Zhengyan.DigitalWife.GamePlayer.Android/README.md) 会自动使用该工具。

维护者可在仓库根目录构建工具：

```text
dotnet build tools/VeldridAndroidPatch
```

手动命令格式如下，路径是占位参数：

```text
dotnet run --project tools/VeldridAndroidPatch -- <original-Veldrid.dll> <output-Veldrid.dll>
dotnet run --project tools/VeldridAndroidPatch -- --check <linked-Veldrid.dll>
dotnet run --project tools/VeldridAndroidPatch -- --loader <original-loader.dll> <output-loader.dll>
dotnet run --project tools/VeldridAndroidPatch -- --check-loader <loader.dll>
```

先使用构建副本，再验证。原始 DLL 与输出路径应分开。

## 相关文档

- [Android Vulkan 启动修复](../../docs/android-mono-vulkan-fixes.md)
- [Present 同步](../../docs/android-vulkan-present-synchronization.md)
- [加载器修复](../../docs/android-vulkan-dlerror-fix.md)
- [项目总览](../../README.md)
