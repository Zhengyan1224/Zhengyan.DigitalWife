---
id: save
title: Save 存档 API
category: 存档
objects:
  - RuntimeSaveStore
  - scene.save
keywords:
  - save
  - json
  - saves
---

# Save 存档 API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Save 存档 API |
| 分类 | 存档 |
| 主要对象 | ``RuntimeSaveStore``, ``scene.save`` |
| C# 入口 | `Scene.Save.WriteText/ReadText` |
| Python 入口 | `scene.save.write_text/read_text` |
| 说明 | 受限 saves 目录下的文本/JSON 存档读写和路径安全。 |

## API 内容

存档目录固定在游戏工程目录下的 `saves/`。API 传入文件名或 `saves/` 下相对路径，例如 `slot1.json`、`chapter1/slot1.json`。运行时会阻止 `../` 逃出 `saves/`。

C#：

```csharp
if (IsStart)
{
    var data = new
    {
        x = Entity.Position.X,
        y = Entity.Position.Y,
        z = Entity.Position.Z
    };

    Scene.Save.WriteJson("slot1.json", data);
}

if (Scene.Save.Exists("slot1.json"))
{
    string raw = Scene.Save.ReadText("slot1.json");
    Console.WriteLine(raw);
}
```

Python：

```python
def start(entity, scene, input, audio):
    scene.save.write_json("slot1.json", {
        "player": {
            "x": entity.position[0],
            "y": entity.position[1],
            "z": entity.position[2],
        }
    })

    data = scene.save.read_json("slot1.json", fallback={})
    print(data)
```

API：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Save.SaveDirectory` | `scene.save.directory` | 存档目录。 |
| `WriteText(fileName, text)` | `write_text(file_name, text)` | 写文本。 |
| `ReadText(fileName, fallback)` | `read_text(file_name, fallback="")` | 读文本。 |
| `WriteJson<T>(fileName, value)` | `write_json(file_name, value)` | 写 JSON。 |
| `ReadJson<T>(fileName, fallback)` | `read_json(file_name, fallback=None)` | 读 JSON。 |
| `Exists(fileName)` | `exists(file_name)` | 文件是否存在。 |
| `Delete(fileName)` | `delete(file_name)` | 删除存档。 |
| `GetFullPath(fileName)` | 无 | 获取完整路径。 |

Python 的 `scene.save.directory` 是只读路径字符串；直接使用 Python `open()` 不会自动限制到 `saves/`，需要脚本自行保证路径安全。推荐优先使用 `scene.save.*` 方法。
