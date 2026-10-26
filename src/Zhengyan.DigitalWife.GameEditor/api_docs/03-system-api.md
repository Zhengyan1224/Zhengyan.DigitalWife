---
id: system-api
title: 基础系统 API
category: 基础
objects:
  - System
  - Python stdlib
  - Time
  - current_time
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
  - 当前时间
  - UtcNow
  - UnixTimeSeconds
  - UnixTimeMilliseconds
---

# 基础系统 API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 基础系统 API |
| 分类 | 基础 |
| 主要对象 | ``System``, ``Python stdlib``, ``Time``, ``current_time`` |
| C# 入口 | `Time, string, Math, Regex, JsonSerializer, Task` |
| Python 入口 | `current_time, str, math, re, json, datetime` |
| 说明 | 当前时间、平台判断、基础语言能力、常用标准库和示例。 |

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

新增 Touch / 平台标识 / Time 后，需要更新 GamePlayer、Android 播放器和 GameEditor；使用新 API 的 `.dwgame` 需重新导出，旧包仍可由新版播放器运行。

## 当前时间（PC / Android）

C# `.csx` 在 PC 和 Android 上都可直接访问只读全局对象 `Time`，类型为 `Zhengyan.DigitalWife.Scripting.ScriptTime`。PC Python 使用预置对象 `current_time`；已有的标准库 `time` 和 `datetime` 仍可正常使用。

| C# 属性 | 类型 | Python 属性（PC） | 说明 |
| --- | --- | --- | --- |
| `Time.Now` | `DateTime` | `current_time.now` | 设备当前本地日期时间。C# 的 `Kind` 为 `Local`；Python 返回包含本地时区偏移的 `datetime`。 |
| `Time.UtcNow` | `DateTime` | `current_time.utc_now` | 设备当前 UTC 日期时间。C# 的 `Kind` 为 `Utc`；Python 返回包含 UTC 时区的 `datetime`。 |
| `Time.UnixTimeSeconds` | `long` | `current_time.unix_time_seconds` | 从 1970-01-01 00:00:00 UTC 起的整秒数。Python 返回 `int`。 |
| `Time.UnixTimeMilliseconds` | `long` | `current_time.unix_time_milliseconds` | 从同一 UTC 起点起的整毫秒数。Python 返回 `int`。 |

这些属性在 Start、Update、GUI、加载和异步回调等事件中都可使用，每次读取都会查询系统时钟，不是事件开始时的快照。设备时区影响本地时间的显示；Unix 时间戳与时区无关。系统时间被手动调整或校时时，读数也会随之改变。帧间隔仍使用 `DeltaSeconds`，PC 游戏运行时长可使用 `Scene.Performance.TotalSeconds`。

```csharp
if (IsStart)
{
    DateTime now = Time.Now;
    Console.WriteLine($"本地时间：{now:yyyy-MM-dd HH:mm:ss}");
    Console.WriteLine($"UTC 时间：{Time.UtcNow:O}");
    Console.WriteLine($"Unix 秒：{Time.UnixTimeSeconds}，毫秒：{Time.UnixTimeMilliseconds}");

    int hour = now.Hour;
    string greeting = hour < 12 ? "早上好" : hour < 18 ? "下午好" : "晚上好";
    Console.WriteLine(greeting);
}
```

```python
def start(entity, scene, input, audio):
    now = current_time.now
    print(now.strftime("%Y-%m-%d %H:%M:%S"))
    print(current_time.utc_now.isoformat())
    print(current_time.unix_time_seconds, current_time.unix_time_milliseconds)
```

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
