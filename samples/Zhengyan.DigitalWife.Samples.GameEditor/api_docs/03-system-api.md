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
