---
id: system-api
title: 基础系统 API
category: 基础
objects:
  - System
  - Python stdlib
keywords:
  - system
  - stdlib
  - regex
  - json
  - math
  - PlatformName
  - IsAndroid
  - IsWindows
  - IsLinux
  - IsMacOS
  - 平台判断
---

# 基础系统 API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 基础系统 API |
| 分类 | 基础 |
| 主要对象 | ``System``, ``Python stdlib`` |
| C# 入口 | `string, Math, Regex, JsonSerializer, Task` |
| Python 入口 | `str, math, re, json, datetime` |
| 说明 | 基础语言能力、受信任脚本边界、常用标准库和示例。 |

## API 内容

## 当前运行平台（全局只读标识）

C# `.csx` 可直接使用以下全局属性，不需要通过 `Scene`，也不需要自己调用 `OperatingSystem`。C# 本身也支持 `OperatingSystem.IsWindows()` 等标准 API。这里的平台标识表示当前播放器运行平台；在 Windows 编辑器导出 Android 脚本时，Android 运行时仍返回 `Android`，不是导出机器的 Windows。

| C# 全局属性 | Python 模块全局变量（PC） | 说明 |
| --- | --- | --- |
| `PlatformName` | `platform_name` | `Windows`、`Linux`、`MacOS`、`Android`；其他系统为 `Unknown`。大小写固定。 |
| `IsWindows` | `is_windows` | Windows 为 true。 |
| `IsLinux` | `is_linux` | 桌面 Linux 为 true；Android 不计作 Linux。 |
| `IsMacOS` | `is_macos` | macOS 为 true。 |
| `IsAndroid` | `is_android` | Android 为 true。Android 不支持 Python，PC Python 中为 false。 |

标识在 Start、Update、GUI、加载等所有事件中保持一致。C# 属性只读，Python 的预置变量可由普通 Python 赋值覆盖，建议只读取。平台与输入能力不同：Windows 也可能有触屏，Android 也可能连接键盘/手柄；输入分支优先检测 `Input.IsTouchAvailable / IsTouchDown / HasGamepad`。

```csharp
if (IsStart)
{
    Console.WriteLine($"当前平台：{PlatformName}");
    if (IsAndroid)
        Console.WriteLine("使用触摸界面");
    else if (IsWindows || IsLinux || IsMacOS)
        Console.WriteLine("启用桌面输入提示");
}
```

```python
def start(entity, scene, input, audio):
    print(f"当前平台：{platform_name}")
    if is_windows:
        print("Windows 播放器")
    elif is_linux or is_macos:
        print("Linux / macOS 播放器")
```

新增 Touch / 平台标识后，需要更新 GamePlayer、Android 播放器和 GameEditor；使用新 API 的 `.dwgame` 需重新导出，旧包仍可由新版播放器运行。

脚本层已经支持基础语言和系统 API。字符串、数值、集合、日期时间、正则、JSON、数学函数等不需要额外的引擎封装，可以直接使用 C# / Python 自身能力。

边界说明：

- C# `.csx` 是受信任本地脚本，运行在 GamePlayer 进程内，不是安全沙箱。
- Python `.py` 是受信任本地脚本，运行在独立 Python 进程内，不是安全沙箱。
- 游戏存档建议优先使用 `Scene.Save` / `scene.save`，这样路径会被限制在工程 `saves/` 目录内，更适合跨平台发布。
- 如果直接使用 C# `System.IO` 或 Python `open()` 访问文件，需要自己处理 Windows / Linux / MacOS 的路径差异和权限问题。

C# 常用能力：

| 能力 | 可用 API |
| --- | --- |
| 字符串 | `string`、`StringBuilder`、`Trim`、`Split`、`Replace`、`Contains`、`StartsWith`、`EndsWith` |
| 数值 | `int`、`float`、`double`、`decimal`、`Math`、`MathF`、`Random` |
| 集合 | `List<T>`、`Dictionary<TKey,TValue>`、数组、LINQ |
| 日期时间 | `DateTime`、`DateTimeOffset`、`TimeSpan` |
| 正则 | `Regex` |
| JSON | `JsonSerializer` |
| 向量 | `Vector2`、`Vector3`、`Vector4`、`Quaternion` |
| 异步 | `Task`、`CancellationToken` |

C# 示例：

```csharp
if (IsStart)
{
    string raw = "  小雨@#$ 123 ABC  ";
    string clean = Regex.Replace(raw.Trim(), @"[^\u4e00-\u9fa5a-zA-Z0-9\s,.!?]", "");

    List<int> values = [1, 2, 3, 4, 5];
    int total = values.Where(v => v % 2 == 1).Sum();

    float wave = MathF.Sin((float)DateTime.UtcNow.TimeOfDay.TotalSeconds);
    Vector3 next = Entity.Position + new Vector3(wave, 0.0f, 0.0f);
    Entity.SetPosition(next.X, next.Y, next.Z);

    string json = JsonSerializer.Serialize(new { clean, total });
    Scene.Save.WriteText("system_api_demo.json", json);
}
```

Python 常用能力：

| 能力 | 可用 API |
| --- | --- |
| 字符串 | `str`、`strip`、`split`、`replace`、`in`、`startswith`、`endswith` |
| 数值 | `int`、`float`、`round`、`abs`、`min`、`max`、`sum` |
| 集合 | `list`、`dict`、`set`、`tuple`、列表推导式 |
| 日期时间 | `datetime`、`time` |
| 数学 | `math`、`random`、`statistics` |
| 正则 | `re` |
| JSON | `json` |

Python 示例：

```python
def start(entity, scene, input, audio):
    raw = "  小雨@#$ 123 ABC  "
    clean = re.sub(r"[^\u4e00-\u9fa5a-zA-Z0-9\s,.!?]", "", raw.strip())

    values = [1, 2, 3, 4, 5]
    total = sum(v for v in values if v % 2 == 1)

    wave = math.sin(time.time())
    entity.set_position(entity.position[0] + wave, entity.position[1], entity.position[2])

    scene.save.write_json("system_api_demo.json", {
        "clean": clean,
        "total": total,
        "created_at": datetime.datetime.now().isoformat()
    })
```
