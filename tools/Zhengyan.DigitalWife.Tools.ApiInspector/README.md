# ApiInspector：检查第三方库的实际公开 API

这是维护者使用的反射工具，列出仓库所引用 SherpaOnnx、Whisper.net、PortAudio 程序集的类型、构造器、方法、属性和字段，便于对照依赖版本排查适配代码。

它不是游戏脚本 API 页面，也不调用语音服务。写游戏脚本请用 [script_api.html](../../src/Zhengyan.DigitalWife.GameEditor/script_api.html)。

## 运行

仓库根目录：

```text
dotnet run --project tools/Zhengyan.DigitalWife.Tools.ApiInspector
```

需要 .NET 10 SDK；首次构建会还原依赖。输出较多，可以重定向到本地文件：

```powershell
dotnet run --project tools/Zhengyan.DigitalWife.Tools.ApiInspector > artifacts-api.txt
```

输出反映实际恢复的程序集，不代表其它机器或其它 NuGet 版本的 API。筛选哪些类型由本目录 Program.cs 中的过滤列表决定。

## 适用场景

- 更新依赖后确认参数名与重载。
- 排查某个模型配置类型是否存在。
- 对照本仓库适配器和第三方实际二进制。

只检查元数据，不验证 native library、GPU 或真实录音。对应运行验证看 [AssistantConsole](../../samples/Zhengyan.DigitalWife.Samples.AssistantConsole/README.md)。

- [项目总览](../../README.md)
