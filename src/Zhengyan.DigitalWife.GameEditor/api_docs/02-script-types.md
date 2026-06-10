---
id: script-types
title: 脚本类型
category: 基础
objects:
  - C# .csx
  - Python .py
keywords:
  - csx
  - python
  - imports
  - runtime
---

# 脚本类型

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 脚本类型 |
| 分类 | 基础 |
| 主要对象 | ``C# .csx``, ``Python .py`` |
| C# 入口 | `.csx globals` |
| Python 入口 | `.py event functions` |
| 说明 | C# 与 Python 脚本运行环境、默认导入和快照模型。 |

## API 内容

C# 脚本：

- 文件扩展名：`.csx`。
- 运行环境：Roslyn C# Script。
- 默认导入：`System`、`System.Collections.Generic`、`System.Globalization`、`System.IO`、`System.Linq`、`System.Net`、`System.Net.Http`、`System.Net.Sockets`、`System.Numerics`、`System.Text`、`System.Text.Json`、`System.Text.RegularExpressions`、`System.Threading`、`System.Threading.Tasks`、`Zhengyan.DigitalWife.GamePlayer`。
- 默认可访问全局对象：`Entity`、`Scene`、`Input`、`Audio`。

Python 脚本：

- 文件扩展名：`.py`。
- 运行环境：系统 `python` 或 `python3` 进程。
- 预置标准库模块：`math`、`random`、`re`、`json`、`datetime`、`time`、`statistics`。
- 仍然可以在脚本中正常 `import` 标准库或当前 Python 环境已安装的第三方包。
- Python 脚本通过桥接命令修改 GamePlayer 状态。
- Python 脚本中的对象属性大多是事件开始时的快照；例如调用 `entity.set_position(...)` 后，当前函数内的 `entity.position` 不会立刻更新，要到下一次事件快照才会反映。
