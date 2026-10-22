# GameProjects：游戏工程与发布包格式

这个库定义 GameEditor 与 PC GamePlayer 共同使用的工程数据、场景保存、资源路径和 `.dwgame` 包格式。它不是编辑器界面，也不是可以直接启动的播放器。

普通游戏制作者可通过 [GameEditor](../Zhengyan.DigitalWife.GameEditor/README.md)完成这些操作；只有扩展工程格式、编写转换工具或自定义运行器时才需要直接使用本库。

## 负责哪些内容

| 入口 | 作用 |
| --- | --- |
| `GameProject / GameProjectScene / GameEntity` | 工程、场景、实体和各类配置 |
| `GameProjectStore` | 加载/保存 `game.project.json` 和场景文件 |
| `GameProjectPath` | 工程路径、`project:`、`app:` 和资源复制 |
| `GameProjectPackage` | 内容打包、加密、分片、解包与缓存 |
| `LayoutResolver / SpriteLayoutResolver` | GUI / Sprite 布局和命中计算 |
| `AndroidProjectCompatibility` | 分析 Android 发布差异 |
| `CollisionGeometry` | Collider 几何与查询基础 |
| `SceneVmdAnimationController` | 场景相机等 VMD 播放辅助 |

游戏内容通常是以下目录：

```text
MyGame/
  game.project.json
  scenes/main.scene.json
  assets/
  scripts/
```

## 在自己的工具中读取工程

从仓库根目录把本库加入现有 .NET 10 项目：

```text
dotnet add MyApp/MyApp.csproj reference src/Zhengyan.DigitalWife.GameProjects/Zhengyan.DigitalWife.GameProjects.csproj
```

`MyApp` 是你自己的项目路径。以下代码在该项目中使用：

```csharp
using Zhengyan.DigitalWife.GameProjects;

string directory = "D:/Games/MyGame"; // 改为实际工程目录
GameProject project = GameProjectStore.Load(directory);
Console.WriteLine(project.Name);
GameProjectScene startup = GameProjectStore.LoadScene(directory, project.DefaultScene);
Console.WriteLine(startup.Entities.Count);
```

`Save` 会写入工程文件；导出工具应先备份或使用工程副本。读取工程不等于执行其中的脚本。

## 工程与包

`.dwgame` 是内容包，需要播放器运行。包的 `Password`、`SplitPartSizeBytes` 和 `IncludeSaves` 在 `GameProjectPackageBuildOptions` 中配置。它们不代替 PC / Android 的脚本预编译；预编译由编辑器完成。

开发目录默认使用目录内存档；包的存档独立于解包目录。不要让存档依赖临时解包路径，运行器应使用包会话返回的 SaveDirectory。

## 与 GameProjects.Core 的关系

[GameProjects.Core](../Zhengyan.DigitalWife.GameProjects.Core/README.md)以链接源码方式复用工程格式，供 Android 和轻量运行时使用，减少桌面渲染依赖。两个程序集有同名类型；同一编译上下文需使用项目别名或明确的依赖边界，不能把类型随意互换。

修改共享数据结构后，应同时验证桌面保存、Android 读取和包解包。详情见[测试入口](../../tests/Zhengyan.DigitalWife.GameEditor.Tests/README.md)。

- [项目总览](../../README.md)
- [路径与资源规则](../Zhengyan.DigitalWife.GameEditor/api_docs/01-paths.md)
