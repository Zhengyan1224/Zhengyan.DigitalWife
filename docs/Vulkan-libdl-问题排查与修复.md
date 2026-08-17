# Vulkan 程序报错 "did not find a Vulkan loader" 排查与修复

> 适用于 Linux (glibc ≥ 2.34,如 Ubuntu 22.04+、Linux Mint 21+/22) 上运行使用 Veldrid / 旧版原生库加载器的 .NET 程序。

## 1. 症状

- 程序报错：`Vulkan was explicitly selected but is unavailable: Veldrid did not find a Vulkan loader or a compatible physical device.`
- 但系统层面 Vulkan 正常：
  - `vulkaninfo --summary` 能正常枚举出 GPU（如 `Intel(R) HD Graphics 520`）
  - C/C++ 程序调用 `dlopen("libvulkan.so.1")` 正常，能创建设备

## 2. 根因

glibc 2.34 起 `libdl`（dlopen/dlsym）被合并进 libc 主库，系统中只存在 `libdl.so.2`，**不再有未带版本号的 `libdl.so`**。

Veldrid 4.9.0 依赖的 `Vk` 包（P/Invoke 绑定）通过 `[DllImport("libdl")]` 加载动态库，加载失败 → 整个 Vulkan 后端判定为"未找到 loader" → 即使系统 Vulkan 完全正常也会报错。

.NET 运行时找不到库时的典型报错（在终端可见）：

```
libdl.so: cannot open shared object file: No such file or directory
```

## 3. 快速诊断（30 秒）

在项目里临时运行以下代码（需引用 Veldrid，`AllowUnsafeBlocks`）：

```csharp
using System;
using Vulkan;

try
{
    unsafe
    {
        uint count = 0;
        VulkanNative.vkEnumerateInstanceExtensionProperties((byte*)null, ref count, null);
        Console.WriteLine($"OK, 扩展数={count}");
    }
}
catch (Exception e)
{
    Console.WriteLine($"异常: {e.Message}");
    var inner = e.InnerException;
    while (inner != null) { Console.WriteLine($"内部: {inner.Message}"); inner = inner.InnerException; }
}
```

- 输出 `OK` → 另有其他问题
- 出现 `libdl.so: cannot open shared object file` → 就是本问题

确认系统缺库：

```bash
ldconfig -p | grep libdl      # 只有 libdl.so.2 没有 libdl.so 即确认
```

## 4. 解决方案

### 方案 A：系统级修复（推荐，一劳永逸）

```bash
sudo ln -s /usr/lib/x86_64-linux-gnu/libdl.so.2 /usr/local/lib/libdl.so
sudo ldconfig
```

对系统上所有程序生效。验证：

```bash
ldconfig -p | grep "libdl.so "
```

### 方案 B：项目级修复（无需 sudo，但会被 clean 清除）

把符号链接放进 .NET 程序的输出目录（.NET 原生库搜索优先该目录）：

```bash
ln -sf /usr/lib/x86_64-linux-gnu/libdl.so.2 bin/Debug/net10.0/libdl.so
# 目录名按实际 TFM 调整，如 bin/Debug/net8.0/
```

注意：`dotnet clean` 或删除 bin 后需要重新放置。

## 5. 验证

```bash
dotnet run
```

看到类似输出即成功：

```
[Vulkan] Device='Intel(R) HD Graphics 520 (SKL GT2)'
[GamePlayer] Graphics backend: Vulkan; renderer: Vulkan (...)
```

## 6. 其他类似情况

同样的"`libdl.so` 不存在"问题也可能出现在其他使用旧版 `dlopen` 封装的程序（如部分 JNA、旧 SDL2 应用、Electron 旧版），修复方式相同。

如果新版本库已修复（例如升级 Veldrid 到修复了 `libdl` 依赖的版本），也可考虑升级依赖替代打补丁。