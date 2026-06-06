---
id: window-runtime
title: Window / Runtime 设置
category: 运行时
objects:
  - RuntimeWindowControl
  - RuntimeProjectControl
  - RuntimeCommandResult
keywords:
  - window
  - runtime
  - exit
  - ExecuteCommand
  - tray
---

# Window / Runtime 设置

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Window / Runtime 设置 |
| 分类 | 运行时 |
| 主要对象 | ``RuntimeWindowControl``, ``RuntimeProjectControl``, ``RuntimeCommandResult`` |
| C# 入口 | `Scene.Window, Scene.Runtime` |
| Python 入口 | `scene.window, scene.runtime` |
| 说明 | 窗口尺寸、显示/隐藏、退出、OpenCL、系统命令和桌面精灵托盘。 |

## API 内容

窗口设置可在 GameEditor 的 Project 面板中配置，也可以脚本运行时修改。

C#：

```csharp
Scene.Window.SetTitle("Battle Scene");
Scene.Window.SetSize(1600, 900);
Scene.Window.SetFullscreen(false);
Scene.Window.SetResizable(true);
Scene.Window.SetTimingMode("time_synchronized");
Scene.Window.SetVisible(true);

Console.WriteLine(Scene.Runtime.ComputeBackend);
Console.WriteLine(Scene.Runtime.IsUsingOpenCL);
Scene.Runtime.SetUseOpenCL(true);

RuntimeCommandResult result = Scene.Runtime.ExecuteShellCommand("echo hello");
Console.WriteLine(result.StandardOutput);
```

Python：

```python
scene.window.set_title("Battle Scene")
scene.window.set_size(1600, 900)
scene.window.set_fullscreen(False)
scene.window.set_resizable(True)
scene.window.set_timing_mode("time_synchronized")
scene.window.set_visible(True)

print(scene.runtime.compute_backend)
print(scene.runtime.is_using_opencl)
scene.runtime.set_use_opencl(True)

result = scene.runtime.execute_shell_command("echo hello")
print(result["stdout"])
```

Runtime 设置：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Runtime.UseOpenCL` | `scene.runtime.use_opencl` | 项目/运行时是否请求使用 OpenCL。它表示“希望启用”，不代表当前一定已经使用。 |
| `Scene.Runtime.IsUsingOpenCL` | `scene.runtime.is_using_opencl` | 当前已加载 PMX 是否实际使用 OpenCL 后端。OpenCL 不可用或初始化失败时为 `false`。 |
| `Scene.Runtime.ComputeBackend` | `scene.runtime.compute_backend` | 当前实际计算后端，通常为 `OpenCL` 或 `CPU`。 |
| `Scene.Runtime.SetUseOpenCL(value)` | `scene.runtime.set_use_opencl(value)` | 切换是否请求 OpenCL；GamePlayer 会重新应用运行时设置，失败时自动回退 CPU。 |
| `Scene.Runtime.ExecuteCommand(fileName, arguments, timeoutMilliseconds, workingDirectory)` | `scene.runtime.execute_command(file_name, args=None, timeout_seconds=30, working_directory=None, shell=False)` | 执行系统命令并返回退出码、标准输出、标准错误和是否超时。默认不经过 shell。 |
| `Scene.Runtime.ExecuteShellCommand(command, timeoutMilliseconds, workingDirectory)` | `scene.runtime.execute_shell_command(command, timeout_seconds=30, working_directory=None)` | 通过系统 shell 执行命令。Windows 使用 `cmd.exe /c`，Linux/macOS 使用 `/bin/sh -c`。 |

窗口设置：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Window.Title` | `scene.window.title` | 当前窗口标题快照。C# 可直接赋值，Python 用 `set_title` 修改。 |
| `Scene.Window.Width` / `Height` | `scene.window.width` / `height` | 当前配置窗口尺寸。Python 是事件快照。 |
| `Scene.Window.Fullscreen` | `scene.window.fullscreen` | 是否全屏。C# 可直接赋值，Python 用 `set_fullscreen` 修改。 |
| `Scene.Window.Resizable` | `scene.window.resizable` | 是否允许调整窗口大小。C# 可直接赋值，Python 用 `set_resizable` 修改。 |
| `Scene.Window.TimingMode` | `scene.window.timing_mode` | 动画计时模式。C# 可直接赋值，Python 用 `set_timing_mode` 修改。 |
| `Scene.Window.SetSize(width, height)` | `scene.window.set_size(width, height)` | 设置窗口尺寸。 |
| `Scene.Window.Visible` / `SetVisible(value)` / `ToggleVisible()` | `scene.window.set_visible(value)` / `scene.window.toggle_visible()` | 显示、隐藏或切换窗口可见状态，常用于桌面精灵系统托盘菜单。 |
| `Scene.Window.Exit()` / `Quit()` | `scene.window.exit()` / `scene.window.quit()` | 从脚本退出 GamePlayer。 |

系统命令返回值：

| 字段 | 说明 |
| --- | --- |
| `ExitCode` / `exit_code` | 进程退出码。超时时为 `-1`。 |
| `StandardOutput` / `stdout` | 标准输出文本。 |
| `StandardError` / `stderr` | 标准错误文本。 |
| `TimedOut` / `timed_out` | 是否超时。 |
| `Success` / `success` | 未超时且退出码为 `0`。 |

桌面精灵托盘：

- GameEditor 的 Window / Runtime 面板在开启 Desktop sprite mode 后可启用系统托盘、设置 Windows `.ico` 图标、编辑右键菜单项。
- 菜单项字段包括 `Id`、显示文本、内置动作和脚本事件名。内置动作当前支持 `none`、`toggle_visibility`、`exit`。
- 点击菜单项会触发 C# 的 `IsTrayMenuEvent`，并传入 `TrayMenuItemId`、`TrayMenuItemText`、`TrayMenuEventName`；Python 会优先调用同名 `Script event` 函数，否则调用 `tray_menu_event(...)`。
- 当前 Windows 使用原生系统托盘实现。Linux/macOS 保留同一配置和脚本 API，但运行时会安全降级为不创建托盘；后续可按桌面环境补原生托盘实现。

`TimingMode` 可用值：

- `time_synchronized`：按真实时间推进动画，帧率波动时动画速度保持稳定。
- `frame_rate_dependent`：按帧推进，帧率下降时动画会变慢。

窗口尺寸最小会被限制到 `320 x 240`。

OpenCL 说明：

- `UseOpenCL` 是偏好设置；实际后端以 `IsUsingOpenCL` / `ComputeBackend` 为准。
- 如果机器没有可用 OpenCL GPU、驱动枚举失败、`skinned_animation.cl` 编译失败或初始化失败，GamePlayer 会回退到 CPU。
- 切换 OpenCL 会让已加载 PMX 重新加载当前模型以应用后端变化，运行中切换可能有短暂停顿。
