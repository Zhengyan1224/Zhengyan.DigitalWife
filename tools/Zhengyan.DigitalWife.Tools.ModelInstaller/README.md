# ModelInstaller：下载和解压语音模型

这是模型下载脚本使用的小工具，可以下载文件，或下载并解压 tar.bz2。普通用户优先使用 [download-models.ps1](../../scripts/download-models.ps1) / [download-models.sh](../../scripts/download-models.sh)，不用自己逐条指定模型地址。

它不训练模型，也不部署 LLM。下载是否成功、模型许可与文件布局需要按来源确认。

## 常规入口

在仓库根目录：

```powershell
./scripts/download-models.ps1
```

Windows 脚本可用 SkipAsr / SkipWake / SkipTts / SkipWhisper 跳过不需要的权重。默认文件写入 models/，压缩包缓存到 artifacts/downloads/。

## 手动调用

需要 .NET 10 SDK 和网络。下面是命令格式，尖括号内容应替换成真实参数，不要原样粘贴：

```text
dotnet run --project tools/Zhengyan.DigitalWife.Tools.ModelInstaller -- download-file <url> <destination>
dotnet run --project tools/Zhengyan.DigitalWife.Tools.ModelInstaller -- download-and-extract-tarbz2 <url> <targetDirectory> [downloadsRoot]
```

已有目标文件会被跳过。下载中断时检查文件完整性，不要把“文件已存在”当作校验通过。解压可能覆盖目标目录中的同名文件，应选专门的模型目录。

## 遇到问题

先检查 URL、网络、可写目录、磁盘空间和压缩格式。模型推理报错还需确认 tokens、字典、encoder/decoder 等文件是否完整，不能只看下载命令退出。

- [项目总览](../../README.md)
- [Sherpa 模型](../../src/Zhengyan.DigitalWife.Speech.SherpaOnnx/README.md)
- [Whisper 模型](../../src/Zhengyan.DigitalWife.Speech.WhisperNet/README.md)
