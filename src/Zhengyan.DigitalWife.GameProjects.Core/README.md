# GameProjects.Core：不依赖桌面渲染的工程格式

这是 Android 与共享场景运行时使用的工程数据层。它通过 `Compile Include / Link` 复用 [GameProjects](../Zhengyan.DigitalWife.GameProjects/README.md)的工程、路径、包和布局源码，避免把整套桌面渲染依赖带进平台宿主。

这是 .NET 10 类库，不能直接运行。制作游戏请从[编辑器](../Zhengyan.DigitalWife.GameEditor/README.md)开始。

## 包含什么

- 工程与场景 JSON 模型及读写。
- `.dwgame` 打包、加密、分片和解包。
- 资源路径与 GUI / Sprite 布局计算。
- Android 兼容性报告。
- Collider 几何。
- Android 脚本兼容类型。

## 开发时注意什么

共享实现主要位于 `src/Zhengyan.DigitalWife.GameProjects/`。修改链接源文件会同时影响本库与桌面库；不要另复制一份同名实现造成行为分叉。

本库与桌面 GameProjects 存在同名命名空间和类型。编辑器同时需要两者时使用项目引用别名，Android 工程优先使用 Core 契约。按目标宿主选择依赖，不直接混传两个程序集中的 GameProject 对象。

## 构建与验证

在仓库根目录：

```text
dotnet build src/Zhengyan.DigitalWife.GameProjects.Core
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "Editor precompile"
```

检查工程格式变更时还应运行[编辑器多场景回归](../../tests/Zhengyan.DigitalWife.GameEditor.Tests/README.md)，并确认旧工程和加密包仍可读。

- [项目总览](../../README.md)
- [共享场景运行时](../Zhengyan.DigitalWife.GamePlayer.Runtime.Core/README.md)
