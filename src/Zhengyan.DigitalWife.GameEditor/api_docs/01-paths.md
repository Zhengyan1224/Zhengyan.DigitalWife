---
id: paths
title: 路径规则
category: 基础
objects:
  - GameProjectPath
keywords:
  - path
  - project:
  - app:
  - rt:
  - assets
---

# 路径规则

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 路径规则 |
| 分类 | 基础 |
| 主要对象 | ``GameProjectPath`` |
| C# 入口 | `GameProjectPath.ToAbsolute(...)` |
| Python 入口 | `project/app/rt path strings` |
| 说明 | 工程相对路径、project/app/rt 虚拟前缀和资源复制规则。 |

## API 内容

GameEditor 保存项目时会尽量把外部脚本和资源复制到游戏工程目录下。脚本运行时建议只引用工程内路径。

| 写法 | 说明 |
| --- | --- |
| `assets/audio/bgm.ogg` | 工程相对路径，推荐写法。 |
| `project:assets/audio/bgm.ogg` | 显式从游戏工程目录解析。 |
| `app:Resources/Skybox/xxx.jpg` | 从 GamePlayer 程序目录解析，适合引擎自带基础资源。 |
| `rt:MiniMapRT` | Render Texture 引用，不是磁盘文件。 |

资源复制规则：

- PMX、音频、动作、贴图、脚本、TTS 模型文件和目录在保存项目时会复制到工程目录。
- 同一个源文件或源目录在同一次保存中只会复制一份，多个配置引用同一目录时会复用同一个工程内路径。
- `app:` 和 `rt:` 不会被复制；`app:` 指向程序自带资源，`rt:` 指向运行时纹理。
- Python 存档 API 只能访问 `saves/`，不能读取任意工程文件。
