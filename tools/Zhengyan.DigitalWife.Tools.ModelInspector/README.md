# ModelInspector：查看模型压缩包内容

这个工具列出 tar.bz2 归档的前 40 个条目，帮助确认下载的模型目录、词表和权重文件叫什么。它读取归档，不执行模型推理，也不是 PMX 查看器。

普通用户通常不需要它；维护模型下载脚本或排查资源路径时有用。

## 运行

在仓库根目录，安装 .NET 10 SDK 后执行：

```text
dotnet run --project tools/Zhengyan.DigitalWife.Tools.ModelInspector -- "artifacts/downloads/model.tar.bz2"
```

替换为实际 tar.bz2 文件路径。当前实现固定按 BZip2 解压，不是任意 ZIP / 7z 格式检查器。

输出每个条目的路径与是否目录。只列出前 40 项；没有看到某个文件不代表整个归档中没有它。

## 下一步

确认真实文件名后更新 ASR/TTS 配置。下载/解压用 [ModelInstaller](../Zhengyan.DigitalWife.Tools.ModelInstaller/README.md)；查看 PMX 特性用 [PmxParity.Tests](../../tests/Zhengyan.DigitalWife.PmxParity.Tests/README.md)。

- [项目总览](../../README.md)
